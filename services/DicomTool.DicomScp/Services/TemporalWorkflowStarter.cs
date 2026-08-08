using DicomTool.Shared.Constants;
using DicomTool.Shared.Contracts;
using Temporalio.Client;

namespace DicomTool.DicomScp.Services;

// ======================================================
// ITemporalWorkflowStarter / TemporalWorkflowStarter
// ======================================================
// docs/CONTRACT.md 4章で説明されている「型なしクライアントAPI」でTemporalワークフローを起動する薄いラッパー。
//
// 【なぜここでWorkflow実装クラス(UploadDicomWorkflow本体)を参照しないのか】
// このプロジェクト(DicomTool.DicomScp)は services/DicomTool.Worker を一切参照していない
// （csprojを見てもProjectReferenceが無いことが確認できる）。
// Temporal .NET SDKは「Task Queue名」と「Workflow Type名」という2つの文字列さえ分かれば、
// ワークフローの実装コードを型として知らなくても StartWorkflowAsync(string workflowTypeName, ...)
// という「文字列ベース(untyped)」のオーバーロードで起動できる。
// これは実務でよくある「他チームが実装したワークフロー/APIを、契約(インターフェース定義)だけ知って
// 呼び出す」状況を疑似体験するための、意図的な設計。
public interface ITemporalWorkflowStarter
{
    /// <summary>
    /// UploadDicomWorkflow(実装本体: services/DicomTool.Worker/Workflows/UploadDicomWorkflow.cs)を起動する。
    /// あくまで「起動を依頼するだけ」であり、実行結果(成功/失敗)を待たない(Fire-and-forgetに近い)。
    /// これはC-STORE応答をアソシエーション内で即座に返す必要があるDICOM側の作法と、
    /// Temporalが担う非同期の後処理(ストレージ確定保存＋DB登録)を分離するための意図的な設計。
    /// </summary>
    Task StartUploadDicomWorkflowAsync(UploadDicomWorkflowInput input, CancellationToken cancellationToken = default);
}

public sealed class TemporalWorkflowStarter : ITemporalWorkflowStarter
{
    private readonly ILogger<TemporalWorkflowStarter> _logger;
    private readonly IConfiguration _configuration;

    // Temporalサーバーへの接続(gRPCチャネル)は使い回すのが望ましいため、
    // 一度確立したクライアントをここにキャッシュする。SemaphoreSlimで初期化の競合を防ぐ。
    private ITemporalClient? _cachedClient;
    private readonly SemaphoreSlim _clientLock = new(1, 1);

    public TemporalWorkflowStarter(ILogger<TemporalWorkflowStarter> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task StartUploadDicomWorkflowAsync(UploadDicomWorkflowInput input, CancellationToken cancellationToken = default)
    {
        var client = await GetOrCreateClientAsync(cancellationToken).ConfigureAwait(false);

        // WorkflowId: Temporal上で個々のワークフロー実行を一意に識別するID。
        // 同じIDで再度起動しようとすると（デフォルト設定では）衝突エラーになる仕組みがあり、
        // 「同じ処理を誤って二重起動しない」ための保護に使える。
        // ここではSOPInstanceUID相当のステージングファイル名 + GUIDを組み合わせて一意性を確保する。
        var workflowId = $"upload-dicom-{input.StagingFileName}-{Guid.NewGuid():N}";

        _logger.LogInformation(
            "UploadDicomWorkflowを起動します。WorkflowId={WorkflowId}, TaskQueue={TaskQueue}, StagingFileName={StagingFileName}, SourceProtocol={SourceProtocol}",
            workflowId, TemporalConstants.TaskQueue, input.StagingFileName, input.SourceProtocol);

        // StartWorkflowAsync(string workflow, ...) が「型なしクライアントAPI」。
        // 第一引数はTemporalConstants.UploadDicomWorkflowTypeName(="UploadDicomWorkflow")という
        // 文字列であり、Worker側で [Workflow("UploadDicomWorkflow")] のように同じ名前が
        // 付けられたクラスがこの文字列と紐づけられて実行される（文字列の約束事だけで疎結合）。
        await client.StartWorkflowAsync(
            TemporalConstants.UploadDicomWorkflowTypeName,
            new object?[] { input },
            new WorkflowOptions(id: workflowId, taskQueue: TemporalConstants.TaskQueue)).ConfigureAwait(false);
    }

    private async Task<ITemporalClient> GetOrCreateClientAsync(CancellationToken cancellationToken)
    {
        if (_cachedClient is not null)
        {
            return _cachedClient;
        }

        await _clientLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedClient is not null)
            {
                return _cachedClient;
            }

            // appsettings.json の Temporal:Address / Temporal:Namespace を参照する。
            // 既定値はdocs/CONTRACT.md 4章の「Temporal Server接続先: localhost:7233（開発時）」と一致。
            var address = _configuration["Temporal:Address"] ?? "localhost:7233";
            var ns = _configuration["Temporal:Namespace"] ?? TemporalConstants.Namespace;

            _logger.LogInformation("Temporal Serverへ接続します。Address={Address}, Namespace={Namespace}", address, ns);

            _cachedClient = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions
            {
                TargetHost = address,
                Namespace = ns,
            }).ConfigureAwait(false);

            return _cachedClient;
        }
        finally
        {
            _clientLock.Release();
        }
    }
}
