"""
test_timeline_load.py
=======================
【シナリオ】Blazor WebAssemblyで実装されたTimelineアプリ(frontend/timeline)が、
指定した患者IDのURLに直接アクセスしたときに、コンソールエラーやHTTPエラー
（404/500等）を出さずに正しく読み込まれることを確認するE2Eテスト。

Blazor WebAssemblyは「ブラウザ上で.NETアセンブリ(dll)一式をダウンロードして実行する」
という特殊な仕組みのため、
  - appsettings.json（接続先Backend APIのURL等の設定）が正しく配信されているか
  - .dll/.wasmファイル一式が404にならず配信されているか
  - JavaScript側の初期化やC#側の例外が発生していないか
といった、通常のNuxt3(SSR/SPA)アプリとは異なる種類の「読み込み失敗」が起こりうる。
このテストは、そうした「画面が真っ白になる/固まる」系の不具合を検知することを目的にしている。

【実行方法】
    pytest tests/e2e/test_timeline_load.py -v -s

【前提条件】
    TIMELINE_BASE_URL で指定したTimelineアプリ(Blazor WebAssembly)が
    起動していること。ログインは不要（Timelineは常駐トレイアプリ経由で
    直接URLを開く画面のため、このテストでもログイン処理は行わない）。
"""

import os

from conftest import OUTPUT_DIR, TEST_PATIENT_ID, TIMELINE_BASE_URL


def test_timeline_page_loads_without_console_or_http_errors(page):
    console_logs = []
    failed_requests = []
    appsettings_requests = []

    page.on("console", lambda msg: console_logs.append(f"[{msg.type}] {msg.text}"))
    page.on("pageerror", lambda exc: console_logs.append(f"[pageerror] {exc}"))

    def on_response(resp):
        if resp.status >= 400:
            failed_requests.append(f"{resp.status} {resp.url}")
        if "appsettings" in resp.url:
            try:
                body = resp.text()
            except Exception as e:  # レスポンスボディの取得自体に失敗するケースも記録しておく
                body = f"<body read error: {e}>"
            appsettings_requests.append(f"{resp.status} {resp.url}\n{body}")

    page.on("response", on_response)
    page.on("requestfailed", lambda req: failed_requests.append(f"FAILED {req.url} - {req.failure}"))

    page.goto(f"{TIMELINE_BASE_URL}/timeline/{TEST_PATIENT_ID}", wait_until="load")
    # Blazor WebAssemblyは.NETランタイム一式をダウンロード・初期化するため、
    # 通常のSPAより初回ロードに時間がかかる。初期化が完了してエラーが出揃うまで待つ。
    page.wait_for_timeout(8000)
    page.screenshot(path=os.path.join(OUTPUT_DIR, "timeline_load_result.png"), full_page=True)

    assert not failed_requests, (
        "Timelineページのロード中にHTTPエラー（404/500等）が発生した。"
        f"詳細: {failed_requests}\n"
        f"appsettings関連のレスポンス: {appsettings_requests}"
    )

    # console.error / pageerror（未処理のJS例外・C#側の例外がJS側に伝播したもの）が
    # 1件でも出ていれば、画面が正常に動作していない可能性が高い。
    error_logs = [log for log in console_logs if log.startswith("[error]") or log.startswith("[pageerror]")]
    assert not error_logs, f"Timelineページの読み込み中にコンソールエラーが発生した: {error_logs}"
