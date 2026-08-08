using DicomTool.Shared.Constants;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace DicomTool.DicomScp.Services;

// ==========================================================================================
// DicomScuTestService ― 自己疎通テスト用のSCU(Service Class User＝送信側/クライアント)実装
// ==========================================================================================
// 本番のPACS運用では「外部のモダリティ(CT/MRI等)がSCUとなって、このサービスにC-STOREを送ってくる」
// のが主な使われ方だが、開発中に外部モダリティを毎回用意するのは大変なので、
// 「このプロセス自身がSCUにもなって、自分自身(localhost:11112)のSCPへC-ECHO/C-STOREを送る」
// というループバックの自己疎通テストを用意する。管理用REST API(/test/c-echo, /test/c-store)から呼ばれる。
//
// 【SCUとしての一連の流れ】
//   1. DicomClient を生成する(まだTCP接続はしていない。リクエストを積み上げるだけの器)。
//   2. AddRequestAsync(...) で送りたいDIMSE要求(C-ECHO要求 / C-STORE要求)を器に積む。
//   3. SendAsync() を呼んだ瞬間に初めて実際のTCP接続とアソシエーション確立(A-ASSOCIATE-RQ)が行われ、
//      プレゼンテーションコンテキストのネゴシエーションが行われた上で、積んでおいた要求が
//      実際にDIMSEメッセージとして送信される。
//   4. 相手(SCP)からの応答(C-ECHO-RSP / C-STORE-RSP)を受け取ると、リクエストに登録しておいた
//      コールバック(OnResponseReceived)が呼ばれる。
//   5. すべての要求を送り終えると、DicomClientは自動的にA-RELEASE-RQを送ってアソシエーションを
//      正式に解放し、TCP接続を閉じる。
public interface IDicomScuTestService
{
    Task<DicomScuTestResult> RunCEchoTestAsync(CancellationToken cancellationToken = default);

    Task<DicomScuTestResult> RunCStoreTestAsync(CancellationToken cancellationToken = default);
}

/// <summary>自己疎通テスト1回分の結果。管理用REST APIのレスポンスとしてそのままJSON化される。</summary>
public sealed record DicomScuTestResult(bool Success, string? DicomStatusCode, string? DicomStatusDescription, string Message);

public sealed class DicomScuTestService : IDicomScuTestService
{
    // ループバック接続なので宛先ホストは常に自分自身(localhost)。
    private const string TargetHost = "127.0.0.1";

    private readonly IDicomClientFactory _dicomClientFactory;
    private readonly ILogger<DicomScuTestService> _logger;
    private readonly IHostEnvironment _hostEnvironment;

    public DicomScuTestService(
        IDicomClientFactory dicomClientFactory,
        ILogger<DicomScuTestService> logger,
        IHostEnvironment hostEnvironment)
    {
        _dicomClientFactory = dicomClientFactory;
        _logger = logger;
        _hostEnvironment = hostEnvironment;
    }

    // ======================================================================================
    // C-ECHO自己疎通テスト
    // ======================================================================================
    public async Task<DicomScuTestResult> RunCEchoTestAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "C-ECHO自己疎通テストを開始します。CallingAE={CallingAE} -> CalledAE={CalledAE} ({Host}:{Port})",
            DicomNetworkConstants.TestScuAeTitle, DicomNetworkConstants.OwnAeTitle, TargetHost, ServicePorts.DicomScpDimse);

        // IDicomClientFactory.Create(host, port, useTls, callingAe, calledAe)
        //   ・callingAe … 自分(SCU)が名乗るAEタイトル。ここではDicomNetworkConstants.TestScuAeTitle。
        //   ・calledAe  … 接続先(SCP)に期待するAEタイトル。DicomScpService.OnReceiveAssociationRequestAsync
        //                 側で「Called AEが自分(OwnAeTitle)と一致するか」を検証しているので、
        //                 ここが一致していないとA-ASSOCIATE-RJで拒否される。
        var client = _dicomClientFactory.Create(
            TargetHost,
            ServicePorts.DicomScpDimse,
            useTls: false,
            callingAe: DicomNetworkConstants.TestScuAeTitle,
            calledAe: DicomNetworkConstants.OwnAeTitle);

        DicomStatus? receivedStatus = null;

        var request = new DicomCEchoRequest
        {
            OnResponseReceived = (_, response) => receivedStatus = response.Status,
        };

        await client.AddRequestAsync(request).ConfigureAwait(false);

        // SendAsync()の呼び出しで実際にTCP接続→アソシエーション確立→C-ECHO送信→応答受信→
        // アソシエーション解放、までが一気に行われる。
        await client.SendAsync(cancellationToken).ConfigureAwait(false);

        var success = receivedStatus == DicomStatus.Success;
        _logger.LogInformation("C-ECHO自己疎通テストが完了しました。Status={Status}", receivedStatus);

        return new DicomScuTestResult(
            Success: success,
            DicomStatusCode: receivedStatus?.Code.ToString(),
            DicomStatusDescription: receivedStatus?.Description,
            Message: success
                ? "C-ECHO疎通確認に成功しました(DICOM Ping OK)。アソシエーション確立→C-ECHO送受信→解放まで正常に完了しています。"
                : "C-ECHO疎通確認に失敗しました。SCP側のログ(コンソール出力)も確認してください。");
    }

    // ======================================================================================
    // C-STORE自己疎通テスト
    // ======================================================================================
    public async Task<DicomScuTestResult> RunCStoreTestAsync(CancellationToken cancellationToken = default)
    {
        // SampleData配下のサンプルDICOMファイルを1つ送信対象として読み込む。
        // csproj側で <CopyToOutputDirectory> を設定しているため、ビルド出力ディレクトリ
        // (=ContentRootPath)直下のSampleDataフォルダに存在する。
        var samplePath = Path.Combine(_hostEnvironment.ContentRootPath, "SampleData", "sample1.dcm");
        if (!File.Exists(samplePath))
        {
            var message = $"サンプルDICOMファイルが見つかりません: {samplePath}";
            _logger.LogError(message);
            return new DicomScuTestResult(false, null, null, message);
        }

        _logger.LogInformation(
            "C-STORE自己疎通テストを開始します。送信ファイル={SamplePath}, CallingAE={CallingAE} -> CalledAE={CalledAE} ({Host}:{Port})",
            samplePath, DicomNetworkConstants.TestScuAeTitle, DicomNetworkConstants.OwnAeTitle, TargetHost, ServicePorts.DicomScpDimse);

        var dicomFile = await DicomFile.OpenAsync(samplePath).ConfigureAwait(false);

        var client = _dicomClientFactory.Create(
            TargetHost,
            ServicePorts.DicomScpDimse,
            useTls: false,
            callingAe: DicomNetworkConstants.TestScuAeTitle,
            calledAe: DicomNetworkConstants.OwnAeTitle);

        DicomStatus? receivedStatus = null;

        // DicomCStoreRequest(DicomFile) は、渡されたファイルのSOP Class UID / SOP Instance UID /
        // 転送構文を自動的に読み取り、それに合ったプレゼンテーションコンテキストをアソシエーション
        // 確立時に提案してくれる(＝呼び出し側が個別にSOP Classや転送構文を指定する必要はない)。
        var request = new DicomCStoreRequest(dicomFile)
        {
            OnResponseReceived = (_, response) => receivedStatus = response.Status,
        };

        await client.AddRequestAsync(request).ConfigureAwait(false);
        await client.SendAsync(cancellationToken).ConfigureAwait(false);

        var success = receivedStatus == DicomStatus.Success;
        _logger.LogInformation("C-STORE自己疎通テストが完了しました。Status={Status}", receivedStatus);

        return new DicomScuTestResult(
            Success: success,
            DicomStatusCode: receivedStatus?.Code.ToString(),
            DicomStatusDescription: receivedStatus?.Description,
            Message: success
                ? "C-STORE送信に成功しました。SCP側でステージング領域への保存とUploadDicomWorkflow起動が行われているはずです(Temporal Web UI: http://localhost:8233 で確認可能)。"
                : "C-STORE送信に失敗しました。SCP側のログ(コンソール出力)も確認してください。");
    }
}
