using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using Temporalio.Exceptions;

namespace DicomTool.Worker.Activities;

// ======================================================
// CheckStorageCapacityActivity — 保存先ストレージの空き容量を確認し、足りなければ即座に処理を止める
// ======================================================
// UploadDicomWorkflowの先頭(SaveToStorageActivityより前)で呼び出される想定のActivity。
//
// 【なぜこの判定ロジック自体をWorkerの中に直接書かず、別プロセス(DicomTool.StorageGuard)へ
//   HTTP経由で問い合わせる設計にしたのか】
// 容量チェック自体は「DriveInfoを見るだけ」の単純な処理であり、Worker内に直接書いても
// 技術的には成立する。それでもあえて別サービスに切り出したのは、
// services/DicomTool.StorageGuard/Program.cs冒頭のコメントで詳しく説明している通り、
//   ・容量チェックという責務を、Workerの「DICOMファイルを保存する」という関心事から分離する
//   ・将来、他のワークフローや機能からも同じ容量チェックを再利用できるようにする
// という狙いがあるため。このActivityは、その独立したサービスをTemporalワークフローの
// 文脈から呼び出すための「薄いアダプター」に徹する（判定ロジックそのものは持たない）。
//
// 【なぜHttpClientをコンストラクタで直接受け取らず、IHttpClientFactoryを使うのか】
// HttpClientを毎回newすると、ソケットが解放されずに枯渇する（いわゆるソケット枯渇問題）ことが
// 知られている。IHttpClientFactory(Microsoft.Extensions.Http)を使うと、内部でHttpClientの
// 生成・使い回し・DNS変更への追従を面倒みてくれる。Program.cs側で
// builder.Services.AddHttpClient(StorageGuardHttpClientName, ...) として名前付き登録しており、
// このActivityではその名前を指定してCreateClient(...)するだけでよい。
public sealed class CheckStorageCapacityActivity(
    IHttpClientFactory httpClientFactory,
    ILogger<CheckStorageCapacityActivity> logger)
{
    // Program.csのAddHttpClient(...)呼び出しと対にする名前付きHttpClientの名前。
    // 名前をこの定数経由で共有することで、Program.cs側とActivity側で名前がズレる事故を防ぐ。
    public const string StorageGuardHttpClientName = "StorageGuard";

    // DicomTool.StorageGuardが返すJSONの形（Program.cs GET /capacity/check参照）を
    // このActivity内でだけ使うためのレスポンスDTO。
    //
    // 【あえてshared/DicomTool.Shared/ContractsにDTOを置かず、ここでプライベートに定義する理由】
    // shared/DicomTool.Shared/Contracts配下の型(UploadDicomWorkflowInput等)は
    // 「同じプロセス内(Temporal Workflow⇔Activity間)でシリアライズされるTemporal Payloadの型」
    // という役割を持つ。一方このDTOは、HTTP越しの別プロセス(DicomTool.StorageGuard)との
    // JSON契約であり、性質が異なる（＝サービスの境界を越えるときの契約はHTTP+JSONそのものであり、
    // C#の型を共有する必要はない。むしろ共有してしまうと、片方の型を変えるともう片方の
    // ビルドまで壊れるという不要な結合が生まれてしまう）。
    private sealed record CapacityCheckResponse(
        bool Ok,
        double FreePercent,
        long FreeBytes,
        long TotalBytes,
        string Message);

    [Activity("CheckStorageCapacity")]
    public async Task CheckAsync()
    {
        using var httpClient = httpClientFactory.CreateClient(StorageGuardHttpClientName);

        logger.LogInformation("保存先ストレージの空き容量をStorageGuardへ問い合わせます: {BaseAddress}", httpClient.BaseAddress);

        CapacityCheckResponse? result;
        try
        {
            // 閾値(minFreePercent)はクエリパラメータを省略し、StorageGuard側の
            // appsettings.json(StorageGuard:MinFreePercentDefault)の既定値に判定を委ねる。
            // 「閾値の値そのもの」はStorageGuardの設定として一元管理し、Worker側では
            // 「聞きに行って、ダメだったら止める」という判断だけを持つ、という役割分担にしている。
            result = await httpClient.GetFromJsonAsync<CapacityCheckResponse>("/capacity/check");
        }
        catch (Exception ex) when (ex is not ApplicationFailureException)
        {
            // StorageGuardサービスが起動していない・ネットワークが繋がらない等は、
            // SaveToStorageActivityのディスクI/O失敗と同種の「一時的かもしれない」障害であるため、
            // ここではnonRetryableにせず、通常の例外のまま投げる。Temporal SDKがこれを
            // 自動的にリトライ可能なApplicationFailureExceptionへ変換してくれる
            // (Activities/SaveToStorageActivity.csの同種のコメント参照)ため、
            // UploadDicomWorkflow側で設定したRetryPolicyに従って再試行される。
            throw new Exception(
                $"StorageGuardサービスへの容量問い合わせに失敗しました({httpClient.BaseAddress}/capacity/check): {ex.Message}",
                ex);
        }

        if (result is null)
        {
            throw new Exception("StorageGuardサービスからの応答が空でした。");
        }

        if (!result.Ok)
        {
            // 容量不足は「時間を置いて再試行しても自然には直らない」種類の失敗
            // （ディスクが誰か・何かによって空にされない限り状況は変わらない）。
            // そのためApplicationFailureException(nonRetryable: true)を投げ、
            // Temporal側のRetryPolicyがどう設定されていても即座に失敗を確定させ、
            // 無駄なリトライでWorkerやStorageGuardに負荷をかけないようにする
            // （このnonRetryableの使い分けの考え方はSaveToStorageActivity.cs参照）。
            logger.LogWarning(
                "保存先ストレージの空き容量が不足しています: {Message} (空き容量率={FreePercent}%, 空き容量={FreeBytes}bytes, 総容量={TotalBytes}bytes)",
                result.Message, result.FreePercent, result.FreeBytes, result.TotalBytes);

            throw new ApplicationFailureException(
                $"保存先ストレージの空き容量が不足しているため、アップロード処理を拒否します: {result.Message}",
                nonRetryable: true);
        }

        logger.LogInformation(
            "保存先ストレージの空き容量は十分です: {Message} (空き容量率={FreePercent}%)",
            result.Message, result.FreePercent);
    }
}
