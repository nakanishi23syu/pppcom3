using System.Diagnostics;
using DicomTool.Shared.Constants;
using DicomTool.TrayApp;
using DicomTool.TrayApp.Models;
// WebApplication / WebApplicationBuilder はこの名前空間にある。
// Microsoft.NET.Sdk.Web を使うプロジェクトでは暗黙的globalusingとして
// 自動的に使えるようになっているが、本プロジェクトはWinForms用の
// Microsoft.NET.Sdk をそのまま使っているため、明示的にusingする必要がある。
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting; // WebHostBuilderExtensions.UseUrls 拡張メソッド用
using Microsoft.AspNetCore.Http; // Results / StatusCodes (Minimal APIのレスポンスヘルパー)用
using Microsoft.Extensions.DependencyInjection; // AddCors / AddEndpointsApiExplorer 等のDI登録拡張メソッド用
using Microsoft.OpenApi;

// ============================================================================
// DicomTool.TrayApp ― Worklist(ブラウザ) と Timeline(ブラウザ) を橋渡しする
//                     OSタスクトレイ常駐アプリ
// ============================================================================
//
// 【このサービスが存在する理由】
// 実務のPACS的システムでは、Worklist画面(frontend/worklist, ポート3100)で
// 検査を右クリックして「タイムラインを別画面で開く」という操作をしたくなる。
// もしWorklistがただのWebアプリだったら、これは簡単そうに見えて実は簡単ではない。
// ブラウザ上のJavaScriptは強いサンドボックスの中で動作しており、
//   ・任意の外部プロセスを起動する
//   ・window.open() 以外の方法でOSのデフォルトブラウザを直接操作する
// といったことはセキュリティ上できないようになっている(意図的な制約)。
// window.open() で新しいタブを開くこと自体はできるが、それだけでは
// 「別オリジンのアプリ間で情報を受け渡しつつ、OSレベルの何らかの処理を挟む」
// といった実務でよくあるデスクトップ連携(Discord/LINEの「ブラウザからアプリを呼ぶ」
// リンクなど)は再現できない。
//
// そこで本サービスのように「OSに常駐し、localhost限定でHTTP APIを開けておく
// ネイティブアプリ」を仲介役として置く。Worklistはこの常駐アプリのHTTP APIに
// fetch()するだけで済み、常駐アプリ側がProcess.Start(UseShellExecute: true)という
// OSネイティブAPIを使ってTimeline(frontend/timeline, ポート5230)をデフォルト
// ブラウザで開く。「ブラウザ ⇔ OSネイティブ操作」の境界をHTTPで越える、という
// 構図がこのサービスの学習ポイント。
//
// 【WinFormsのメッセージループとASP.NET Core(Kestrel)の同居について】
// このプロセスは「タスクトレイに常駐するWinFormsアプリ」であると同時に
// 「localhost:5299でHTTPを待ち受けるWebサーバー」でもある、という二重の顔を持つ。
// WinFormsはメッセージループ(Application.Run)がスレッドを占有することで
// マウス操作やメニュー選択などのUIイベントを処理し続ける仕組みであり、
// ASP.NET CoreのWebApplication(Kestrel)も内部で非同期にリクエストを待ち受け続ける。
// この2つを同一プロセス内で共存させる一般的なパターンは、
//   1. WebApplication を app.RunAsync() で「待たずに(await せず)」起動する
//      → RunAsync()はTaskを返してすぐ制御を戻すため、Kestrelはバックグラウンドの
//        スレッドプール上で動き続ける。
//   2. その後にメインスレッドで Application.Run(...) を呼び、WinFormsの
//      メッセージループを回す。
// という順序で「投げっぱなしのバックグラウンドタスク(Kestrel) + フォアグラウンドの
// メッセージループ(WinForms)」を両立させる形。どちらも「呼んだら制御を返さず
// 待ち続ける」性質を持つため、片方を非同期化(RunAsync)しない限り共存できない。
//
// 【終了時の後始末】
// ユーザーがトレイメニューから「終了」を選んだときは、Kestrelホストを
// WebApplication.StopAsync()で行儀よく止めてから Application.Exit() で
// WinForms側のメッセージループを終了させる(実装は TrayApplicationContext 側)。
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // WinForms側の初期化。
        // 高DPI対応やビジュアルスタイル(Windowsのテーマに沿った見た目)を
        // 明示的に有効化しておく。
        ApplicationConfiguration.Initialize();

        var webApp = BuildWebApplication();

        // ここがポイント: awaitせずに投げっぱなしにする。
        // await してしまうとKestrelがリクエストを待ち受け続ける限り
        // このMainメソッドの実行がブロックされてしまい、下のApplication.Run()
        // (WinFormsのメッセージループ)に到達できなくなる。
        // 「HTTPサーバーはバックグラウンドで動かしつつ、UIスレッドは
        // WinFormsのメッセージループに使う」という役割分担のために
        // あえて非同期に投げっぱなしにしている。
        _ = webApp.RunAsync();

        // WinFormsのメッセージループ本体。
        // TrayApplicationContextが「終了」メニューを押されるまでこのプロセスを
        // 生かし続ける(詳細は TrayApplicationContext.cs 参照)。
        Application.Run(new TrayApplicationContext(webApp));
    }

    /// <summary>
    /// ローカルHTTP API(Minimal API)を組み立てる。
    /// ポートは docs/CONTRACT.md 1章の "唯一の正" である
    /// <see cref="ServicePorts.TrayAppHttp"/>(5299) をそのまま使う。
    /// </summary>
    private static WebApplication BuildWebApplication()
    {
        var builder = WebApplication.CreateBuilder();

        // localhost限定で5299番ポートを待ち受ける。
        // 0.0.0.0ではなくlocalhostに限定するのは、この常駐アプリが
        // 「同じPC上で動いているブラウザだけを相手にする」ローカル専用の
        // 橋渡し役であり、LAN上の他PCから叩かれる必要がないため。
        builder.WebHost.UseUrls($"http://localhost:{ServicePorts.TrayAppHttp}");

        // WorklistやTimelineが「このPCとは別のマシン(例: VM)」でホストされている場合、
        // ブラウザ上のWorklistのオリジンも、TrayAppが起動すべきTimelineの宛先も
        // localhostではなくそのマシンのIP/ホスト名になる。設定(appsettings.json)や
        // 環境変数 RemoteHost で上書きできるようにし、既定値は今まで通り localhost にする
        // (docker-compose等で全サービスが同一PC上にある場合の後方互換のため)。
        var remoteHost = builder.Configuration["RemoteHost"] ?? "localhost";

        // ------------------------------------------------------------------
        // CORS(Cross-Origin Resource Sharing)の設定について
        // ------------------------------------------------------------------
        // Worklist(http://localhost:3100 または http://{remoteHost}:3100)は、
        // この常駐アプリ(http://localhost:5299)にfetch()でリクエストを送る。
        // ポート番号やホスト名が異なるだけでもブラウザから見れば「別オリジン」であり、
        // ブラウザは既定では別オリジンへのXHR/fetchのレスポンス読み取りをブロックする
        // (同一オリジンポリシー)。サーバー側が明示的に
        // 「このオリジンからのリクエストは許可する」というCORSヘッダーを
        // 返さない限り、Worklist側のJavaScriptはレスポンスを受け取れない。
        // そのため、Worklistのオリジン(localhostとremoteHostの両方)を名指しで許可する
        // CORSポリシーをここで登録しておく。
        const string worklistCorsPolicy = "AllowWorklistOrigin";
        builder.Services.AddCors(options =>
        {
            options.AddPolicy(worklistCorsPolicy, policy =>
            {
                policy
                    .WithOrigins(
                        $"http://localhost:{ServicePorts.WorklistNuxt}",
                        $"http://{remoteHost}:{ServicePorts.WorklistNuxt}")
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });

        // Swagger(OpenAPI) UIを /swagger に生やすための登録。
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "DicomTool.TrayApp API",
                Version = "v1",
                Description =
                    "OSタスクトレイ常駐アプリのローカルHTTP API。" +
                    "Worklistからの起動命令を受け取り、Timelineをブラウザで開く橋渡し役。",
            });
        });

        var app = builder.Build();

        // 開発中・動作確認のしやすさを優先し、環境を問わず常にSwagger UIを出す。
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "DicomTool.TrayApp API v1");
        });

        app.UseCors(worklistCorsPolicy);

        MapEndpoints(app, remoteHost);

        return app;
    }

    private static void MapEndpoints(WebApplication app, string remoteHost)
    {
        // 生存確認用のヘルスチェック。トレイメニューの「ステータス確認」からも叩く。
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            port = ServicePorts.TrayAppHttp,
        }))
        .WithName("GetHealth")
        .WithSummary("常駐アプリの生存確認");

        // Worklistからの「このpatientIdのTimelineを開いてほしい」という命令を受け取る本体。
        // ここが「ブラウザのfetch」と「OSネイティブのプロセス起動」の境界。
        app.MapPost("/commands/open-timeline", (OpenTimelineRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.PatientId))
            {
                return Results.BadRequest(new { error = "patientId is required." });
            }

            // Timelineの URL は docs/CONTRACT.md の想定パス(/timeline/{patientId})に合わせる。
            // Uri.EscapeDataStringでpatientIdをURLエンコードし、
            // 意図しない文字(スペースや'/'など)でURLが壊れないようにする。
            var timelineUrl =
                $"http://{remoteHost}:{ServicePorts.TimelineBlazor}/timeline/{Uri.EscapeDataString(request.PatientId)}";

            try
            {
                // UseShellExecute = true がここでの肝。
                // これを指定すると.NETは「exeを直接起動する」のではなく、
                // Windowsのシェル(エクスプローラー)に「このURLをどう開くべきか」を
                // 委ねる。URLに対してはOSに登録された既定のブラウザが自動的に
                // 選ばれて起動する。これはWindowsで cmd.exe から `start <URL>` を
                // 打つのと同じ仕組みであり、.NETからはUseShellExecuteでこれを再現する。
                Process.Start(new ProcessStartInfo(timelineUrl)
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Timelineを開けませんでした。",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Ok(new
            {
                opened = true,
                url = timelineUrl,
            });
        })
        .WithName("OpenTimeline")
        .WithSummary("Worklistからの起動命令を受け取り、指定患者のTimelineをブラウザで開く");
    }
}
