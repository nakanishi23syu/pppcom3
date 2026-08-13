using DicomTool.DicomScp.Services;
using DicomTool.Shared.Constants;
using DicomTool.Shared.Data;
using FellowOakDicom;
using Microsoft.EntityFrameworkCore;

// ==========================================================================================
// Program.cs ― DicomTool.DicomScp のエントリポイント
// ==========================================================================================
// このプロセスは「1つのASP.NET Coreアプリ」でありながら、性質の異なる2つの待受を同時に持つ:
//
//   ① 管理用REST API (HTTP, ポート8090) … 通常のASP.NET Core Kestrelが待ち受ける、
//      人間やブラウザ(Swagger UI)向けのおなじみのHTTP+JSONインターフェース。
//   ② DICOM SCP(DIMSE)リスナー (TCP, ポート11112) … fo-dicomのDicomServerが、
//      ①とは全く別のTCPポートで、DICOM独自のバイナリプロトコルを待ち受ける。
//      IHostedService(DicomScpHostedService)として登録することで、
//      ASP.NET CoreのGeneric Hostが①と同じライフサイクル(起動/停止)で②の面倒も見てくれる。
var builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------------------------------------
// 管理用REST APIの待受ポートを明示的に固定する。
// --------------------------------------------------------------------------------
// docs/CONTRACT.md 1章のとおり、appsettings.jsonにポート番号をベタ書きするのはASP.NET Coreの
// 制約上避けられないケースが多いが、Program.cs(C#コード)であれば
// shared/DicomTool.Shared/Constants/ServicePorts.cs の定数を直接参照できるため、
// 「唯一の正」である定数からポート番号がズレる事故を防げる。
builder.WebHost.UseUrls($"http://0.0.0.0:{ServicePorts.DicomScpManagementHttp}");

// --------------------------------------------------------------------------------
// fo-dicomのDI統合を登録する。
// --------------------------------------------------------------------------------
// AddFellowOakDicom() を呼ぶことで、IDicomServerFactory(SCP=待受側を作る)・
// IDicomClientFactory(SCU=送信側を作る)などがDIコンテナに登録され、
// DicomScpService/DicomScuTestServiceのコンストラクタで受け取れるようになる。
builder.Services.AddFellowOakDicom();

// --------------------------------------------------------------------------------
// DicomDbContext のDI登録(C-FIND/C-MOVE SCPが登録済みStudy/Series/Sopを検索するために使う)。
// --------------------------------------------------------------------------------
// services/DicomTool.Worker/Program.csと同じパターン。マイグレーション適用(db.Database.Migrate())は
// docs/CONTRACT.md 7章の規約によりbackend/DicomTool.Apiだけが行うため、ここでは呼び出さない
// (＝backend/DicomTool.Apiを先に一度起動してスキーマを作成しておく必要がある)。
var connectionString = builder.Configuration.GetConnectionString("Dicom")
    ?? throw new InvalidOperationException(
        "接続文字列 ConnectionStrings:Dicom が設定されていません。" +
        "appsettings.Development.json（本番はappsettings.Production.json）または" +
        "環境変数 ConnectionStrings__Dicom を確認してください。");

builder.Services.AddDbContext<DicomDbContext>(options => options.UseNpgsql(connectionString));

// C-FIND/C-MOVEの検索条件解釈(DBクエリ)を担当。DicomDbContextを使うためScoped
// (fo-dicomは1接続ごとに新しいDIスコープを作るため、DicomScpServiceのコンストラクタで
// そのまま安全に受け取れる。Services/DicomScpService.cs参照)。
builder.Services.AddScoped<IDicomQueryService, DicomQueryService>();

// C-MOVEの転送先AEタイトル → host:port の対応表。appsettings.jsonの"RemoteAeTitles"セクションを
// 読み込むだけなので状態を持たず、Singletonで使い回してよい。
builder.Services.AddSingleton<IRemoteAeRegistry, RemoteAeRegistry>();

// Temporalワークフロー起動役。C-STORE受信のたびに使うのでシングルトンで使い回す
// (内部でTemporal Serverへの接続(gRPCチャネル)も一度確立したら使い回すよう実装している)。
builder.Services.AddSingleton<ITemporalWorkflowStarter, TemporalWorkflowStarter>();

// SCU自己疎通テストのロジック本体。管理用REST APIのエンドポイントから呼ばれる。
builder.Services.AddSingleton<IDicomScuTestService, DicomScuTestService>();

// DICOM SCP(DIMSE)リスナーをバックグラウンドサービスとして登録する。
// アプリ起動(app.Run())と同時にStartAsyncが呼ばれ、ポート11112での待受が始まる。
builder.Services.AddHostedService<DicomScpHostedService>();

// --------------------------------------------------------------------------------
// Swagger(OpenAPI)まわり。
// --------------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "DicomTool.DicomScp 管理用API",
        Version = "v1",
        Description =
            "DICOM SCU/SCPサービス(DicomTool.DicomScp)の管理用REST API。" +
            "DICOM本体の通信(C-ECHO/C-STORE)はこのAPIとは別のTCPポート(11112, DIMSE)で行われる。" +
            "このAPIはあくまで「状態確認」「自己疎通テストの起動」といった、人間向けの補助窓口。",
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "DicomTool.DicomScp 管理用API v1");
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
.WithDescription("このプロセス(管理用REST API)が生きているかどうかを返す。DIMSEリスナー(ポート11112)自体の生存確認にはならない点に注意。");

// ==========================================================================================
// GET /config ― 現在のAEタイトル・ポート等の設定を返す
// ==========================================================================================
app.MapGet("/config", () => Results.Ok(new
{
    ownAeTitle = DicomNetworkConstants.OwnAeTitle,
    testScuAeTitle = DicomNetworkConstants.TestScuAeTitle,
    dicomScpDimsePort = ServicePorts.DicomScpDimse,
    managementHttpPort = ServicePorts.DicomScpManagementHttp,
    temporalTaskQueue = TemporalConstants.TaskQueue,
    uploadWorkflowTypeName = TemporalConstants.UploadDicomWorkflowTypeName,
}))
.WithName("GetConfig")
.WithSummary("現在の設定を表示")
.WithDescription("AEタイトル・ポート番号・Temporalの識別子等、docs/CONTRACT.md 4章・5章で定義されている値を実際に読み込んで返す。設定ミスの切り分けに使う。");

// ==========================================================================================
// POST /test/c-echo ― 自己疎通テスト(C-ECHO)
// ==========================================================================================
app.MapPost("/test/c-echo", async (IDicomScuTestService scuTestService, CancellationToken cancellationToken) =>
{
    var result = await scuTestService.RunCEchoTestAsync(cancellationToken);
    return result.Success ? Results.Ok(result) : Results.Json(result, statusCode: StatusCodes.Status502BadGateway);
})
.WithName("TestCEcho")
.WithSummary("自己疎通テスト(C-ECHO)を実行")
.WithDescription("このプロセス自身がSCUとなり、自分自身のSCP(localhost:11112)へC-ECHOを送信する。" +
                  "アソシエーション確立からDICOM Ping応答受信までが正常に行えるかを確認する、Temporalに依存しないテスト。");

// ==========================================================================================
// POST /test/c-store ― 自己疎通テスト(C-STORE)
// ==========================================================================================
app.MapPost("/test/c-store", async (IDicomScuTestService scuTestService, CancellationToken cancellationToken) =>
{
    var result = await scuTestService.RunCStoreTestAsync(cancellationToken);
    return result.Success ? Results.Ok(result) : Results.Json(result, statusCode: StatusCodes.Status502BadGateway);
})
.WithName("TestCStore")
.WithSummary("自己疎通テスト(C-STORE)を実行")
.WithDescription("このプロセス自身がSCUとなり、SampleData配下のサンプルDICOMファイルを自分自身のSCP(localhost:11112)へC-STORE送信する。" +
                  "成功すればSCP側でステージング領域への保存とUploadDicomWorkflowの起動が行われる" +
                  "(Worker未実装でもTemporal Serverへのワークフロー起動自体は成功する＝docs/CONTRACT.md 4章の疎結合設計)。");

app.Run();
