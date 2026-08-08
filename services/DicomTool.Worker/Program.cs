using DicomTool.Shared.Constants;
using DicomTool.Shared.Data;
using DicomTool.Worker.Activities;
using DicomTool.Worker.Workflows;
using Microsoft.EntityFrameworkCore;
using Temporalio.Extensions.Hosting;

// ======================================================
// Program.cs — DicomTool.Worker のエントリポイント
// ======================================================
// このプロセスは「HTTPサーバーを持たない、Temporal Serverに接続しに行くだけのバックグラウンド
// プロセス」である(docs/CONTRACT.md 1章)。Generic Host(Microsoft.Extensions.Hosting)の上に、
// 以下の2つの責務を乗せている:
//   1. DicomDbContext を PostgreSQL に向けてDI登録する(Activityがコンストラクタインジェクションで使う)
//   2. Temporal Worker を IHostedService としてDI登録し、Task Queueを継続的にポーリングさせる
var builder = Host.CreateApplicationBuilder(args);

// ------------------------------------------------------
// 1. DicomDbContext のDI登録
// ------------------------------------------------------
// 接続文字列は appsettings.Development.json の ConnectionStrings:Dicom、または
// 環境変数 ConnectionStrings__Dicom（ASP.NET Core標準の階層区切り）から読む
// (docs/CONTRACT.md 7章)。既定値はローカルDocker(docker-compose.yml)のPostgreSQLに向いている。
var connectionString = builder.Configuration.GetConnectionString("Dicom")
    ?? throw new InvalidOperationException(
        "接続文字列 ConnectionStrings:Dicom が設定されていません。" +
        "appsettings.Development.json または環境変数 ConnectionStrings__Dicom を確認してください。");

builder.Services.AddDbContext<DicomDbContext>(options => options.UseNpgsql(connectionString));
// ↑ ここで意図的に db.Database.Migrate() を呼び出していない。
// docs/CONTRACT.md 7章の規約により、マイグレーションの適用(スキーマ作成・更新)は
// backend/DicomTool.Api だけが行う責務であり、Workerは「スキーマは既に存在する」前提で
// DbContextを使う。理由は「複数プロセスが同時に同じDBへマイグレーションを試みると
// 競合(ロック待ち・スキーマ変更中の中間状態への読み書き)が起きうる」ため、
// マイグレーション実行者を1プロセスに一本化している。
// もしWorkerを先にDBスキーマが無い状態で起動すると、Activity内のクエリが
// テーブル不在エラーで失敗する（＝先にbackend/DicomTool.Apiを起動してマイグレーションを
// 適用しておく必要がある、という起動順の前提がここにある）。

// ------------------------------------------------------
// 2. Temporal Worker のDI登録
// ------------------------------------------------------
// Temporalio.Extensions.Hosting パッケージが提供する AddHostedTemporalWorker(...) は、
// 「Temporal Serverに接続するクライアントを内部で持ち、指定したTask Queueをポーリングし続ける
// IHostedServiceをDIコンテナに登録する」拡張メソッド。戻り値の Builder に対して
// .AddScopedActivities<T>() / .AddWorkflow<T>() をメソッドチェーンで呼び、
// 「このWorkerが処理できるActivity/Workflowの一覧」を登録していく
// (公式サンプル temporalio/samples-dotnet の src/DependencyInjection と同じパターン)。
var temporalAddress = builder.Configuration["Temporal:Address"] ?? "localhost:7233";

builder.Services
    .AddHostedTemporalWorker(
        clientTargetHost: temporalAddress,
        clientNamespace: TemporalConstants.Namespace,
        taskQueue: TemporalConstants.TaskQueue)
    // ── Activity登録 ──
    // AddScopedActivities<T>() は「Tのインスタンスを"Scoped"としてDIコンテナに登録」+
    // 「T内の[Activity]属性付きメソッドをすべてこのWorkerに登録」を同時に行う。
    // "Scoped"を選んでいる理由: DicomDbContextを使うActivity(RegisterDicomRecordActivity/
    // DeleteRecordActivity)は、DbContextと同じライフタイム(Scoped)で管理する必要があるため
    // (Activities/RegisterDicomRecordActivity.csのコメント参照)。
    // ストレージ操作のみでDbContextを使わないActivity(SaveToStorageActivity/
    // DeleteFromStorageActivity)も、4つのActivity登録の書き方を揃えて学習しやすくする意図で
    // 同じくScopedにしている(Singletonにしても実害はないが、統一した方がコードは読みやすい)。
    //
    // Temporal .NET SDKは「Activity呼び出し1回ごとに新しいDIスコープを作り、
    // 呼び出し終了時に破棄する」ため、ASP.NET Coreの「HTTPリクエスト1回ごとに
    // 新しいDIスコープ」と同じ感覚でDbContextを安全に使い回せる。
    .AddScopedActivities<SaveToStorageActivity>()
    .AddScopedActivities<RegisterDicomRecordActivity>()
    .AddScopedActivities<DeleteFromStorageActivity>()
    .AddScopedActivities<DeleteRecordActivity>()
    // ── Workflow登録 ──
    // Workflowクラスは(Activityとは違って)DIコンテナには登録されない。
    // 決定性制約(Workflows/UploadDicomWorkflow.cs参照)により、Workflow自体はDB接続や
    // HTTPクライアント等の「外部リソースを持つ依存」を注入されるべきではない設計になっているため、
    // AddWorkflow<T>()は単に「このWorkerがどのWorkflow Type名を処理できるか」を登録するだけでよい。
    .AddWorkflow<UploadDicomWorkflow>()
    .AddWorkflow<DeleteDicomWorkflow>();

var host = builder.Build();

host.Services.GetRequiredService<ILogger<Program>>().LogInformation(
    "DicomTool.Worker を起動します。Temporal Server={TemporalAddress}, Namespace={Namespace}, TaskQueue={TaskQueue}",
    temporalAddress, TemporalConstants.Namespace, TemporalConstants.TaskQueue);

// host.RunAsync()はTemporal Workerを含む全IHostedServiceを起動し、Ctrl+C(SIGINT)や
// コンテナのSIGTERM等でGraceful Shutdownがかかるまでブロックし続ける
// （＝このプロセスは「常駐してTask Queueを待ち受け続けるプロセス」であるという設計そのもの）。
await host.RunAsync();
