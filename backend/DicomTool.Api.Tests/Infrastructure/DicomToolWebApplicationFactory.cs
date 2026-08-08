using DicomTool.Shared.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Temporalio.Client;

namespace DicomTool.Api.Tests.Infrastructure;

// ============================================================================
// DicomToolWebApplicationFactory ―― このテストプロジェクト全体の要（かなめ）となるクラス
// ============================================================================
// 【そもそも「結合テスト（Integration Test）」とは何か、「単体テスト」「E2Eテスト」との違い】
// このリポジトリには3種類のテストが（これから）存在することになる。
//
//   1. 単体テスト(Unit Test)
//      1つのクラス/メソッドだけを、依存関係を全部モックに置き換えて検証する。
//      例: AuthServiceのHashPassword/VerifyPasswordだけをテストする、など。
//      このプロジェクトでは今回そこまで踏み込まず、下記2に寄せている。
//
//   2. 結合テスト(Integration Test) ―― このプロジェクト(backend/DicomTool.Api.Tests)がこれ
//      「ASP.NET Coreアプリを実際に起動し、GraphQLエンドポイントにHTTPリクエストを送って、
//      Mutation/Queryが期待通りに動くか」を検証する。DBやTemporal Serverなど「外部の本物」は
//      使わず、それらだけを偽物（InMemory DB、モックのITemporalClient）に差し替える。
//      「アプリの配線（DI登録・GraphQLスキーマ・認証ミドルウェア等）が正しく繋がっているか」
//      まで含めて検証できるのが単体テストとの違い。
//
//   3. E2Eテスト(End-to-End Test) ―― tests/e2e/ 配下のPlaywrightスクリプトがこれ
//      実際に動いているVM上の全サービス（PostgreSQL・Temporal Server・Api・Worker・
//      フロントエンド）を相手に、本物のブラウザを操作してユーザー目線で検証する。
//      本物を使う分、実行が重く・遅く・環境に依存する（VMが起動していないと動かない）代わりに
//      「本当にユーザーが困らないか」を最終確認できる。
//      このApi.Testsとの役割分担: 「ロジックの分岐やエラーメッセージが正しいか」のような
//      細かい確認はここ(結合テスト)で高速かつ安定して何度も回し、
//      「本物のPostgreSQL/Temporal/ブラウザを通しても実際に使えるか」の最終確認だけを
//      E2Eテストに任せる、という考え方（テストピラミッド）。
//
// 【WebApplicationFactory<Program>とは】
// Microsoft.AspNetCore.Mvc.Testingパッケージが提供するクラスで、「本物のASP.NET Coreアプリ
// (backend/DicomTool.Api)を、実際にTCPポートをlistenさせず、プロセス内蔵のTestServerという
// 疑似HTTPサーバーとして丸ごと起動する」仕組み。app.Run()が呼ばれる直前でホストの構築を止め、
// そこから先はHttpClient経由でHTTPリクエスト（この場合はGraphQLへのPOST）を送って
// レスポンスを検証する、という使い方をする。
// ジェネリック引数の`Program`は、テスト対象アプリ(backend/DicomTool.Api)のエントリーポイントを
// 指す型で、Program.cs末尾の`public partial class Program { }`のおかげでテストプロジェクトから
// 参照できるようになっている（トップレベルステートメントは既定でinternalなクラスに
// コンパイルされるため、この1行が無いと外部アセンブリから参照できない）。
//
// 【このクラスでやっていること（全体の流れ）】
// backend/DicomTool.Api/Program.cs は本来、
//   - DBは実PostgreSQL(Npgsql)に接続
//   - ITemporalClientは実Temporal Serverに(遅延)接続
// という「本物」を前提に組み立てられている。これをテストでそのまま使うと
//   - PostgreSQLサーバーが無いと全テストが失敗する
//   - Temporal Serverが無いとアップロードのテストが失敗する
// という「テストのために本物のインフラを常に用意しないといけない」状態になり、
// 手元でサッと`dotnet test`を実行する、という単体/結合テストの利点が失われてしまう。
// そこでこのFactoryは、環境名を"Testing"に切り替えることで Program.cs 側の本物の登録
// （Npgsql / 実Temporal Client）をそもそも行わせず、代わりに ASP.NET Coreの
// `ConfigureWebHost` フック経由で
//   - DicomDbContext … EF Core の InMemory プロバイダとして新規登録
//   - ITemporalClient … NSubstituteで作った偽物(モック)として新規登録
// を行う。これにより、本物のPostgreSQL/Temporal Serverが1つも無い環境でも
// `dotnet test` だけで全テストが完結する（詳細な経緯は下のConfigureWebHost内コメント参照）。
public sealed class DicomToolWebApplicationFactory : WebApplicationFactory<Program>
{
    // ------------------------------------------------------------------
    // InMemory DB の「名前」について
    // ------------------------------------------------------------------
    // EF CoreのInMemoryプロバイダは、実際には「プロセスのメモリ上にある名前付きの
    // 仮想データベース」を複数保持できる仕組みになっている。同じ名前を指定した
    // DbContextインスタンスは全部「同じ仮想DB」を見にいく。
    // もし全テストが同じ固定の名前（例えば"TestDb"）を使うと、あるテストが登録した
    // データが別のテストにも見えてしまい、テスト同士が想定外に影響し合う
    // （「テストは独立していること」という大原則が壊れる）。
    // そのため、このFactoryのインスタンスが作られるたび（＝xUnitのテストクラス1つにつき1回、
    // IClassFixture<DicomToolWebApplicationFactory>を使う場合）に、Guidで一意な名前を
    // 発行している。
    private readonly string _inMemoryDatabaseName = $"DicomToolApiTests-{Guid.NewGuid():N}";

    // ------------------------------------------------------------------
    // ステージング領域/ストレージ領域を、リポジトリ本体から隔離する
    // ------------------------------------------------------------------
    // uploadDicomFiles Mutation は、shared/DicomTool.Shared/Constants/StoragePaths.cs の規約
    // （ContentRootPathから見て "../../infra/data/dicom-incoming"）に従って、実際にディスクへ
    // ファイルを書き込む。何も対策しないと、ASP.NET Coreの既定の仕組みにより
    // ContentRootPathがこのリポジトリ本物の backend/DicomTool.Api フォルダに解決されてしまい、
    // テストを実行するたびに本物の infra/data/dicom-incoming/ にゴミファイルが増えてしまう。
    // OSの一時フォルダ配下に、テスト専用の使い捨てディレクトリ構造
    // （"<一時フォルダ>/<Guid>/backend/DicomTool.Api" という、本物と同じ「2階層下」の
    // 構造）を用意し、ASP.NET Coreの ContentRootPath をそちらに向けることで、
    // テストが書き込むファイルを本物のリポジトリから完全に隔離する。
    private readonly string _tempRoot =
        Path.Combine(Path.GetTempPath(), "dicom-tool-api-tests", Guid.NewGuid().ToString("N"));

    // ------------------------------------------------------------------
    // ITemporalClient のモック（偽物）
    // ------------------------------------------------------------------
    // 【なぜモックが必要か、モックとは何か】
    // 「モック」とは、本物のクラス/インターフェースの代わりにテストで使う「偽物」のこと。
    // 本物のITemporalClientは、実際にTemporal Serverへネットワーク越しに接続し、
    // ワークフローの起動をリクエストする（＝副作用があり、外部サーバーが必要）。
    // uploadDicomFiles Mutationが「正しくワークフローを起動しようとしたか」だけを
    // 確認したいのであって、「本物のTemporalが実際にワークフローを実行できるか」は
    // このテストの関心事ではない（それはservices/DicomTool.Worker側の責務であり、
    // 本当に実行されるかどうかまで確認したければ tests/e2e/ のE2Eテストの役割になる）。
    //
    // NSubstitute（NuGetパッケージ）の `Substitute.For<ITemporalClient>()` は、
    // 「ITemporalClientインターフェースが持つ全メソッドについて、呼ばれても何もせず、
    // 戻り値の型に応じた無難なデフォルト値（Task<T>なら完了済みTaskなど）を返すだけの
    // 空っぽの偽物」を自動生成してくれる。
    // さらに便利な点として、このsubstituteは「何回、どんな引数で呼ばれたか」を記録して
    // いるため、テストコード側から
    //   await TemporalClientMock.Received(1).StartWorkflowAsync(引数の条件, ...)
    // のように「期待した呼び出しが本当に起きたか」を後から検証できる
    // （これが「実際にTemporalへ接続せずに、正しい引数で呼ばれたことを検証できる」という
    // モックの効能そのもの）。
    //
    // publicにしているのは、各テストクラスが「このモックが正しく呼ばれたか」を
    // 直接検証できるようにするため。
    public ITemporalClient TemporalClientMock { get; } = Substitute.For<ITemporalClient>();

    // ------------------------------------------------------------------
    // コンストラクタで環境変数を使ってJWT関連の設定を上書きする
    // ------------------------------------------------------------------
    // 【なぜ ConfigureWebHost / ConfigureAppConfiguration の中で上書きしなかったのか】
    // 最初は ConfigureAppConfiguration(...) の中で appsettings.json の値を上書きしようとしたが
    // うまくいかなかった。理由は、WebApplicationFactoryが提供する ConfigureWebHost フックは、
    // 「Program.cs が var app = builder.Build(); を呼び出す“まさにその瞬間”」に横から
    // 割り込んで内容を書き換える仕組みになっているため（詳細は下のConfigureWebHost内コメント
    // 参照）、Program.cs の中で **Build()より前** に実行される通常のコード
    // （例えば `var jwtOptions = jwtSection.Get<JwtOptions>() ?? ...;` のように、
    // 起動処理の途中で設定値を読み出して変数に入れてしまっている箇所）に対しては、
    // このフックはもう間に合わない（読み出しの方が先に終わってしまっている）。
    // 実際、Jwt:SigningKeyが空文字のまま読み出されてしまい、
    // 「署名鍵の長さが0」という例外でテストが失敗する、という事象が実際に起きた。
    //
    // ASP.NET Coreの既定の設定システムは、`WebApplication.CreateBuilder(args)` が
    // 呼ばれた“その場”で環境変数を読み込む（Configurationプロバイダーの1つとして
    // 環境変数が組み込まれているため）。つまり「Program.csのどのタイミングのコードからも
    // 一貫して見える」ように設定値を上書きしたい場合は、Program.cs の実行が始まるより前に
    // 環境変数として設定しておく必要がある。このFactoryのインスタンスはテストメソッドの
    // 実行より前（xUnitがフィクスチャを準備する段階）に必ず生成されるため、
    // コンストラクタで環境変数をセットしておけば、その後いつ実際のアプリ起動処理
    // (Program.cs の実行)が走っても、常に上書き後の値が見える。
    public DicomToolWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("Jwt__Issuer", "DicomTool.Api.Tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "DicomTool.Api.Tests");
        Environment.SetEnvironmentVariable(
            "Jwt__SigningKey",
            "test-only-signing-key-for-dicomtool-api-tests-min-32bytes");
        Environment.SetEnvironmentVariable("Jwt__ExpiryMinutes", "60");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_tempRoot);
        var fakeContentRoot = Path.Combine(_tempRoot, "backend", "DicomTool.Api");
        Directory.CreateDirectory(fakeContentRoot);
        builder.UseContentRoot(fakeContentRoot);

        // ------------------------------------------------------------------
        // 環境名を "Testing" にする
        // ------------------------------------------------------------------
        // ASP.NET Coreは環境名（Development/Staging/Production等）ごとに
        // appsettings.{環境名}.json を読み込む仕組みを持っており、"Testing" という名前は
        // このリポジトリで特別に予約している（Program.cs 側の if 分岐を参照）。
        // 環境名を"Testing"にすると、Program.cs内の
        //   if (!builder.Environment.IsEnvironment("Testing")) { ... }
        // という条件により、「本物のPostgreSQL(Npgsql)へのAddDbContext」と
        // 「本物のTemporal ServerへのITemporalClient登録」がそもそも実行されなくなる。
        //
        // 【なぜ「後から登録し直す(ConfigureServicesでRemoveAllしてから差し替える)」方式を
        // 採らなかったのか】
        // 最初はその方式（Microsoft公式の結合テストドキュメントでもよく紹介されている定石）を
        // 試したが、EF Coreは「1つのDbContext型に対して、Npgsql用の内部サービスと
        // InMemory用の内部サービスの“両方"がDIコンテナに登録された状態」を検知すると、
        // "Only a single database provider can be registered" という例外を投げて起動に
        // 失敗することが分かった（DbContextOptions<T>という表向きの設定を消しても、
        // UseNpgsql/UseInMemoryDatabaseそれぞれが裏側でDIコンテナに追加する
        // プロバイダ固有のサービス群まではRemoveAllで綺麗に取り除けないため）。
        // そこで「そもそも本物の登録をさせない」という、より単純で事故りにくい方式
        // （Program.cs側でのif分岐 + このFactory側での環境名切り替え）を採用している。
        builder.UseEnvironment("Testing");

        // Jwt:SigningKey等の設定値は、コンストラクタで既に環境変数として上書き済み
        // （理由はコンストラクタ側のコメント参照）。ここで改めて設定を上書きする必要はない。

        builder.ConfigureServices(services =>
        {
            // ================================================================
            // ① DicomDbContext を InMemory プロバイダとして登録する
            // ================================================================
            // Program.cs側は"Testing"環境ではAddDbContextを一切呼んでいないため、
            // ここで初めてDicomDbContextがDIコンテナに登録されることになる
            // （「差し替え」ではなく「テスト用に新規追加」という位置づけ）。
            // 実PostgreSQLサーバーを1台も使わずに「DbContext経由の読み書きロジックが
            // 正しく書けているか」を検証できるのが、このInMemoryプロバイダの価値。
            services.AddDbContext<DicomDbContext>(options =>
                options.UseInMemoryDatabase(_inMemoryDatabaseName));

            // ================================================================
            // ② ITemporalClient をモック(偽物)として登録する
            // ================================================================
            // 同様にProgram.cs側は"Testing"環境ではITemporalClientを登録していないため、
            // ここでモック(TemporalClientMock)をSingletonとして新規登録する。
            // これにより、Mutation.cs の UploadDicomFilesAsync/DeleteXxxAsync が
            // [Service] ITemporalClient として受け取るインスタンスは、実際には
            // このモックになる。
            services.AddSingleton(TemporalClientMock);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // テスト用に作った一時ディレクトリ（アップロードしたダミーファイル置き場）を掃除する。
        // 掃除自体に失敗しても後続のテスト結果に影響させたくないので例外は握りつぶす。
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // 一時フォルダの削除失敗はテスト結果として扱わない（OS側の一時的なロック等が原因のことがある）。
        }
    }
}
