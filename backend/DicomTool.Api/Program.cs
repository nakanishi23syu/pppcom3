using System.Text;
using DicomTool.Api.Constants;
using DicomTool.Api.Configuration;
using DicomTool.Api.Data;
using DicomTool.Api.GraphQL;
using DicomTool.Api.Services;
using DicomTool.Shared.Constants;
using DicomTool.Shared.Data;
using HotChocolate.Types;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Temporalio.Client;

// ======================================================
// このファイル(Program.cs)の全体像 ― ASP.NET Coreアプリの「起動スクリプト」
// ======================================================
// ASP.NET Coreのアプリは、大きく2段階の手続きで組み立てる。
//
//   ① builder.Services.AddXxx(...)   … "DIコンテナ" に「この型が欲しくなったら
//        こうやって作ってね」というレシピを登録するフェーズ（builder.Build() まで）。
//        ここで登録した型は、後からコンストラクタの引数やメソッドの引数に
//        "その型をただ書くだけ" で自動的にインスタンスが渡ってくるようになる。
//        これが「DI（Dependency Injection / 依存性の注入）」で、ASP.NET Core全体の土台となる仕組み。
//
//   ② app.UseXxx(...) / app.MapXxx(...)  … 実際にHTTPリクエストが来たときに
//        「どんな順番で処理するか」というパイプライン（流れ作業のライン）を組み立てるフェーズ
//        （builder.Build() 以降）。
//        UseXxx は「全リクエストに共通で通す処理」（認証チェックなど）を追加し、
//        MapXxx は「このURLパスに来たらこの処理を実行する」というルーティングを登録する。
//
// ①と②は完全に別物で、①でDIコンテナに登録していない型は②のどこでも自動注入できない、
// という関係になっている。
var builder = WebApplication.CreateBuilder(args);

// ======================================================
// リクエストサイズ上限の緩和（DICOMアップロード対応）
// ======================================================
// KestrelはデフォルトでHTTPリクエストボディを30MBまでしか受け付けない。
// DICOM検査は1検査でも数十〜数百MBになることが珍しくなく
// （CTの1シリーズだけで数百枚のスライスがあるため）、複数ファイルをまとめて
// アップロードする uploadDicomFiles Mutation（GraphQL/Mutation.cs参照）では
// 既定値だと簡単に超えてしまう。学習用ツールとしての現実的な上限として500MBまで許容する。
// この設定はKestrel層（HTTPサーバーそのもの）のガード値であり、GraphQLエンドポイント
// （/graphql）宛のリクエストにも当然適用される。ただしASP.NET Coreのマルチパートフォーム
// パーサーにはこれとは別に「フォーム全体のバイト数上限」を持つFormOptionsという設定があり、
// 既定値（既定128MB）のままだとそちらで先に弾かれてしまうため、下のFormOptions設定と
// 2つセットで緩和する必要がある。
// なお、非同期化（UploadDicomWorkflow経由）後もこのMutationは「ファイルをステージング領域に
// 書き込む」処理自体は変わらず行うため、このサイズ上限緩和は引き続き必要。
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 500 * 1024 * 1024;
});

// ======================================================
// マルチパートフォームの上限緩和（GraphQLファイルアップロード対応）
// ======================================================
// HotChocolateのファイルアップロード（uploadDicomFiles Mutation）は、GraphQL multipart
// request spec（https://github.com/jaydenseric/graphql-multipart-request-spec）に従って
// `multipart/form-data` で送られてくるリクエストを、ASP.NET Core標準のフォームパーサー
// （HttpRequest.ReadFormAsync）で解釈している。このパーサーはKestrelのMaxRequestBodySizeとは
// 別に、FormOptions.MultipartBodyLengthLimit（既定128MB）という独自の上限を持っており、
// これを緩和しないと複数DICOMファイルをまとめて送った際に上のKestrel設定より先に
// このパーサー側の上限に引っかかってしまう。値はKestrel側と揃えて500MBにしておく。
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 500 * 1024 * 1024;
});

// ======================================================
// DIコンテナへの登録
// ======================================================
// AddXxx系メソッドの名前についている "Scoped" は、そのサービスが
// 「いつインスタンスを使い回すか」という寿命（ライフタイム）を表す。3種類ある:
//   - AddSingleton … アプリ起動から終了まで、ずっと同じ1個のインスタンスを使い回す。
//   - AddScoped    … リクエスト1件（GraphQLなら1クエリ/1ミューテーション）ごとに新しいインスタンス。
//                     同じリクエスト内で複数回注入されても同じインスタンスが渡る。
//   - AddTransient … 注入されるたびに毎回新しいインスタンス。
// DbContext（DB接続を持つ）はリクエストをまたいで使い回すと事故りやすいため Scoped が定石。
//
// ======================================================
// DB接続先をPostgreSQLへ（dicom-tool(旧)からの最大の環境変更点）
// ======================================================
// dicom-tool(旧)はSQLite（ローカルファイル1つ）だったが、実務環境再現のため
// PostgreSQLへ移行した（docs/CONTRACT.md 7章）。DicomDbContext自体（shared/DicomTool.Shared側）は
// プロバイダ非依存に書かれているため、ここでUseNpgsqlに差し替えるだけで済む。
// マイグレーションファイルもshared側に既に用意されている（同じテーブル定義をApi/Workerの
// 2プロセスがそれぞれ持つとズレる事故が起きるため、shared 1箇所に集約している）。
//
// 【なぜ "Testing" 環境ではこの登録自体をスキップするのか】
// backend/DicomTool.Api.Tests（結合テスト、WebApplicationFactory<Program>を使用）では、
// 実PostgreSQLの代わりにEF Coreの InMemory プロバイダへ差し替えたい。
// 一見、「後からInMemory版のAddDbContextを呼んで上書きすればいい」ように思えるが、
// 実際にそれをやろうとすると、EF Coreは「同じDbContext型に対して、Npgsql用の内部サービスと
// InMemory用の内部サービスの両方がDIコンテナに登録されている」状態を検知して
// 例外を投げる（"Only a single database provider can be registered" という趣旨のエラー。
// DbContextOptions<DicomDbContext>という「表向きの設定」だけ差し替えても、
// UseNpgsql/UseInMemoryDatabaseそれぞれが裏側でDIコンテナに追加するプロバイダ固有の
// サービス群までは綺麗に取り除けないため）。
// そのため「そもそも本番用のNpgsql登録をテスト実行時には行わない」という、
// 登録時点でのif分岐によって二重登録そのものを避けている。
// IWebHostEnvironment.EnvironmentName を "Testing" にするのは
// backend/DicomTool.Api.Tests/Infrastructure/DicomToolWebApplicationFactory.cs 側の役割で、
// そちらが builder.UseEnvironment("Testing") を呼んだ上で、代わりにInMemory版の
// AddDbContext呼び出しを行う（詳細は同ファイルのコメント参照）。
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<DicomDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("Dicom")));
}

builder.Services.AddScoped<DicomTagRevertService>();

// ======================================================
// JWT認証の設定
// ======================================================
// appsettings.json（実際の値はappsettings.Development.json）の "Jwt" セクションから
// Issuer/Audience/署名鍵を読み込む。AddJwtBearer が「Authorizationヘッダーの
// Bearerトークンを検証する」処理をASP.NET Coreパイプラインに組み込む。
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.Configure<JwtOptions>(jwtSection);
builder.Services.AddScoped<AuthService>();

var jwtOptions = jwtSection.Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            // トークンの有効期限切れを判定する際、サーバー間の時計のズレを許容する猶予（既定5分）を0にする。
            ClockSkew = TimeSpan.Zero,
        };
        // ======================================================
        // JWTをAuthorizationヘッダーではなくhttpOnly Cookieから読み取る
        // ======================================================
        // フロントエンドはもうJWTの値そのものを知らない（localStorageに保存しない）ため、
        // Authorizationヘッダーを自分で組み立てて送ることができない。代わりにブラウザが
        // Cookieを自動送信してくるので、ここでCookieの中身をトークンとして扱うよう教える。
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(AppConstants.AuthCookieName, out var token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

// Mutation側でHttpContext（Cookieの設定・削除、ログイン中ユーザーの判定）にアクセスするために必要。
builder.Services.AddHttpContextAccessor();

// ======================================================
// CORS設定（フロントエンドからのクロスオリジン通信を許可する）
// ======================================================
// フロントエンド（Worklist:3100 / Viewer:3200 / Timeline:5230）とバックエンド
// （このASP.NET Coreアプリ、既定で http://localhost:5030）はポートが異なるため、
// ブラウザの同一オリジンポリシーにより、CORSを許可しないとfetchが失敗する（docs/CONTRACT.md 8章）。
// 許可オリジンは appsettings.json の Cors:AllowedOrigins（環境変数 Cors__AllowedOrigins でも上書き可）で管理する。
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(AppConstants.FrontendCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            // 認証をhttpOnly Cookieに変更したため、ブラウザにCookieを一緒に送らせる／
            // 受け取ったSet-CookieをJSに見せる（CORSの credentials モード）には
            // 明示的な許可が必要。ワイルドカードオリジン（*）とは併用できない仕様のため、
            // WithOrigins()で許可オリジンを個別指定していることが前提になる。
            .AllowCredentials());
});

// ======================================================
// GraphQLサーバーの設定
// ======================================================
// AddGraphQLServer() が GraphQL エンドポイントの土台を作り、
// AddQueryType<T> / AddMutationType<T> で「どのC#クラスをQuery/Mutationのルートにするか」を教える。
// .AddAuthorization() で HotChocolate に [Authorize] 属性を解釈させる（ASP.NET Core標準の
// AddAuthorization とは別物で、GraphQL側のミドルウェアを有効化するために必要）。
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddAuthorization()
    // ======================================================
    // Upload スカラーの登録（GraphQLファイルアップロード対応）
    // ======================================================
    // GraphQLの標準スカラー（String, Int, Boolean...）には「ファイル」を表す型が無いため、
    // HotChocolateは `Upload` という独自スカラーを用意している。ただしこれは組み込みでは
    // 有効になっておらず、AddType<UploadType>() で明示的に登録しないと、スキーマ側で
    // `[Upload!]!` のような型を書いてもエラーになる。
    .AddType<UploadType>();

// ======================================================
// Temporal Clientへの接続（DIコンテナへSingleton登録）
// ======================================================
// docs/CONTRACT.md 4章の通り、Api/DicomScpは「ワークフローを起動する側」であって
// 「ワークフローの中身を知っている側」ではない。ITemporalClient（起動用の型なしクライアントAPIを
// 持つインターフェース）をSingletonとしてDIコンテナに登録しておくと、Mutation.csの各メソッドが
// [Service] ITemporalClientでそのまま注入を受けられる。
//
// 【ConnectAsyncではなくCreateLazyを使う理由】
// Temporal .NET SDKには接続方法が2つある。
//   - TemporalClient.ConnectAsync(...) … その場で実際にTemporal Serverへ接続を試み、
//     失敗すれば例外を投げる（＝Api起動時点でTemporal Serverが必ず立っている必要がある）。
//   - TemporalClient.CreateLazy(...)   … 接続確認をその場では行わず、実際にワークフローを
//     起動する（StartWorkflowAsync等）が最初に呼ばれたタイミングまで接続を遅延する。
// 以前はConnectAsyncを使っていたが、これだと「Api起動時に必ずTemporal Serverが応答する
// 状態でなければアプリ全体が起動できない」という制約が生じる（Temporal Serverの起動が
// Apiより遅れる運用に弱い）。CreateLazyであれば起動時のネットワーク疎通確認を行わないため、
// Temporal Serverが後から起動する運用でもApiは先に起動できる。
// 実際に接続できない場合の挙動は「次にそのクライアントが使われた時に再試行される」
// （Temporal .NET SDKのCreateLazyのドキュメントコメントより）ため、本番運用上の安全性も変わらない。
//
// 【なぜ "Testing" 環境ではこの登録自体をスキップするのか】
// 上のDicomDbContextと全く同じ理由。backend/DicomTool.Api.Tests では実Temporal Serverに
// 接続する本物のITemporalClientをそもそもDIコンテナに登録させず、代わりに
// NSubstituteで作った偽物(モック)だけを登録する（詳細はDicomToolWebApplicationFactory.cs参照）。
// 二重登録を避けるため、ここでも登録自体をif分岐でスキップしている。
if (!builder.Environment.IsEnvironment("Testing"))
{
    var temporalAddress = builder.Configuration["Temporal:Address"] ?? "localhost:7233";
    var temporalClient = TemporalClient.CreateLazy(new(temporalAddress)
    {
        Namespace = TemporalConstants.Namespace,
    });
    builder.Services.AddSingleton<ITemporalClient>(temporalClient);
}

var app = builder.Build();

app.UseCors(AppConstants.FrontendCorsPolicy);

// ======================================================
// DICOM画像配信（静的ファイル配信）
// ======================================================
// マウント先は appsettings.json の設定値ではなく、shared/DicomTool.Shared/Constants/StoragePaths.cs
// を使って組み立てる（保存規約を共有定数に一本化する意図。docs/CONTRACT.md 6章）。
// services/DicomTool.Worker がSaveToStorageActivityで確定保存するパスと、
// このApiが配信するパスが「同じ計算式」から導かれることを保証するため。
// フロントのcanvas描画はここからファイル本体を取得し、GraphQL側はメタデータ
// （UserSop.FilePath等）だけを扱う。
var dicomStorageFullPath = StoragePaths.ResolveStoragePath(builder.Environment.ContentRootPath);
Directory.CreateDirectory(dicomStorageFullPath);
// ".dcm" はASP.NET Core既定のFileExtensionContentTypeProviderに登録が無く、
// 既定設定のままだと「Content-Typeを判定できないファイルは404にする」挙動によりDICOMファイルが
// 一切配信されない（ServeUnknownFileTypesの既定値はfalse）。".dcm"のMIMEタイプを明示的に追加して解決する。
var dicomContentTypeProvider = new FileExtensionContentTypeProvider();
dicomContentTypeProvider.Mappings[".dcm"] = "application/dicom";
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(dicomStorageFullPath),
    RequestPath = "/dicom-files",
    ContentTypeProvider = dicomContentTypeProvider,
});

// UseAuthentication（「誰か」を判定）→ UseAuthorization（「その人に権限があるか」を判定）の順で登録する。
// この順序を逆にすると認可判定の時点でユーザー情報が確定しておらず正しく動作しない。
app.UseAuthentication();
app.UseAuthorization();

// ======================================================
// 起動時にDBマイグレーションを適用し、データが空ならサンプルデータを投入する
// ======================================================
// DbContextはScopedなので、アプリ起動時（DIスコープの外）で使うには
// 明示的にスコープを作って取り出す必要がある。
// docs/CONTRACT.md 7章の通り、マイグレーション適用は「Backend APIだけ」が行う
// （services/DicomTool.Worker は「マイグレーション済みである前提」でDbContextを使い、
// 複数プロセスが同時にマイグレーションを試みる競合を避ける）。
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DicomDbContext>();
    var authService = scope.ServiceProvider.GetRequiredService<AuthService>();

    // 【なぜMigrate()を無条件に呼ばないのか】
    // Migrate()は「リレーショナルDB（テーブルやマイグレーション履歴という概念を持つDB）」
    // 向けの操作で、本番/開発で使うPostgreSQL(Npgsql)ではこれで問題ない。
    // 一方 backend/DicomTool.Api.Tests の結合テストでは、実PostgreSQLを用意しない代わりに
    // EF Coreの InMemory プロバイダ（DBを使わずメモリ上だけで完結させるテスト用プロバイダ、
    // 詳細はテストプロジェクト側のコメント参照）に差し替えている。InMemoryプロバイダは
    // マイグレーションという概念自体を持たないため、Migrate()を呼ぶと
    // 「InMemoryではサポートされていません」という例外で落ちてしまう。
    // IsRelational()で「今使っているプロバイダがマイグレーションに対応した種類か」を判定し、
    // 対応していない場合は EnsureCreated()（今のモデル定義から、その場で最低限のテーブル相当の
    // 構造を作るだけの簡易メソッド）で代用する。
    if (db.Database.IsRelational())
    {
        db.Database.Migrate();
    }
    else
    {
        db.Database.EnsureCreated();
    }

    DbSeeder.SeedIfEmpty(db, authService);
}

// /graphql に GraphQL エンドポイントを公開する。
// 開発環境でブラウザから http://localhost:5030/graphql を開くと、
// 「Nitro」というGraphQL用のIDE（クエリを試し打ちできる画面）が表示される。
// DICOMファイルのアップロードもこのMutation（uploadDicomFiles、GraphQL/Mutation.cs参照）に
// 統一しているため、このMapGraphQL()の1本だけでアップロードも含めた全操作を受け付ける。
app.MapGraphQL();

app.Run();

// ======================================================
// public partial class Program
// ======================================================
// トップレベルステートメント（このファイルのように usingの後いきなり文を書けるC#の書き方）で
// 書かれたエントリーポイントは、コンパイラが裏側で自動生成する非公開(internal)の
// `Program` というクラスの `Main` メソッドに変換される。
// backend/DicomTool.Api.Tests の結合テストでは、このApiプロジェクトを丸ごと
// `WebApplicationFactory<Program>`（ASP.NET Coreアプリをテスト用にメモリ上で起動する仕組み）
// に渡す必要があるが、テストプロジェクトは別アセンブリのため、コンパイラ生成のままの
// internalな`Program`を参照できない。この1行を明示的に書き足すことで、
// 「同じ名前のPartial（分割）クラス」としてpublicに昇格させ、外部アセンブリから参照可能にしている
// （中身は空でよい。コンパイラが生成する方のPartialクラスと合体するだけ）。
public partial class Program { }
