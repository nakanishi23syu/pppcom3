# VM運用手順書 ― 日常の起動・終了作業

> このドキュメントは、[`VM構築手順.md`](./VM構築手順.md)で構築済みのVM環境（`dicom-pacs-vm`,
> `192.168.93.128`）を**日常的に使い始める・使い終わる**際の手順をまとめたものです。
> 構築そのものの手順（IIS/PostgreSQL/NSSM等のセットアップ）は`VM構築手順.md`を参照してください。
> このドキュメントは「もう構築は終わっていて、日々の運用だけしたい」人向けです。

---

## 0. 前提知識（重要）

VM上の全サービス（PostgreSQL, IIS, `DicomToolScp`, `DicomToolWorker`, `TemporalServer`,
`DicomToolWorklist`, `DicomToolViewer`）は、すべてWindowsサービスとして**「自動」起動**に
設定済みです。そのため、

- **VMの電源さえ入れば、中のサービスは何もしなくても自動的に立ち上がります。**
- 逆にVMをシャットダウンすれば、Windows OSのシャットダウンシーケンスの一部として
  各サービスは自動的に正常停止します。個別に1つずつ止める必要はありません。

本当に人手が必要なのは次の2つだけです。

1. VM自体の電源を入れる／落とす
2. ホストPC側の`DicomTool.TrayApp`を起動する／止める
   （このアプリはVMには載っていない。実際に読影医が使うPCで動かす常駐アプリのため）

`start-all.bat` / `stop-all.bat` は、この2つの作業＋簡単な健全性チェックを自動化したものです。

---

## 1. 日常的な開始の流れ（`start-all.bat`を使う場合）

1. リポジトリ直下の `start-all.bat` をダブルクリック、またはコマンドプロンプトから実行する。
2. スクリプトが以下を順に行う:
   1. `vmrun`（VMware Workstation付属コマンド）が使えれば、VMを起動する。
      使えない場合はメッセージが出るので、VMware Workstationの画面から手動でVMを
      パワーオンしてから、何かキーを押して続行する。
   2. VMのSSHに疎通するまで、最大120秒程度リトライしながら待つ。
   3. 疎通したら、主要ポート（5030, 3100, 3200, 5230, 7233, 11112）の生死を
      `netstat`で軽くチェックし、念のため各Windowsサービスに`nssm start`を試みる
      （既に起動済みのサービスに対しては何も起きない、無害な操作）。
   4. ホストPC側で`DicomTool.TrayApp`がまだ起動していなければ、
      `RemoteHost=192.168.93.128`を設定した新しいウィンドウで自動的に起動する。
      既に起動済みなら何もしない。
   5. 最後に、Worklist・Viewer・Timeline・Temporal Web UI等のURL一覧を表示して終了する。
3. 表示されたURLをブラウザで開いて作業を開始する。

## 2. 日常的な終了の流れ（`stop-all.bat`を使う場合）

1. リポジトリ直下の `stop-all.bat` をダブルクリック、またはコマンドプロンプトから実行する。
2. スクリプトが以下を順に行う:
   1. ホストPC側の`DicomTool.TrayApp.exe`プロセスを終了する。
   2. SSH経由でVMに`shutdown /s /t 0`を送り、VM自体をシャットダウンする
      （VM内の各サービスは自動起動設定のため、OSシャットダウンに伴って自動的に
      正常停止する。個別のサービス停止操作は不要）。
   3. `vmrun`が使える場合は、VMの電源が実際に切れるまで最大60秒程度様子を見る
      （必須の処理ではなく、あくまで確認用）。
3. 完了メッセージが出たら終了。VMware Workstationの画面でも仮想マシンが
   「パワーオフ」になっていることを確認できる。

---

## 3. 初回だけ必要な設定

`start-all.bat`（と`stop-all.bat`）は、**あなたの環境のVMXファイルのパスを知らない**ため、
最初の1回だけ以下の書き換えが必要です。

1. `start-all.bat` をテキストエディタで開く。
2. 冒頭付近にある以下の行を見つける:
   ```
   set VMX_PATH=C:\path\to\your\vm.vmx
   ```
3. これを、実際のvmxファイルのフルパスに書き換える。確認方法:
   - VMware Workstationのライブラリで対象の仮想マシンを右クリック →「設定」を開くと、
     ウィンドウ内または設定ファイルの情報としてvmxパスを確認できることが多い。
   - もしくは、既定の保存先（`%USERPROFILE%\Documents\Virtual Machines\<VM名>\<VM名>.vmx`）
     を確認する。
4. `stop-all.bat` 側にも同名の`VMX_PATH`変数があるので、**同じ値に**書き換えておく
   （こちらは、VMの電源が実際に切れたかを`vmrun`で確認する処理でのみ使われる、任意の機能）。
5. （任意）`start-all.bat`内の`NSSM_REMOTE_PATH`は既定で`C:\Tools\nssm.exe`
   （`VM構築手順.md` 16章の例）になっている。VM側で別の場所に`nssm.exe`を置いた場合は
   ここも書き換える。

書き換えが不要な項目（通常そのままでよい）:
- `SSH_HOST`（`dicomvm`。`~/.ssh/config`に登録済みのエイリアス）
- `REMOTE_HOST_IP`（`192.168.93.128`。VM構築手順.md記載の固定IP）
- `TRAYAPP_PROJECT`（`services\DicomTool.TrayApp`）

---

## 4. 手動でやりたい場合の代替手順

バッチファイルを使わず、すべて手動で行いたい場合の手順です。

### 開始時

1. VMware Workstationの画面から、対象の仮想マシンを選択して
   「この仮想マシンをパワーオン」をクリックする。
2. **これだけで終わりです。** VM内の各サービス（PostgreSQL, IIS, DicomToolScp,
   DicomToolWorker, TemporalServer, DicomToolWorklist, DicomToolViewer）はすべて
   自動起動設定のため、特に何もする必要はありません
   （心配な場合は、少し待ってから`ssh dicomvm "netstat -ano | findstr :5030"`等で
   ポートが`LISTENING`になっているか確認してもよい）。
3. ホストPC側でTrayAppだけ手動起動する:
   ```powershell
   $env:RemoteHost = "192.168.93.128"
   cd D:\Programming\lerning\dicom-tool-3\services\DicomTool.TrayApp
   dotnet run
   ```
   （`RemoteHost`の設定を忘れると、CORSエラーや誤ったURLへのアクセスが発生する。
   詳細は`VM構築手順.md` 26章を参照）。

### 終了時

1. ホストPC側のTrayAppのウィンドウを閉じる（またはタスクマネージャーで
   `DicomTool.TrayApp.exe`を終了する）。
2. VM内にリモートデスクトップ等で接続し、通常のWindowsのシャットダウン操作を行う
   （スタートメニュー→電源→シャットダウン、または管理者権限のコマンドプロンプトで
   `shutdown /s /t 0`）。
   各サービスは自動起動設定のWindowsサービスなので、シャットダウンシーケンスの中で
   自動的に正常停止する。個別にサービスを止める必要はない。
3. VMware Workstationの画面で仮想マシンが「パワーオフ」表示になっていることを確認する。

---

## 5. トラブルシュート

### SSHが疎通しない（`start-all.bat`が120秒待っても`SSH is reachable.`と出ない）

- VMがまだ起動途中の可能性が高い。VMware Workstationの画面でVMのデスクトップ画面が
  表示され、サインイン画面またはサインイン後の状態になっているか確認する。
- ホストPCから直接疎通確認する:
  ```powershell
  ping 192.168.93.128
  ssh dicomvm "echo test"
  ```
  `ping`が通らない場合はVMのネットワークアダプタやVMware側のNAT設定を確認
  （`VM構築手順.md` 9章参照）。
- `ssh`コマンド自体が「そのようなホストが見つかりません」等になる場合、
  `~/.ssh/config`に`Host dicomvm`のエイリアスが正しく登録されているか確認する。

### 主要ポートの一部が`LISTENING`にならない（サービスが起動していない）

- VMにSSH接続して、対象サービスの状態を直接確認する:
  ```powershell
  ssh dicomvm "C:\Tools\nssm.exe status DicomToolScp"
  ssh dicomvm "C:\Tools\nssm.exe status DicomToolWorker"
  ssh dicomvm "C:\Tools\nssm.exe status TemporalServer"
  ssh dicomvm "C:\Tools\nssm.exe status DicomToolWorklist"
  ssh dicomvm "C:\Tools\nssm.exe status DicomToolViewer"
  ```
- `SERVICE_STOPPED`等が返る場合は手動起動を試す:
  ```powershell
  ssh dicomvm "C:\Tools\nssm.exe start DicomToolScp"
  ```
- それでも起動しない場合、NSSMの「I/O」タブで設定したstdout/stderrログファイル
  （例: `C:\apps\DicomTool.DicomScp\logs\stderr.log`）を確認する。大抵は
  `appsettings.Production.json`の接続文字列ミスや、依存サービス（PostgreSQL、
  Temporal Server）がまだ起動していない状態でWorkerだけ先に起動してしまったことが原因。
  `VM構築手順.md` 27章「NSSMでサービス登録したのに数秒で停止する」も参照。
- PostgreSQL/IISはWindows標準のサービスなので `services.msc` から直接状態確認・起動も可能。

### `DicomTool.TrayApp`が起動しない／起動してもWorklistから認識されない

- `RemoteHost`環境変数が設定された状態で起動しているか確認する
  （`start-all.bat`は自動で設定するが、手動起動時に忘れがち）。
- 既に古い`DicomTool.TrayApp.exe`プロセスが残っていないか`tasklist`で確認し、
  残っていれば一度`taskkill /im DicomTool.TrayApp.exe /f`で終了してから起動し直す。
- 詳細な症状と原因は`VM構築手順.md` 26章を参照。

### `stop-all.bat`実行後もVMware Workstation上でVMが「実行中」のまま

- Windows Serverのシャットダウンには時間がかかることがある。数分待ってから
  VMware Workstationの画面で状態を再確認する。
- 保留中のWindows Update適用などでシャットダウンが長引いている可能性もある。
  VMware Workstationのコンソール画面を開いて直接状態を見るのが確実。
