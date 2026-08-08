# tests/e2e/ ―― Playwright + pytest によるE2E(End-to-End)テスト

このディレクトリのテストは、**実際に動いているVM(仮想マシン)上の各サービス**
（Worklist・Timeline・常駐トレイアプリ・Backend API・Temporal Server/Worker・PostgreSQL等）に対して、
Playwrightで本物のブラウザを自動操作し、「ユーザーが実際にこの手順で使えるか」を
最終確認するためのE2Eテストです。

> **重要:** これらは単体テスト・結合テストではありません。**VM(またはそれに相当する
> 実行環境)上で、対象の全サービスが起動している状態**でなければ、ほぼ全てのテストが
> 接続エラーで失敗します。CI等で自動実行する前提の作りにはなっていません。

## backend/DicomTool.Api.Tests との役割分担

このリポジトリには2種類のテストがあります。

| | backend/DicomTool.Api.Tests | tests/e2e/ (このディレクトリ) |
|---|---|---|
| 何をテストするか | GraphQL Mutation/Queryのロジック | 実際の画面操作・複数サービス間の連携 |
| 依存する外部サービス | なし（DBはInMemory、Temporalはモックに差し替え） | 本物のVM上の全サービス |
| 実行速度 | 速い（数秒） | 遅い（画面遷移・実処理の完了待ちがあるため） |
| 実行の安定性 | 高い（毎回同じ結果になる） | VMの状態・タイミングに左右され、稀に揺れることがある |
| 実行タイミング | いつでも気軽に(`dotnet test`) | VMが起動しているときに、最終確認として |

「ロジックが正しいか」の細かい確認は `backend/DicomTool.Api.Tests` で高速に何度も回し、
「本当にブラウザ・実インフラを通して使えるか」の最終確認だけをこちらのE2Eテストに
任せる、という考え方（いわゆる"テストピラミッド"）です。

## 前提条件

1. Pythonパッケージのインストール
   ```bash
   pip install -r tests/e2e/requirements.txt
   # または個別に:
   pip install pytest playwright pydicom numpy
   ```
2. Playwrightが使うChromiumブラウザ本体のインストール（Pythonパッケージの
   インストールとは別に、ブラウザバイナリを追加でダウンロードする必要があります）
   ```bash
   playwright install chromium
   ```
3. テスト対象のサービス一式が起動していること
   - Worklist(Nuxt3)・Backend API・PostgreSQL・Temporal Server・Temporal Worker
     （`test_upload.py` に必要）
   - 常駐トレイアプリ(DicomTool.TrayApp)がローカルで起動していること
     （`test_timeline.py` に必要）
   - Timelineアプリ(Blazor WebAssembly)が起動していること
     （`test_timeline_load.py` に必要）

   起動手順は `VM構築手順.md` / `手動セットアップ手順.md` を参照してください。

## 環境変数（実行環境ごとに上書き可能な設定値）

IPアドレスやログイン情報をコードに直接書き込まず、環境変数から読み取るようにしています
（`conftest.py` 参照）。未設定の場合は、現状の開発用VMの値が既定値として使われます。

| 環境変数 | 既定値 | 用途 |
|---|---|---|
| `WORKLIST_BASE_URL` | `http://192.168.93.128:3100` | Worklist(検査一覧・アップロード画面)のURL |
| `TIMELINE_BASE_URL` | `http://192.168.93.128:5230` | Timeline(患者タイムライン画面)のURL |
| `TRAYAPP_BASE_URL` | `http://localhost:5299` | 常駐トレイアプリのURL（ユーザーの手元PCで動く想定） |
| `ADMIN_USERNAME` | `admin` | ログインに使う管理者アカウントのユーザー名 |
| `ADMIN_PASSWORD` | `admin1234` | 同上パスワード |
| `OUTPUT_DIR` | `tests/e2e/output/` | スクリーンショット等の出力先フォルダ |
| `TEST_PATIENT_ID` | `patient-103` | Timeline系テストで使う患者ID（サンプルデータに存在する前提） |

例（別のVMに向けて実行する場合、PowerShell）:
```powershell
$env:WORKLIST_BASE_URL = "http://192.168.1.50:3100"
$env:TIMELINE_BASE_URL = "http://192.168.1.50:5230"
pytest tests/e2e/ -v -s
```

## 実行方法

全テストをまとめて実行する場合:
```bash
pytest tests/e2e/ -v -s
```
（`-v` はテスト名を詳しく表示、`-s` はテスト内のscreenshot保存パス等のprint出力を
表示するためのオプション。必須ではありません。）

1ファイルだけ実行する場合:
```bash
pytest tests/e2e/test_upload.py -v -s
```

実行すると、`OUTPUT_DIR`（既定 `tests/e2e/output/`）にスクリーンショットが保存されます。
テストが失敗したときの状況確認（画面が本当に想定通りに表示されていたか等）に使ってください。
このフォルダの中身は `.gitignore` で除外されており、リポジトリにはコミットされません。

## 各ファイルの説明

### `conftest.py`
pytestが自動的に読み込む共通設定ファイル。環境変数の読み取り、Playwrightの
ブラウザ起動・ページ作成・ログイン処理といった「複数のテストで共通して必要な処理」を
`@pytest.fixture` としてまとめています。テストコード本体からは
`def test_xxx(logged_in_page, sample_dicom_file):` のように引数名を書くだけで、
対応する準備済みのオブジェクトが渡ってきます。

### `generate_test_dicom.py`
pydicomを使って、テスト用の最小限のダミーDICOMファイル（256x256の
グラデーション画像1枚）を生成するユーティリティ。`test_upload.py` から
`conftest.py` 経由で呼び出されるほか、単体でも実行できます。
```bash
python tests/e2e/generate_test_dicom.py
```

### `test_upload.py`
**検証内容:** ログイン → アップロード画面でDICOMファイルを選択・送信 →
（Temporalワークフローの非同期処理を待って）検査一覧に反映されていることを確認。
「アップロード機能の一番肝心な、エンドツーエンドの流れ」を確認するテストです。

### `test_timeline.py`
**検証内容:** Worklist画面から常駐トレイアプリへ、実際のブラウザのCORSチェックを
経由してHTTPリクエストを送り、正しく応答が返ってくることを確認。
「別オリジンのサービス間通信のCORS設定が壊れていないか」を確認するテストです。

### `test_timeline_load.py`
**検証内容:** Timelineアプリ(Blazor WebAssembly)を患者IDつきのURLで直接開き、
HTTPエラー（404/500等）やJavaScript/C#側のエラーが発生せずに読み込めることを確認。
「Blazor WebAssemblyアプリ特有の読み込み失敗（appsettings.json配信不備、
dll/wasmの404等）」を検知するテストです。ログインは不要です。

## 既知の制約・注意点

- これらのテストはネットワーク越しに本物のサービスへ接続するため、対象のVM/サービスが
  停止していると、ほぼ全て失敗します（エラーメッセージに接続失敗の詳細が出ます）。
- `test_upload.py` はTemporalワークフローの非同期処理の完了を固定時間の待機
  （`page.wait_for_timeout(...)`）で待っています。実行環境の負荷によっては、
  待機時間内に処理が終わらず、稀に失敗することがあります。
- CI(継続的インテグレーション)での自動実行は想定していません。手元やVM上で、
  必要なときに手動で実行することを想定しています。
