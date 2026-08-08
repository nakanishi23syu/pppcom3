// ======================================================
// Program.cs — Blazor WebAssembly アプリのエントリーポイント
// ======================================================
// dicom-tool-2/blazor/DicomTool.Blazor の Program.cs の移植。
// 元アプリはAuthState/StudyState/ToastState等、検査一覧・ログインを含む一枚岩アプリ全体の
// DI登録を持っていたが、Timelineは「patientTimelineクエリを叩いて表示するだけ」の
// 独立サービスなので、DIに登録するのも GraphQLClient と StudyService の2つだけに絞っている。

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using DicomTool.Timeline;
using DicomTool.Timeline.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// 【AddScoped とは】
// Blazor WebAssemblyは「ブラウザのタブ1つ ＝ 1プロセス」で動くSPAなので、
// ASP.NET Core（サーバー）で言う「1リクエストごとに新しいインスタンス」というScopedの
// 本来の意味は薄れ、実質「アプリ起動中ずっと使い回されるシングルトン」として働く。
// それでもScopedを使うのが公式の慣習になっている（将来Blazor Serverへ移行した場合に
// 「1接続＝1状態」という意味が保たれるよう安全側に倒すため）。
//
// HttpClient: backend/DicomTool.Api（別オリジン: http://localhost:5030）へアクセスするための
// 素のHttpClient。BaseAddressはBlazorアプリ自身のオリジンのままでよい
// （GraphQLClient側でフルURLを指定して呼ぶため）。
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<GraphQLClient>();
builder.Services.AddScoped<StudyService>();

await builder.Build().RunAsync();
