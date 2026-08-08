using System.Diagnostics;
using DicomTool.Shared.Constants;
using Microsoft.AspNetCore.Builder;

namespace DicomTool.TrayApp;

/// <summary>
/// タスクトレイに常駐するアイコンと、その右クリックメニュー（コンテキストメニュー）を
/// 管理するクラス。
///
/// 【なぜ ApplicationContext を使うのか】
/// 通常のWinFormsアプリは <c>Application.Run(new MainForm())</c> のように
/// 「メインウィンドウが閉じられたらアプリ全体も終了する」という作りが一般的だが、
/// このアプリには常時表示するウィンドウが存在しない（普段はタスクトレイに
/// アイコンが1つ表示されているだけ）。
/// <see cref="ApplicationContext"/> を継承した独自クラスを
/// <c>Application.Run(ApplicationContext)</c> に渡す形にすることで、
/// 「表示中のフォームの有無」ではなく「このコンテキストが生きている間」
/// メッセージループを回し続ける、という制御に切り替えられる。
/// 終了は明示的にユーザーが「終了」メニューを選んだときだけ行う。
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly WebApplication _webApp;
    private readonly NotifyIcon _notifyIcon;
    private readonly HttpClient _httpClient;

    public TrayApplicationContext(WebApplication webApp)
    {
        _webApp = webApp;
        _httpClient = new HttpClient();

        var menu = new ContextMenuStrip();
        menu.Items.Add("ステータス確認", image: null, OnCheckStatusClicked);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", image: null, OnExitClicked);

        // SystemIcons.Application は「適当な標準アイコン」として用意されているもの。
        // 本来のプロダクトであれば .ico ファイルを埋め込んで専用アイコンにするが、
        // 学習用途では標準アイコンで十分。
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = $"DICOM Tool 常駐アプリ (ポート{ServicePorts.TrayAppHttp}で待受中)",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    /// <summary>
    /// 「ステータス確認」メニュー。自分自身が待ち受けているHTTP API(/health)に
    /// 実際にリクエストを飛ばし、その応答をそのままダイアログに出す。
    /// 同一プロセス内から自分のHTTPサーバーへアクセスしているだけだが、
    /// 「Kestrelが実際に同一プロセス内で生きて応答を返せている」ことを
    /// 目に見える形で確認できるようにする意図がある。
    /// </summary>
    private async void OnCheckStatusClicked(object? sender, EventArgs e)
    {
        try
        {
            var response = await _httpClient.GetAsync($"http://localhost:{ServicePorts.TrayAppHttp}/health");
            var body = await response.Content.ReadAsStringAsync();

            MessageBox.Show(
                $"HTTP APIは正常に応答しています。{Environment.NewLine}{Environment.NewLine}" +
                $"GET /health -> {(int)response.StatusCode} {response.StatusCode}{Environment.NewLine}{body}",
                "DICOM Tool 常駐アプリ - ステータス確認",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"HTTP APIへの接続に失敗しました。{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "DICOM Tool 常駐アプリ - ステータス確認",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 「終了」メニュー。ASP.NET CoreのKestrelホストを行儀よく止めてから
    /// WinFormsのメッセージループを終了させる。
    /// </summary>
    private async void OnExitClicked(object? sender, EventArgs e)
    {
        // 先にトレイアイコンを消しておく（終了処理に時間がかかっても
        // ユーザーからは「もう終了した」ように見せるため）。
        _notifyIcon.Visible = false;

        try
        {
            // WebApplication(=IHost)を突然Killするのではなく、StopAsyncで
            // 受付中のリクエストの完了などを待ちつつ穏当にシャットダウンする。
            // 何らかの理由で長時間かかる場合に備えてタイムアウトを設けておく。
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _webApp.StopAsync(cts.Token);
        }
        catch
        {
            // シャットダウンに失敗しても、常駐アプリを終了させるという
            // ユーザー操作自体は必ず完遂させたいので握りつぶす。
        }

        // WinForms側のメッセージループ(Application.Run)を終了させ、プロセスを終了させる。
        Application.Exit();
    }
}
