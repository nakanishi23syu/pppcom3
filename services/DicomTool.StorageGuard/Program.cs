using DicomTool.Shared.Constants;

// ==========================================================================================
// Program.cs ― DicomTool.StorageGuard のエントリポイント
// ==========================================================================================
// 【このサービスが存在する理由 ―― なぜ「独立したマイクロサービス」にしたのか】
//
// 保存先ストレージ(infra/data/dicom-storage)の空き容量チェックは、一見するとごく小さな
// 処理であり、DicomTool.Worker(Temporalワーカー)の中に1関数として実装してしまうことも
// できる。しかし、あえてそうせず独立したプロセス／マイクロサービスとして切り出したのには
// 以下の狙いがある。
//
//   ① 責務の分離 ―― 「DICOMファイルをどう保存するか」というWorkerの本来の関心事と、
//      「保存先の物理ディスクがどれだけ空いているか」というインフラ寄りの関心事は、
//      変化する理由(変更頻度・変更のきっかけ)が異なる。前者はDICOM規格やワークフロー設計の
//      都合で変わり、後者はディスク増設・監視ポリシーの変更等、インフラ運用側の都合で変わる。
//      1つのプロセスに同居させると、片方の変更がもう片方に予期せず影響するリスクが増える。
//
//   ② 再利用性 ―― 容量チェックは UploadDicomWorkflow だけでなく、将来的には
//      DeleteDicomWorkflow(削除で空くはずの容量の検証)や、定期バッチによる容量アラート、
//      あるいはTrayApp側からの「今アップロードして大丈夫か」の事前確認等、
//      複数の呼び出し元から同じロジックを使い回したくなる可能性が高い。
//      独立したHTTP APIにしておけば、呼び出し元がTemporal Workflow経由かどうかに関わらず、
//      「HTTPで/capacity/checkを叩く」という単純な依存関係だけで再利用できる。
//
//   ③ 常駐という性質との相性 ―― このサービスは「VM上で常に起動している」ことが前提であり、
//      WorkerのようにTemporal Task Queueのポーリングというイベント駆動の生存サイクルとは
//      性質が異なる（＝いつ問い合わせても即座に今の空き容量を答えられる状態でいたい）。
//      無理にWorkerに同居させるより、単独のプロセスとして独立してデプロイ・再起動・
//      監視できるようにしておく方が運用上シンプル。
//
// 構成はDicomTool.DicomScpの管理用REST API(Program.cs参照)と同じパターンを踏襲する:
// ASP.NET Core Minimal API + Swashbuckle(Swagger UI)による、単純なHTTP+JSONサービス。
var builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------------------------------------
// 待受ポートを明示的に固定する。
// --------------------------------------------------------------------------------
// shared/DicomTool.Shared/Constants/ServicePorts.cs が「唯一の正」。
// appsettings.jsonにベタ書きせずここでC#の定数を直接参照することで、値のズレを防ぐ
// (DicomTool.DicomScp/Program.csの同じ箇所のコメントも参照)。
builder.WebHost.UseUrls($"http://0.0.0.0:{ServicePorts.StorageGuardHttp}");

// appsettings.json の "StorageGuard:MinFreePercentDefault" を、
// /capacity/check のクエリパラメータ省略時の既定閾値として使う。
// 未設定の場合は10%をコード側のフォールバック値とする。
var minFreePercentDefault = builder.Configuration.GetValue<double?>("StorageGuard:MinFreePercentDefault") ?? 10.0;

// --------------------------------------------------------------------------------
// Swagger(OpenAPI)まわり。DicomTool.DicomScpと同じ構成。
// --------------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "DicomTool.StorageGuard 管理用API",
        Version = "v1",
        Description =
            "保存先ストレージ(infra/data/dicom-storage)の空き容量を監視するマイクロサービス。" +
            "UploadDicomWorkflow(DicomTool.Worker側のCheckStorageCapacityActivity)から呼ばれる想定だが、" +
            "他のワークフローや将来の機能からも再利用できるよう、単純なHTTP+JSON APIとして独立させている。",
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "DicomTool.StorageGuard 管理用API v1");
});

// ==========================================================================================
// GET /health ― 生存確認
// ==========================================================================================
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    timestampUtc = DateTime.UtcNow,
}))
.WithName("Health")
.WithSummary("生存確認")
.WithDescription("このプロセスが生きているかどうかを返す。");

// ==========================================================================================
// GET /capacity ― 保存先ドライブの容量情報をそのまま返す
// ==========================================================================================
app.MapGet("/capacity", (IHostEnvironment env) =>
{
    var info = GetCapacityInfo(env);
    return Results.Ok(new
    {
        driveName = info.Drive.Name,
        storagePath = info.StoragePath,
        totalBytes = info.Drive.TotalSize,
        freeBytes = info.Drive.AvailableFreeSpace,
        freePercent = info.FreePercent,
    });
})
.WithName("GetCapacity")
.WithSummary("保存先ドライブの容量情報を取得")
.WithDescription(
    "StoragePaths.ResolveStoragePath(...)が指すフォルダが置かれているドライブについて、" +
    "DriveInfoクラスから取得できる総容量・空き容量・空き容量率(%)をそのまま返す。" +
    "閾値判定はしない、単なる現状確認用のエンドポイント（判定が欲しい場合は/capacity/checkを使う）。");

// ==========================================================================================
// GET /capacity/check ― 閾値を下回っていないかを判定する
// ==========================================================================================
app.MapGet("/capacity/check", (IHostEnvironment env, double? minFreePercent) =>
{
    // クエリパラメータ省略時は appsettings.json の既定値(StorageGuard:MinFreePercentDefault)を使う。
    // Workerからの定常的な呼び出しでは基本的に省略される想定で、閾値を毎回運用側の判断で
    // 変えたい場合(例: 一時的に厳しくする)にクエリパラメータで上書きできるようにしている。
    var threshold = minFreePercent ?? minFreePercentDefault;

    var info = GetCapacityInfo(env);
    var ok = info.FreePercent >= threshold;

    var message = ok
        ? $"空き容量は十分です（空き容量率 {info.FreePercent:F1}% ≧ 閾値 {threshold:F1}%）。"
        : $"空き容量が閾値を下回っています（空き容量率 {info.FreePercent:F1}% ＜ 閾値 {threshold:F1}%）。" +
          "後続のワークフロー処理は拒否してください。";

    return Results.Ok(new
    {
        ok,
        freePercent = info.FreePercent,
        freeBytes = info.Drive.AvailableFreeSpace,
        totalBytes = info.Drive.TotalSize,
        message,
    });
})
.WithName("CheckCapacity")
.WithSummary("空き容量が閾値を下回っていないかを判定")
.WithDescription(
    "minFreePercent(クエリパラメータ、%単位、省略時はappsettings.jsonのStorageGuard:MinFreePercentDefault、" +
    "さらに未設定の場合は10%)を下回っていないかを判定して { ok, freePercent, freeBytes, totalBytes, message } " +
    "を返す。DicomTool.WorkerのCheckStorageCapacityActivityがUploadDicomWorkflowの先頭でこれを呼び出し、" +
    "ok=falseなら以降の保存処理(SaveToStorageActivity等)を一切実行せずに早期に失敗させる。");

app.Run();

// --------------------------------------------------------------------------------
// ローカル関数: 保存先ストレージが乗っているドライブの情報をまとめて取得する。
// --------------------------------------------------------------------------------
// /capacity と /capacity/check の両方で同じ取得ロジックを使うため、重複を避けて共通化している。
static (DriveInfo Drive, string StoragePath, double FreePercent) GetCapacityInfo(IHostEnvironment env)
{
    var storagePath = DicomTool.Shared.Constants.StoragePaths.ResolveStoragePath(env.ContentRootPath);

    // ResolveStoragePath自体はフォルダを作らない(コメント参照)ため、まだ存在しない可能性がある。
    // DriveInfoはパスが実在しなくても「そのパス文字列が属するドライブ」さえ特定できればよいため、
    // Directory.CreateDirectoryは呼ばずにそのままDriveInfoへ渡す
    // (ドライブ自体が存在しない・パス形式が不正等の場合はDriveInfoの生成やプロパティアクセスで例外になる)。
    var drive = new DriveInfo(Path.GetPathRoot(storagePath) ?? storagePath);

    var freePercent = drive.TotalSize == 0
        ? 0.0
        : drive.AvailableFreeSpace * 100.0 / drive.TotalSize;

    return (drive, storagePath, freePercent);
}
