"""
test_timeline.py
==================
【シナリオ】Worklist画面から、常駐トレイアプリ(DicomTool.TrayApp)へ「Timelineを開いて」という
命令をHTTP経由で送り、CORS設定・接続先URLが正しく機能していることを確認するE2Eテスト。

実際の運用では、Worklist上で検査を右クリックすると、JavaScript(fetch)がユーザーの
手元PCで常駐しているトレイアプリ(既定でlocalhost:5299)に対してPOSTリクエストを送り、
トレイアプリがブラウザでTimeline画面を開く、という流れになっている
（docs/CONTRACT.md参照。WorklistはVM上で動いていても、トレイアプリはユーザーの
手元PCで動いている前提のため、ベースURLがVMのIPではなくlocalhostになっている）。

この一連の流れは「異なるオリジン間の通信」であるため、CORS設定を誤ると
ブラウザにブロックされて動かなくなる。このテストは実際のボタンクリックではなく
page.evaluate()でJavaScriptのfetchを直接実行することで、「本物のブラウザの
CORSチェックを経由した場合に、本当に接続できるか」を確認する
（ボタンクリックではなくfetch直接実行にしているのは、実際に発生している問題が
UIの見た目ではなくネットワーク層(CORS)の問題であることが多いため、
原因を切り分けやすくする意図）。

【実行方法】
    pytest tests/e2e/test_timeline.py -v -s

【前提条件】
    - WORKLIST_BASE_URL で指定したWorklistとBackend APIが起動していること
    - TRAYAPP_BASE_URL で指定した常駐トレイアプリ(DicomTool.TrayApp)が
      ローカルで起動していること（既定は http://localhost:5299）
"""

from conftest import TEST_PATIENT_ID, TRAYAPP_BASE_URL, WORKLIST_BASE_URL


def test_tray_app_open_timeline_command_is_reachable_with_cors(logged_in_page):
    page = logged_in_page

    page.goto(f"{WORKLIST_BASE_URL}/")
    page.wait_for_load_state("networkidle")
    page.wait_for_timeout(1000)

    # page.evaluate()の中身はPythonではなくJavaScriptとして、そのままブラウザの中で実行される。
    # つまりこのfetchは「Worklistのオリジン(WORKLIST_BASE_URL)で動いているJSコードが、
    # 別オリジン(TRAYAPP_BASE_URL)へfetchする」という、実際の操作と全く同じCORS条件で実行される。
    # 第2引数のリストは、そのままJavaScript側の関数の引数として渡される
    # （Python変数の値をJavaScriptの文字列に直接埋め込むと、値にクォート文字が
    # 含まれていた場合に壊れるため、page.evaluate()の引数渡し機能を使うのが安全）。
    result = page.evaluate(
        """
        async ([trayAppBaseUrl, patientId]) => {
            try {
                const res = await fetch(trayAppBaseUrl + '/commands/open-timeline', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ patientId }),
                });
                const body = await res.json();
                return { ok: res.ok, status: res.status, body };
            } catch (e) {
                // CORSでブロックされた場合、ブラウザはエラーの詳細を隠すため
                // "Failed to fetch" のような素っ気ないメッセージになることが多い。
                return { error: String(e) };
            }
        }
        """,
        [TRAYAPP_BASE_URL, TEST_PATIENT_ID],
    )

    assert "error" not in result, (
        "常駐トレイアプリへのfetchが失敗した（CORS設定漏れ、または常駐トレイアプリが"
        f"起動していない可能性がある）。TRAYAPP_BASE_URL={TRAYAPP_BASE_URL} 結果: {result}"
    )
    assert result["ok"] is True, f"常駐トレイアプリがエラー応答を返した: {result}"
