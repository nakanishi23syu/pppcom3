"""
conftest.py
============
tests/e2e/ 配下の全テストで共通して使う設定値・pytestフィクスチャをまとめたファイル。
pytestは同じディレクトリ（またはその親ディレクトリ）にある `conftest.py` を自動的に
読み込み、その中で定義した `@pytest.fixture` を全テスト関数から
「引数として名前を書くだけ」で使えるようにする仕組みを持っている
（import文を書かなくても、pytestが自動的に対応する名前のフィクスチャを見つけて渡してくれる）。

【設定値について ―― 環境変数で上書きできるようにしている理由】
このディレクトリのテストは、実際に動いているVM(仮想マシン)上の各サービス
（Worklist・Timeline・常駐トレイアプリ・Backend API等）に対してPlaywright(ブラウザ自動操作)で
アクセスする「E2E(End-to-End)テスト」。IPアドレスやポート番号、ログイン用ID/パスワードは
環境（どのVM/どのマシンで動かすか）によって変わりうるため、Pythonコードに直接埋め込む
（ハードコードする）のではなく、環境変数から読み取り、環境変数が未設定の場合にのみ
「現状の開発用VM」の値をデフォルトとして使うようにしている。
これにより、別のVMや別の開発者の手元環境でテストを動かしたい場合も、
コードを書き換えずに環境変数を設定するだけで済む。

例（PowerShellの場合）:
    $env:WORKLIST_BASE_URL = "http://192.168.1.50:3100"
    pytest tests/e2e/

例（bashの場合）:
    WORKLIST_BASE_URL=http://192.168.1.50:3100 pytest tests/e2e/
"""

import os

import pytest
from playwright.sync_api import sync_playwright

from generate_test_dicom import generate_sample_dicom

# ============================================================================
# 環境変数から読み取る設定値（未設定時はVM構築手順.md記載の開発用VMの値を既定値にする）
# ============================================================================

# Worklist(Nuxt3、検査一覧・アップロード画面)のベースURL。
WORKLIST_BASE_URL = os.environ.get("WORKLIST_BASE_URL", "http://192.168.93.128:3100")

# Timeline(Blazor WebAssembly、患者タイムライン画面)のベースURL。
TIMELINE_BASE_URL = os.environ.get("TIMELINE_BASE_URL", "http://192.168.93.128:5230")

# 常駐トレイアプリ(DicomTool.TrayApp、WinForms+Minimal API)のベースURL。
# Worklistの「右クリック→Timelineを開く」操作の送信先。VM上のブラウザから見て
# 「常駐トレイアプリはユーザーの手元PC(localhost)で動いている」という構成を再現するため、
# 既定値はVMのIPではなくlocalhostになっている点に注意（docs/CONTRACT.md参照）。
TRAYAPP_BASE_URL = os.environ.get("TRAYAPP_BASE_URL", "http://localhost:5299")

# ログイン用アカウント。backend/DicomTool.Api/Data/DbSeeder.cs が起動時に投入する
# 既定の管理者アカウントに合わせてある。
ADMIN_USERNAME = os.environ.get("ADMIN_USERNAME", "admin")
ADMIN_PASSWORD = os.environ.get("ADMIN_PASSWORD", "admin1234")

# スクリーンショットや生成したテスト用DICOMファイルの出力先。
# 既定値は「このconftest.pyがあるフォルダ(tests/e2e/) 直下の output/」。
OUTPUT_DIR = os.environ.get("OUTPUT_DIR", os.path.join(os.path.dirname(os.path.abspath(__file__)), "output"))
os.makedirs(OUTPUT_DIR, exist_ok=True)

# Timelineの読み込みテストで使う患者ID（サンプルデータに存在する前提のID）。
TEST_PATIENT_ID = os.environ.get("TEST_PATIENT_ID", "patient-103")


# ============================================================================
# Playwright関連のフィクスチャ
# ============================================================================
# 【フィクスチャのscopeについて】
# scope="session" … pytestプロセス全体で1回だけ作られ、全テストで使い回される
#                    （ブラウザの起動はコストが高いため、session全体で1個のブラウザを使い回す）。
# 既定のscope（省略時="function"） … テスト関数1つにつき毎回新しく作り直される
#                    （ページは毎回まっさらな状態から始めたいのでテストごとに作り直す）。

@pytest.fixture(scope="session")
def playwright_instance():
    """Playwright本体（ブラウザの起動・終了を管理するオブジェクト）。"""
    with sync_playwright() as p:
        yield p


@pytest.fixture(scope="session")
def browser(playwright_instance):
    """
    テストセッション全体で使い回す、ヘッドレス(画面を表示しない)Chromiumブラウザ。
    ブラウザプロセスの起動は数百ms〜数秒かかるため、テストごとに起動し直さず
    session全体で1つを使い回すことで実行時間を短縮する。
    """
    browser = playwright_instance.chromium.launch(headless=True)
    yield browser
    browser.close()


@pytest.fixture()
def page(browser):
    """
    各テスト用に新しく開く、まっさらなブラウザページ(タブ)。
    Cookie等の状態がテスト間で混ざらないよう、テストごとに新しいページを用意し、
    テスト終了後に閉じる。
    """
    page = browser.new_page()
    yield page
    page.close()


@pytest.fixture()
def logged_in_page(page):
    """
    Worklistにログイン済みの状態のページを返す共通フィクスチャ。
    「ログイン→何かを操作する」という流れのテストが複数あるため
    （test_upload.py, test_timeline.py）、ログイン処理そのものをここに1箇所へ集約している。
    """
    page.goto(f"{WORKLIST_BASE_URL}/login")
    page.wait_for_load_state("networkidle")
    page.fill('input[type="text"]', ADMIN_USERNAME)
    page.fill('input[type="password"]', ADMIN_PASSWORD)
    page.get_by_role("button", name="ログイン", exact=True).click()
    page.wait_for_load_state("networkidle")
    page.wait_for_timeout(1000)
    return page


@pytest.fixture()
def sample_dicom_file(tmp_path):
    """
    テストごとに新しく生成する、使い捨てのテスト用DICOMファイル。
    generate_test_dicom.py の generate_sample_dicom() を呼び出して、
    pytestが自動的に用意してくれる一時ディレクトリ(tmp_path、テスト終了後に自動掃除される)
    の中にファイルを作る。これにより、tests/e2e/output/ のような共有フォルダに
    テスト実行のたびにファイルが増え続けることを避けている
    （スクリーンショットはOUTPUT_DIRに残すが、入力用DICOMファイルは使い捨てでよいため）。

    戻り値は辞書で、生成したファイルパスに加えて、DICOMタグに埋め込んだ
    PatientID/StudyInstanceUIDも一緒に返す（アップロード後の反映確認で
    「本当に今回アップロードした検査が表示されているか」を照合するのに使う）。
    """
    output_path = os.path.join(str(tmp_path), "sample.dcm")
    path, study_instance_uid, patient_id, study_description = generate_sample_dicom(output_path=output_path)
    return {
        "path": path,
        "study_instance_uid": study_instance_uid,
        "patient_id": patient_id,
        "study_description": study_description,
    }
