# VM構築手順書 ― Windows Server 2025 + IIS + PostgreSQL に dicom-tool-3 を載せるまで

> このドキュメントは [`手動セットアップ手順.md`](./手動セットアップ手順.md) 7章が簡素すぎたための詳細版です。
> ローカルPC上でのdocker-compose環境構築（[`README.md`](./README.md)のクイックスタート）は行わず、
> **最初から実務相当のVM環境を作る**ことだけを目的にしています。上から順に読んで、そのまま実行できる
> 粒度で書いています。各ステップに「なぜそうするか」の解説を付けています。

---

## 0. 前提として理解しておくこと

- `実務環境再現_提案書.md`には「VM構築・PostgreSQL導入・IIS採用は完了済み」と書かれていますが、
  これはヒアリング時点の申告ベースの記述であり、**実際にこのドキュメントの読者が手を動かして
  ゼロから構築する**という前提で本書を書いています。既にVMがある場合は該当ステップを読み飛ばして
  構いません。
- 目標のIPアドレスは `192.168.93.128` です。このIPは「宇宙のどこでも同じ値になる固定値」では
  **ありません**。VMware Workstationがインストール時に自動生成する仮想ネットワーク（NAT）のサブネットは
  PCごとに異なります。`192.168.93.128`は、おそらく「このプロジェクトの元になったPC環境でVMware Workstationが
  たまたま採番したNATサブネット(`192.168.93.0/24`)の、DHCPで最初に払い出されたアドレス」です。
  9章で、あなたの環境で実際にどのサブネットが使われているかを確認してから、同じ考え方で固定IPを
  設定する手順を説明します（あなたの環境でも数字が一致するとは限らないので、無理に`192.168.93.128`に
  合わせる必要はありません。合わせたい場合の対処法も9章に書いています）。
- **このVMに載せるもの／載せないもの**を最初に明確にしておきます。

| コンポーネント | このVMに載せる？ | 理由 |
|---|---|---|
| PostgreSQL | ○ 載せる | 提案書の通り、DBサーバーとしての役割 |
| Backend API (`DicomTool.Api`) | ○ 載せる | IIS配下でホストするWebサービス |
| Worklist / Viewer (Nuxt) | ○ 載せる | IIS配下でリバースプロキシ経由ホスト |
| Timeline (Blazor WASM) | ○ 載せる | ビルド後は静的ファイルなのでIISで直接配信 |
| DICOM SCP (`DicomTool.DicomScp`) | ○ 載せる | 外部モダリティからの通信を受ける常時稼働プロセス |
| Temporal Server | ○ 載せる | ワークフローエンジン本体 |
| Temporal Worker (`DicomTool.Worker`) | ○ 載せる | 常時稼働のバックグラウンドプロセス |
| **常駐トレイアプリ (`DicomTool.TrayApp`)** | **× 載せない** | これは「読影医個人のPCのタスクトレイに常駐するデスクトップアプリ」です。サーバーであるこのVMではなく、**利用者一人ひとりのWindows PCにインストールするもの**です。VM上で`Application.Run`しても誰も見ることのないデスクトップセッションで無意味に起動するだけになります。 |

- **Dockerは使いません。** ローカルPCでの検証(`docker-compose.yml`)ではDocker(PostgreSQL/Temporal)を
  使いましたが、VM上ではあえて使わず、PostgreSQL・Temporalともにネイティブインストールします。
  理由: このVM自体がVMware Workstation上の仮想マシンであり、その中でさらにDocker Desktop
  （Linuxコンテナを動かすためにHyper-VまたはWSL2という「もう1段階の仮想化」を必要とする）を
  動かすには「入れ子の仮想化(Nested Virtualization)」をVMware側で明示的に有効化する必要があり、
  ホスト機とVMware・Windows Serverの組み合わせによっては動作が不安定になりがちです。
  ネイティブインストールであればこの問題を根本的に回避できます。

---

## 1. 事前に準備するもの（ダウンロードは全部Windows側のホストPCで先に済ませておく）

| 項目 | 入手先 | 備考 |
|---|---|---|
| VMware Workstation Pro インストーラ | Broadcom公式ダウンロードページ | 2024年11月以降、個人利用なら無料 |
| Windows Server 2025 評価版ISO | https://www.microsoft.com/en-us/evalcenter/evaluate-windows-server-2025 | 180日評価版。`slmgr /rearm`で延長可（4章参照） |
| （このPC上のリポジトリ） | `D:\Programming\lerning\dicom-tool-3` 一式 | VMへ転送する対象 |

VMware Workstation Proの入手先URLは、頻繁にパスが変わるため本書には直接記載しません。
[`実務環境再現_提案書.md`](./実務環境再現_提案書.md)末尾の脚注[^1][^2]にある2つのリンク
（VMwareの無料化告知ブログとBroadcomのダウンロード案内）から辿ってください。

---

## 2. VMware Workstation Pro のインストール

1. 上記の入手先からインストーラをダウンロードし実行。
2. インストールウィザードは基本的に「次へ」で進めてよい。ライセンスキー入力画面が出た場合は
   **空欄のまま「次へ」**（2024年11月以降、個人利用は未入力でライセンス認証なしで使える）。
3. インストール完了後、一度PCの再起動を求められることがある（Hyper-Vとの排他制御ドライバの都合）。
   再起動する。
4. **注意**: Windows 11で「Hyper-V」「Windowsハイパーバイザー プラットフォーム」「メモリ整合性
   (Core Isolation)」等が有効になっていると、VMware Workstationの動作が不安定になったり、
   VM起動が極端に遅くなることがあります。普段Docker DesktopをWSL2バックエンドで使っている場合は
   Hyper-Vが有効になっている可能性が高いです。問題が出た場合は「Windowsの機能の有効化または無効化」で
   Hyper-V関連のチェックを外して再起動してから試してください（このPC上のdicom-tool-3のローカル検証で
   Docker Desktopを使い続けたい場合は、Hyper-Vを無効化するとDocker Desktopが動かなくなる可能性がある点に
   注意。両立させたい場合はVMware Workstation 17.x以降のHyper-V互換モードを使うか、Docker Desktopと
   VMware Workstationを同時に使わない運用にする）。

---

## 3. 仮想ネットワーク(NAT/VMnet8)のサブネットを確認する

VMを作る**前に**確認しておくと、後々「なぜこのIPになるのか」を迷わずに済みます。

1. VMware Workstationを起動 → メニュー「編集(Edit)」→「仮想ネットワークエディター(Virtual Network Editor)」。
2. 管理者権限が必要な場合は「設定の変更(Change Settings)」をクリック（UAC許可）。
3. 一覧から `VMnet8`（種別: NAT）を選択。
4. 画面下部に表示される「サブネットIP(Subnet IP)」と「サブネットマスク(Subnet Mask)」を確認する
   （例: `192.168.93.0` / `255.255.255.0` のように表示される）。これがあなたの環境でのNATネットワークです。
5. 「NAT設定(NAT Settings)」ボタンを押すと、ゲートウェイIP（例: `192.168.93.2`）が確認できる。
6. 「DHCP設定(DHCP Settings)」ボタンを押すと、DHCPで払い出す範囲（例: `192.168.93.128` 〜
   `192.168.93.254`）が確認できる。**この範囲の先頭が`192.168.93.128`であれば、まさにそれが
   このドキュメントの目標IPの正体**（VMが一番最初にDHCPで受け取ったアドレス）です。
7. 確認したサブネットIP・サブネットマスク・ゲートウェイIPの3つをメモしておく（9章の固定IP設定で使う）。

あなたの環境でサブネットが`192.168.93.0/24`以外だった場合、無理に`192.168.93.128`に合わせる必要は
ありません。以降の手順の「192.168.93.128」は、あなたの環境で確認した値に読み替えてください
（例えば`192.168.174.0/24`が確認できたなら、固定IPは`192.168.174.128`のように、同じサブネット内の
DHCP範囲に含まれるアドレスを選ぶ）。

---

## 4. 仮想マシンの新規作成

1. VMware Workstationのホーム画面で「新規仮想マシンの作成(Create a New Virtual Machine)」。
2. 「カスタム(Custom / Advanced)」を選択（互換性オプションを自分で見られるため。標準でも問題なし）。
3. インストーラディスクイメージファイル(iso)に、1章でダウンロードしたWindows Server 2025のISOを指定。
4. ゲストOSの種類: 「Microsoft Windows」、バージョン: 「Windows Server 2025」（一覧に無ければ
   「Windows Server 2022」等の近いものを選んでも動作上は問題ない。後からVM設定で変更も可能）。
5. 仮想マシン名・保存先フォルダを指定（例: `DicomToolVM`）。空き容量が十分なドライブを選ぶこと
   （Windows Server本体だけで20GB以上、DICOM画像を貯めるなら余裕を見て**80GB以上**を推奨）。
6. **プロセッサ数**: 提案書に明記が無いためデフォルト（2コア程度）でよい。
7. **メモリ**: 提案書の確定事項通り **4096MB (4GB)** を指定。
8. **ネットワークの種類**: 「NAT(Use network address translation)」を選択（3章で確認したVMnet8を使う）。
9. I/Oコントローラ・ディスクの種類はデフォルトのまま進めてよい。
10. ディスク容量: 上記6の通り80GB程度を指定。「単一ファイルとして格納」でも「複数ファイルに分割」でも
    どちらでもよい（複数ファイル分割の方がホスト側のファイルシステム都合で扱いやすいことが多い）。
11. 「完了」で仮想マシンが作成される。

---

## 5. Windows Server 2025 のインストール

1. 作成した仮想マシンを選択し「この仮想マシンをパワーオン(Power on this virtual machine)」。
2. インストーラが起動する。言語・時刻・キーボードの形式を選択し「次へ」。
3. 「今すぐインストール」をクリック。
4. **エディションの選択**が出る。学習用途であれば **「Windows Server 2025 Standard Evaluation
   (デスクトップ エクスペリエンス)」** を選ぶ（GUIありのデスクトップ環境。IIS管理画面等をこのVM内で
   直接GUI操作したい場合はデスクトップ エクスペリエンス版が扱いやすい。Server Core版はGUIが無く
   PowerShell操作のみになるため、学習目的では非推奨）。
5. ライセンス条項に同意。
6. インストールの種類は「カスタム: Windows のみをインストールする」を選択（アップグレードではなく
   新規インストールのため）。
7. インストール先ディスクは、4章で作成した仮想ディスク（未割り当て領域）を選択して「次へ」。
   自動的にパーティションが作成されインストールが始まる。
8. インストール完了後、自動的に再起動が数回入る。初回サインイン時に**Administratorパスワード**の
   設定を求められるので、忘れないよう記録しておく。

---

## 6. VMware Tools のインストール

VMware Toolsを入れないと、画面解像度が合わなかったり、ホスト⇔ゲスト間のファイルコピー/クリップボード
共有ができず作業効率が悪いため、最初に入れておく。

1. Windows Serverにサインイン後、VMware Workstationのメニューから
   「VM」→「VMware Toolsのインストール(Install VMware Tools)」をクリック
   （仮想CDドライブとしてマウントされる）。
2. ゲストOS内でエクスプローラーを開き、マウントされたドライブ（`D:`等）の`setup64.exe`を実行。
3. ウィザードに従ってインストール、完了後に再起動を求められたら再起動する。

---

## 7. 初期設定

1. **コンピューター名の変更**（任意だが推奨）: サーバーマネージャー(Server Manager)が自動起動する
   ので、左メニュー「ローカルサーバー」→「コンピューター名」の横のリンクをクリック →
   「変更」→ 分かりやすい名前（例: `DICOM-PACS-VM`）に変更 → 再起動を求められたら再起動。
2. **Windows Update**: サーバーマネージャー左メニュー「ローカルサーバー」→「Windows Update」から
   更新プログラムを確認・適用しておく（後述のIIS/ASP.NET Core周りの脆弱性修正が含まれることがあるため）。
3. **リモートデスクトップの有効化**（推奨・任意）: サーバーマネージャー「ローカルサーバー」→
   「リモートデスクトップ」の横のリンク →「このコンピューターへのリモート接続を許可する」を選択。
   これで以降はVMware Workstationのコンソール画面ではなく、ホストPCから`mstsc`（リモートデスクトップ
   接続）で作業できるようになり、コピー&ペーストや画面サイズの面で快適になる（クリップボード共有は
   VMware ToolsでもRDPでもどちらでも可能）。

---

## 8. Windows Defender ファイアウォールの基本方針

このVMは複数のサービスを外部（Worklistが動くPC等）に公開するため、後の章で個別にポートの受信規則を
追加していきます（24章に一覧をまとめています）。ここでは方針だけ理解しておいてください。

- Windows Serverの既定ファイアウォールは、標準的なロール（IIS等）を追加すると自動的に必要な規則
  （例: 「World Wide Web サービス (HTTP)」）を有効化してくれることが多い。
- ただしDICOM SCPのポート(`11112`)やTemporalのポート(`7233`)のようにWindowsが用途を知らない
  独自ポートは、**手動で受信規則を追加しないと外部から到達できない**（ローカルホスト内の通信は
  ファイアウォールの影響を受けないため、VM内で完結する動作確認では気づきにくい点に注意）。

---

## 9. 固定IPアドレスの設定

DHCPのままだと再起動のたびにIPが変わりうるため、サーバーには固定IPを設定するのが定石です。

1. VM内でスタートメニュー → 「ネットワークとインターネットの設定」（または コントロール パネル →
   ネットワークと共有センター → アダプターの設定の変更）を開く。
2. イーサネットアダプターを右クリック →「プロパティ」。
3. 「インターネット プロトコル バージョン 4 (TCP/IPv4)」を選択して「プロパティ」。
4. 「次のIPアドレスを使う」を選択し、3章でメモした値を使って以下のように入力する
   （下記は`192.168.93.0/24`だった場合の例。実際は自分の環境の値に置き換える）:
   - IPアドレス: `192.168.93.128`
   - サブネットマスク: `255.255.255.0`
   - デフォルトゲートウェイ: `192.168.93.2`（3章の「NAT設定」で確認した値）
5. DNSサーバーは「次のDNSサーバーのアドレスを使う」で、優先DNSにゲートウェイと同じアドレス
   （`192.168.93.2`）を入力する（VMwareのNATはDNSプロキシも兼ねていることが多い）。うまく名前解決
   できない場合は代わりに `8.8.8.8`（Google Public DNS）を試す。
6. 「OK」で保存。
7. 確認: VM内でコマンドプロンプトを開き `ipconfig` を実行し、設定したIPが反映されているか確認。
   続けて `ping 8.8.8.8` 等でインターネット疎通を確認（NAT経由でホストPCのネット回線を使って
   外に出られるはず）。
8. **ホストPC側からの疎通確認**: ホストPC（このリポジトリを操作しているWindows PC）のコマンド
   プロンプトから `ping 192.168.93.128` を実行し、応答が返ることを確認する。返らない場合は
   VM側のファイアウォールで「ICMPv4 エコー要求」が許可されているか確認する（既定でオンのことが多いが、
   環境によりオフになっている場合がある。「詳細設定」→「受信の規則」→「ファイルとプリンター共有
   (エコー要求 - ICMPv4受信)」を有効化）。

---

## 10. PostgreSQLのインストール

1. ホストPCのブラウザで PostgreSQL公式ダウンロードページ (`https://www.postgresql.org/download/windows/`)
   を開き、Windows用インストーラ（EDB社提供のインストーラ）をダウンロードする。バージョンは
   **16系**を選ぶ（このリポジトリの`docker-compose.yml`でもPostgreSQL 16イメージを使っており、
   `shared/DicomTool.Shared`のマイグレーションもPostgreSQL 16で動作確認済みのため、揃えておくと
   トラブルが少ない）。
2. ダウンロードしたインストーラをVMのデスクトップにコピーする方法は2通り:
   - VMware Toolsを入れていれば、ホストPCのファイルをドラッグ＆ドロップでVM画面にコピーできる。
   - もしくはVM内のブラウザで直接ダウンロードページを開いて取得してもよい（9章でインターネット
     疎通を確認済みのはず）。
3. インストーラを実行。コンポーネント選択画面で「PostgreSQL Server」「pgAdmin 4」
   （GUI管理ツール、動作確認に便利なので入れておく）「Command Line Tools」にチェックが入っている
   ことを確認して次へ。
4. データディレクトリはデフォルトのままでよい。
5. **スーパーユーザー(postgres)のパスワード**を設定する画面が出る。忘れないよう記録する
   （このリポジトリのアプリ用ユーザーとは別に、DB管理用の最上位ユーザー）。
6. ポート番号はデフォルトの **5432** のまま（`docs/CONTRACT.md`のポート一覧と一致させるため変更しない）。
7. ロケールはデフォルト（`Default locale`）のままでよい。
8. インストール完了後、Stack Builderの追加コンポーネント案内が出るが、今回は不要なのでスキップ
   （チェックを外して終了）してよい。
9. **アプリ用のデータベースとユーザーを作成する**:
   スタートメニューから「SQL Shell (psql)」を起動。Server/Database/Port/Usernameは全てEnterキーで
   デフォルトのまま進め、パスワードは5で設定したものを入力してログイン。ログインできたら以下を実行:

   ```sql
   CREATE USER dicomtool WITH PASSWORD 'ここに強固なパスワードを設定する';
   CREATE DATABASE dicomtool OWNER dicomtool;
   ```

   > ローカル検証用の`docker-compose.yml`では学習の簡便化のため`dicomtool_dev_password`という
   > 分かりやすい固定パスワードを使っていますが、**このVMは実際にネットワークへ公開される想定の
   > サーバー**です。ここでは必ず推測されにくいパスワードに変更してください
   > （`docs/CONTRACT.md`はローカル開発の既定値を書いたものであり、VM環境ではこの章の手順で
   > 別のパスワードを使う前提です。18章で、このパスワードをBackend API側の設定に反映します）。

10. **リモート接続を許可する設定**（既定では`localhost`からしか繋げない）:
    - PostgreSQLのデータディレクトリ（既定 `C:\Program Files\PostgreSQL\16\data`）にある
      `postgresql.conf` をメモ帳等（管理者権限で）で開き、`listen_addresses = 'localhost'` の行を
      `listen_addresses = '*'` に変更（コメントアウトの`#`が付いていれば外す）。
    - 同じフォルダの `pg_hba.conf` の末尾に以下の行を追加し、「VMのNATサブネット内からの接続」を許可する
      （192.168.93.0/24の部分は自分の環境のサブネットに置き換える。ホストPCおよび同じVMware
      ネットワーク上の他マシンからのアクセスを許可する設定）:
      ```
      host    dicomtool    dicomtool    192.168.93.0/24    scram-sha-256
      ```
    - 「サービス」管理画面（`services.msc`）から `postgresql-x64-16` サービスを再起動して設定を反映する。
11. ファイアウォールの受信規則を追加（PowerShellを管理者権限で実行）:
    ```powershell
    New-NetFirewallRule -DisplayName "PostgreSQL (5432)" -Direction Inbound -Protocol TCP -LocalPort 5432 -Action Allow
    ```
12. **ホストPCから接続確認**: ホストPCに`psql`があれば
    `psql -h 192.168.93.128 -U dicomtool -d dicomtool` で接続できるか確認する（無ければ後述の
    Backend API起動時のマイグレーション成功をもって確認とみなしてもよい）。

---

## 11. IISのインストール（サーバーの役割の追加）

1. サーバーマネージャー →「管理」→「役割と機能の追加」。
2. 「役割ベースまたは機能ベースのインストール」を選択して次へ。
3. 対象サーバー（このVM自身）を選択して次へ。
4. **サーバーの役割**一覧から **「Web サーバー (IIS)」** にチェック → 「機能の追加」ダイアログが
   出たらそのまま「機能の追加」をクリック。
5. 役割サービスの選択画面で、最低限以下にチェックが入っていることを確認する（デフォルトで
   多くが選択されているはずだが念のため）:
   - Web サーバー > 共通 HTTP 機能 一式（既定のドキュメント、静的コンテンツ 等）
   - Web サーバー > 正常性とその診断 > HTTP ログ
   - Web サーバー > パフォーマンス > 静的コンテンツの圧縮
   - **セキュリティ > 要求フィルター**（既定で入っている）
   - 管理ツール > **IIS 管理コンソール**
6. 「次へ」を進めて「インストール」。完了したら閉じる。
7. デスクトップまたはスタートメニューから「インターネット インフォメーション サービス(IIS)マネージャー」
   を起動できることを確認する。
8. VM内のブラウザで `http://localhost` を開き、IISの既定のウェルカムページが表示されれば
   インストール成功。
9. ホストPCのブラウザから `http://192.168.93.128` を開き、同じウェルカムページが見えることを確認
   （見えない場合、ファイアウォールでポート80が塞がれている可能性が高い。IISロール追加時に
   「World Wide Web サービス (HTTP 受信)」という規則が自動的に有効化されているはずだが、
   `wf.msc`で確認・有効化する）。

---

## 12. ASP.NET Core Hosting Bundle のインストール

IIS単体では ASP.NET Core (Kestrelベース)のアプリをそのままホストできません。IISのワーカープロセスと
Kestrelプロセスを橋渡しする **ASP.NET Core Module (ANCM)** と、.NET共有ランタイムをセットで導入する
「Hosting Bundle」が必要です。

1. VM内のブラウザで `https://dotnet.microsoft.com/` を開き、「Download .NET」→
   バージョン **10.0** の「Hosting Bundle」（ASP.NET Core Runtime のインストーラ群の中にある、
   IIS向けの統合インストーラ）を探してダウンロードする。
   （バージョン表記や配置場所はサイト更新で変わることがあるため、「.NET 10.0 ASP.NET Core Runtime
   Hosting Bundle」というキーワードでサイト内検索するのが確実）。
2. ダウンロードしたインストーラを管理者権限で実行。ウィザードに従いインストール。
3. インストール完了後、**IISを再起動して認識させる**必要がある。管理者権限のコマンドプロンプトで:
   ```powershell
   net stop was /y
   net start w3svc
   ```
4. 確認: コマンドプロンプトで `dotnet --info` を実行し、ASP.NET Core共有フレームワークとして
   `Microsoft.AspNetCore.App 10.0.x` が一覧に出ることを確認する。

---

## 13. .NET SDK のインストール（ビルドをこのVM上で行う場合のみ）

`dotnet publish` をホストPC側で実行し、発行済みファイル一式をVMへコピーする運用であれば、VM側には
12章のHosting Bundle（ランタイムのみ）で十分です。もしVM上で直接ビルドしたい場合は、
`https://dotnet.microsoft.com/` から **.NET 10.0 SDK** のインストーラも別途入れてください
（本書は「ホストPCでビルドしてVMへ発行物をコピーする」方式を前提に進めます。17章参照）。

---

## 14. URL Rewrite と Application Request Routing (ARR) のインストール

Backend API・Timeline(静的ファイル)はASP.NET Core Module経由・直接配信でIISが直接扱えますが、
**Worklist/Viewer(Nuxt)はNode.jsのプロセス**であり、IISネイティブでは処理できません。
IISを「入り口」にしつつ、実際の処理はNode.jsプロセス（ポート3100/3200）に転送する
**リバースプロキシ構成**にするため、Microsoft公式のIIS拡張機能を2つ追加します。

1. VM内ブラウザで `https://www.iis.net/downloads/microsoft/url-rewrite` を開き、
   **URL Rewrite Module 2.1** をダウンロード・インストール。
2. `https://www.iis.net/downloads/microsoft/application-request-routing` を開き、
   **Application Request Routing (ARR) 3.0** をダウンロード・インストール。
3. IISマネージャーを一度閉じて開き直す（拡張機能を認識させるため）。
4. IISマネージャーでサーバー名（ツリーの最上位）をクリック →中央ペインに「Application Request
   Routing Cache」アイコンが表示されていればインストール成功。
5. サーバー名を選択した状態で右側の「操作」ペインから「Server Proxy Settings」を開き、
   「Enable proxy」にチェックを入れて「適用」（既定でオフになっているため、明示的に有効化しないと
   リバースプロキシとして機能しない）。

---

## 15. Node.js のインストール

1. `https://nodejs.org/` からWindows用インストーラ（**LTS版**を推奨。このリポジトリの
   `frontend/worklist`・`frontend/viewer`はNode 20系以上で動作確認済み）をダウンロードしVMへコピー
   （13章と同様の方法でホストからコピー、またはVM内ブラウザで直接ダウンロード）。
2. インストーラを実行。「Automatically install the necessary tools」のチェックは外してよい
   （C++ビルドツール一式が入るため時間がかかる。今回のNuxtアプリのビルドには通常不要）。
3. インストール完了後、コマンドプロンプトで `node --version` / `npm --version` が表示されることを確認。

---

## 16. NSSM のインストール（コンソールアプリをWindowsサービス化する）

DICOM SCP・Temporal Worker・Temporal Server・Node.js(Worklist/Viewer)は、いずれも
「ログインしてコンソールで`dotnet run`/`node ...`を実行し続ける」タイプのプロセスです。これを
サーバーとして運用するには、**サインアウトしても動き続ける「Windowsサービス」として登録する**
必要があります。

**重要な技術的注意**: `sc.exe create` コマンドで直接Windowsサービスを作ろうとしても、
普通のコンソールアプリ（`dotnet.exe`や`node.exe`）は**そのままではサービスとして正常に動作しません**。
Windowsサービスは「サービス制御マネージャー(SCM)」との専用の通信プロトコルに対応している必要が
あり、普通のコンソールアプリはこれに対応していないため、`sc create`で登録して起動しても
「タイムアウトエラー」で失敗します（`.NET`アプリ側で`UseWindowsService()`を組み込むコード改修を
すれば直接サービス化できますが、今回はコードを変更せずに済む方法を採ります）。

そこで **NSSM (Non-Sucking Service Manager)** という無料・定番のツールを使います。これは
「任意のコンソールアプリを外側から監視し、Windowsサービスの皮を被せる」ラッパーで、コード変更なしに
サービス化できます。

1. `https://nssm.cc/` を開き、`download` から最新版のzipをダウンロード。
2. zipを展開し、`win64/nssm.exe` を分かりやすい場所（例: `C:\Tools\nssm.exe`）に配置する。
3. 以降の章（19〜21章）で、この`nssm.exe`を使って各プロセスをサービス化する。

---

## 17. dicom-tool-3 のソースをVMへ配置する

**方針**: ホストPC側で各サービスを `dotnet publish` / `npm run build` してから、成果物一式のみを
VMへコピーします（VM上にはソースコード全体やビルドツール一式を置かない、実務のデプロイに近い運用）。

### 17-1. Backend API を発行する（ホストPC側で実行）

```powershell
cd D:\Programming\lerning\dicom-tool-3\backend\DicomTool.Api
dotnet publish -c Release -o .\publish
```

`backend\DicomTool.Api\publish` フォルダが生成される。

### 17-2. DICOM SCP / Temporal Worker も同様に発行する

```powershell
cd D:\Programming\lerning\dicom-tool-3\services\DicomTool.DicomScp
dotnet publish -c Release -o .\publish

cd D:\Programming\lerning\dicom-tool-3\services\DicomTool.Worker
dotnet publish -c Release -o .\publish
```

### 17-3. Timeline (Blazor) をビルドする

```powershell
cd D:\Programming\lerning\dicom-tool-3\frontend\timeline\DicomTool.Timeline
dotnet publish -c Release -o .\publish
```

Blazor WebAssemblyの場合、`publish\wwwroot` 配下に静的ファイル一式（html/js/wasm）が生成される。
IISでは**この`wwwroot`フォルダの中身**をそのまま配信する。

### 17-4. Worklist / Viewer (Nuxt) をビルドする

```powershell
cd D:\Programming\lerning\dicom-tool-3\frontend\worklist
npm install
npm run build

cd D:\Programming\lerning\dicom-tool-3\frontend\viewer
npm install
npm run build
```

Nuxt3の既定ビルド（Node サーバーとして動く形式）では `.output` フォルダが生成される
（`.output/server/index.mjs` が起動エントリポイント）。

### 17-5. VMへコピーする

VM内に配置先フォルダを作る（例）:
```
C:\apps\DicomTool.Api\
C:\apps\DicomTool.DicomScp\
C:\apps\DicomTool.Worker\
C:\apps\Timeline\               (← publish\wwwrootの中身をここへ)
C:\apps\Worklist\               (← .outputフォルダの中身をここへ)
C:\apps\Viewer\                 (← .outputフォルダの中身をここへ)
```

VMware Toolsのドラッグ&ドロップ、または共有フォルダ機能（VM設定 →オプション→共有フォルダ）を使って、
上記17-1〜17-4で生成された各フォルダの中身をそのままコピーする。

---

## 18. Backend API を IIS へデプロイする

1. IISマネージャーを開く。左ツリーの「サイト」を右クリック →「Web サイトの追加」。
2. サイト名: `DicomTool.Api`。物理パス: `C:\apps\DicomTool.Api`。バインドの種類は `http`、
   ポートは `5030`（`docs/CONTRACT.md`のポート一覧と一致させる）、IPアドレスは「すべて未割り当て」。
3. アプリケーションプールが自動作成される（`DicomTool.Api`という名前）。これを選択して
   「基本設定」を確認し、「.NET CLR バージョン」が **「マネージド コードなし (No Managed Code)」**
   になっていることを確認する（ASP.NET Core はIIS標準の.NET CLRホスティングを使わず、
   ANCM経由で独自にKestrelを起動するため。これが12章のHosting Bundleが必要な理由）。
4. **接続文字列の上書き**: `C:\apps\DicomTool.Api\appsettings.Production.json` を新規作成
   （無ければ）し、以下のように10章で作成したDBユーザー・パスワードを設定する:
   ```json
   {
     "ConnectionStrings": {
       "Dicom": "Host=localhost;Port=5432;Database=dicomtool;Username=dicomtool;Password=<10章で設定したパスワード>"
     }
   }
   ```
   （IIS上で動くAPIから見るとPostgreSQLは同じVM内の`localhost`）。
5. IIS上でASP.NET Coreアプリの実行環境(`ASPNETCORE_ENVIRONMENT`)を`Production`にするには、
   IISマネージャーで対象サイトを選び「構成エディター」からweb.config相当の環境変数を設定するか、
   `C:\apps\DicomTool.Api\web.config`（`dotnet publish`で自動生成されている）を開き、
   `<aspNetCore>`タグ内に以下を追記する:
   ```xml
   <environmentVariables>
     <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
   </environmentVariables>
   ```
6. ブラウザで `http://192.168.93.128:5030/graphql` を開き、GraphQL用のIDE(Nitro)が表示されれば成功。
   初回アクセス時、Program.cs内の起動処理でPostgreSQLへのマイグレーション適用が走る
   （`docs/CONTRACT.md` 7章の通り、マイグレーション適用はこのApiの役割）。

---

## 19. DICOM SCP を Windows サービス化する

DICOM SCPはIISではなくNSSM経由の常駐プロセスとして動かします（理由: DIMSE(生TCP)通信は
IISのHTTPパイプラインの外側の話であり、IISのアプリケーションプールのリサイクル(既定で
一定時間ごとに自動再起動する)に巻き込まれると通信中の接続が切れてしまうため）。

1. 管理者権限のコマンドプロンプトで:
   ```powershell
   C:\Tools\nssm.exe install DicomToolScp
   ```
2. GUIダイアログが開く。
   - Path: `C:\apps\DicomTool.DicomScp\DicomTool.DicomScp.exe`
   - Startup directory: `C:\apps\DicomTool.DicomScp`
   - 「Details」タブでDisplay nameを分かりやすく設定（例: `DicomTool DICOM SCP`）。
   - 「Environment」タブで `ASPNETCORE_ENVIRONMENT=Production` を追加。
   - 「I/O」タブでstdout/stderrのログ出力先ファイルを指定しておくと、後でトラブルシュートしやすい
     （例: `C:\apps\DicomTool.DicomScp\logs\stdout.log` / `stderr.log`、事前にlogsフォルダを作成）。
3. 「Install service」をクリック。
4. `services.msc` を開き、「DicomToolScp」を探して右クリック →「開始」。
5. `netstat -ano | findstr 11112` を実行し、`LISTENING`状態でこのポートが開いていることを確認。

---

## 20. Temporal Worker を Windows サービス化する

19章と同じ手順で、対象を `DicomTool.Worker.exe` に変えて登録する。

```powershell
C:\Tools\nssm.exe install DicomToolWorker
```
- Path: `C:\apps\DicomTool.Worker\DicomTool.Worker.exe`
- Startup directory: `C:\apps\DicomTool.Worker`
- Environment: `ASPNETCORE_ENVIRONMENT=Production`
- appsettings側の接続文字列も18章4と同様に`appsettings.Production.json`で上書きしておく
  （PostgreSQLの接続情報。Temporal Serverの接続先は次章でサービス化した後、`localhost:7233`のままでよい）。

**注意**: `services/DicomTool.Worker`は`docs/CONTRACT.md`7章の規約により`Database.Migrate()`を
呼ばない設計です。**必ず18章のBackend APIを先に一度起動し、マイグレーションが適用された後**に
このWorkerサービスを起動してください（テーブルが無い状態でWorkerだけ起動すると、
ワークフロー実行時にSQLエラーになります）。

---

## 21. Temporal Server を導入・サービス化する

提案書4-5章の通り、セルフホストかつ無料・軽量な **`temporal server start-dev`**（Temporal公式CLIに
同梱された、SQLite内蔵の単一バイナリサーバー）を使います。ローカルPCでの検証では
`docker-compose.yml`経由でTemporal Server本体+PostgreSQLバックエンドの構成を使いましたが、
このVMでは10章で入れたPostgreSQLと同居させる複雑な構成を避け、シンプルな`start-dev`モードにします
（学習目的の負荷であれば十分。提案書4-5章にも同様の記載あり）。

1. Temporal CLIの公式GitHubリリースページ (`https://github.com/temporalio/cli/releases`) を開き、
   最新版の Windows用 zip（`temporal_cli_<version>_windows_amd64.zip`のような名前）をダウンロードし
   VMへ配置。
2. 展開して `temporal.exe` を `C:\apps\TemporalCli\temporal.exe` のような場所に置く。
3. データの永続化先を指定して起動できるようにするため、NSSMでサービス化する:
   ```powershell
   C:\Tools\nssm.exe install TemporalServer
   ```
   - Path: `C:\apps\TemporalCli\temporal.exe`
   - Arguments:
     ```
     server start-dev --ip 0.0.0.0 --port 7233 --http-port 7243 --ui-port 8233 --db-filename C:\apps\TemporalCli\temporal.db
     ```
     （`--ip 0.0.0.0`が重要。指定しないと既定で`localhost`のみ待受になり、他プロセスや
     ホストPCから見えなくなる。`--db-filename`を指定することで、サービス再起動後もワークフロー
     履歴のデータが消えずに残る）。
   - Startup directory: `C:\apps\TemporalCli`
4. 「Install service」→ `services.msc`から起動。
5. `netstat -ano | findstr 7233` でLISTENING確認。
6. VM内ブラウザで `http://localhost:8233` を開き、Temporal Web UIが表示されれば成功。

---

## 22. Worklist / Viewer (Nuxt) を Windows サービス化 + IISリバースプロキシ

### 22-1. Node.jsプロセスをNSSMでサービス化する

Worklist（17-5でコピーした`.output`一式が`C:\apps\Worklist`にある想定）:
```powershell
C:\Tools\nssm.exe install DicomToolWorklist
```
- Path: `node.exe`のフルパス（`where node`で確認できる。例: `C:\Program Files\nodejs\node.exe`）
- Arguments: `C:\apps\Worklist\server\index.mjs`
- Startup directory: `C:\apps\Worklist`
- Environment に以下を追加（Nuxtの`runtimeConfig`をポート・接続先に合わせて上書き。
  `frontend/worklist/nuxt.config.ts`の環境変数名と一致させる）:
  ```
  NITRO_PORT=3100
  NUXT_PUBLIC_GRAPHQL_ENDPOINT=http://192.168.93.128:5030/graphql
  ```

Viewerも同様に `DicomToolViewer` という名前で、ポートを`3200`にして登録する
（`NITRO_PORT=3200`、`NUXT_PUBLIC_GRAPHQL_ENDPOINT`は同じAPIを指す）。

両方とも`services.msc`から起動し、`netstat -ano | findstr "3100 3200"`でLISTENING確認する。

### 22-2. IIS側にリバースプロキシ用サイトを作る（Worklistの例）

Node.jsプロセス自体は`3100`番ポートで直接動いていますが、これを`80`番ポート（既定のWebポート）
経由で見せたい場合や、複数のNuxtアプリを別々のホスト名（例: `worklist.local`）で振り分けたい場合に
IISのARRリバースプロキシを使います（学習目的で`3100`に直接アクセスするだけで十分な場合はこの節は
スキップしてよい）。

1. IISマネージャーで新規サイト「WorklistProxy」を作成。物理パスは空の適当なフォルダでよい
   （リバースプロキシ専用でコンテンツを配置しないため）。ポート`80`、ホスト名`worklist.local`
   （もしくは別ポート `3180` 等でホスト名を使わない運用でもよい）。
2. サイトのルートに`web.config`を新規作成し、以下のように書く:
   ```xml
   <?xml version="1.0" encoding="UTF-8"?>
   <configuration>
     <system.webServer>
       <rewrite>
         <rules>
           <rule name="ReverseProxyToNode" stopProcessing="true">
             <match url="(.*)" />
             <action type="Rewrite" url="http://localhost:3100/{R:1}" />
           </rule>
         </rules>
       </rewrite>
     </system.webServer>
   </configuration>
   ```
3. ホストPC側の`hosts`ファイル（`C:\Windows\System32\drivers\etc\hosts`、管理者権限で編集）に
   `192.168.93.128 worklist.local` を追記すれば、ホストPCのブラウザから `http://worklist.local`
   でアクセスできるようになる。

---

## 23. Timeline (Blazor) を IIS で静的配信する

Blazor WebAssemblyは事前ビルド済みの静的ファイル(html/js/wasm)なので、Node.jsのような常駐プロセスは
不要です。IISに直接サイトを作るだけで動きます。

1. 17-3でコピーした `C:\apps\Timeline`（`publish\wwwroot`の中身）を確認。
2. IISマネージャーで新規サイト「Timeline」を作成。物理パス `C:\apps\Timeline`、ポート `5230`。
3. Blazor WebAssemblyは `.wasm`, `.dll`, `.json` 等の拡張子をIISが正しいMIMEタイプで配信する必要が
   あるため、`web.config`（`dotnet publish`で自動生成されているはず）がそのまま配置されていることを
   確認する。無い場合は以下を`C:\apps\Timeline\web.config`として作成:
   ```xml
   <?xml version="1.0" encoding="UTF-8"?>
   <configuration>
     <system.webServer>
       <staticContent>
         <remove fileExtension=".dll" />
         <mimeMap fileExtension=".dll" mimeType="application/octet-stream" />
         <remove fileExtension=".wasm" />
         <mimeMap fileExtension=".wasm" mimeType="application/wasm" />
       </staticContent>
       <rewrite>
         <rules>
           <rule name="Blazor SPA fallback" stopProcessing="true">
             <match url=".*" />
             <conditions logicalGrouping="MatchAll">
               <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
               <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
             </conditions>
             <action type="Rewrite" url="index.html" />
           </rule>
         </rules>
       </rewrite>
     </system.webServer>
   </configuration>
   ```
   （Blazorはクライアントサイドルーティング(SPA)のため、`/timeline/xxx`のようなURLに直接アクセス
   された場合でも常に`index.html`を返してJS側でルーティングさせる必要がある。上記の
   「SPA fallback」ルールがこれを行う）。
4. ブラウザで `http://192.168.93.128:5230` を開き、Timelineアプリが表示されれば成功。

---

## 24. ファイアウォール受信規則まとめ

管理者権限のPowerShellで以下をまとめて実行できます（`docs/CONTRACT.md`のポート一覧と対応）。

```powershell
New-NetFirewallRule -DisplayName "DicomTool API (5030)" -Direction Inbound -Protocol TCP -LocalPort 5030 -Action Allow
New-NetFirewallRule -DisplayName "DicomTool Worklist (3100)" -Direction Inbound -Protocol TCP -LocalPort 3100 -Action Allow
New-NetFirewallRule -DisplayName "DicomTool Viewer (3200)" -Direction Inbound -Protocol TCP -LocalPort 3200 -Action Allow
New-NetFirewallRule -DisplayName "DicomTool Timeline (5230)" -Direction Inbound -Protocol TCP -LocalPort 5230 -Action Allow
New-NetFirewallRule -DisplayName "DicomTool DICOM SCP DIMSE (11112)" -Direction Inbound -Protocol TCP -LocalPort 11112 -Action Allow
New-NetFirewallRule -DisplayName "DicomTool DICOM SCP Mgmt (8090)" -Direction Inbound -Protocol TCP -LocalPort 8090 -Action Allow
New-NetFirewallRule -DisplayName "Temporal Frontend (7233)" -Direction Inbound -Protocol TCP -LocalPort 7233 -Action Allow
New-NetFirewallRule -DisplayName "Temporal Web UI (8233)" -Direction Inbound -Protocol TCP -LocalPort 8233 -Action Allow
New-NetFirewallRule -DisplayName "PostgreSQL (5432)" -Direction Inbound -Protocol TCP -LocalPort 5432 -Action Allow
```

> セキュリティ上の注意: これらは学習目的でVMware NAT内（＝実質このホストPC経由のみ到達可能）を
> 想定した緩めの設定です。将来的に社内LAN等、より広いネットワークに公開する場合は、送信元IPを
> 絞る（`-RemoteAddress`オプション）等、追加の制限を検討してください。

---

## 25. 動作確認チェックリスト

ホストPCのブラウザ・コマンドプロンプトから、上から順に確認してください
（`192.168.93.128`は自分の環境の固定IPに読み替え）。

1. `ping 192.168.93.128` が通る。
2. `http://192.168.93.128:5030/graphql` を開くとNitro(GraphQL IDE)が表示される。
3. `http://192.168.93.128:3100` でWorklistが表示される。
4. `http://192.168.93.128:3200` でViewerが表示される。
5. `http://192.168.93.128:5230` でTimelineが表示される。
6. `http://192.168.93.128:8090/swagger` でDICOM SCP管理APIのSwagger UIが表示される。
7. 上記6のSwagger UIから `POST /test/c-echo` を実行し、成功レスポンスが返る
   （SCPがDIMSEポート11112で正しく待受していることの確認）。
8. `http://192.168.93.128:8233` でTemporal Web UIが表示され、7で実行したテストに紐づく
   ワークフロー実行（あるいは6の`/test/c-store`実行後）が一覧に見える。
9. Worklistからファイルをアップロードし、数秒後に一覧に反映される
   （非同期処理の遅延は正常。`docs/architecture.md` 2章参照）。

すべて成功すれば、VM上での実務相当環境の構築は完了です。

---

## 26. 常駐トレイアプリについて（重要な再掲）

`services/DicomTool.TrayApp` は **このVMにはインストールしません**。実際に読影医が使う
Windows PC（＝Worklistをブラウザで開くのと同じPC）に個別にインストールするアプリです。
動作確認するには、ホストPC（またはWorklistを開く別のWindows PC）で以下を実行してください:

```powershell
$env:RemoteHost = "192.168.93.128"
cd D:\Programming\lerning\dicom-tool-3\services\DicomTool.TrayApp
dotnet run
```

**`RemoteHost`環境変数が重要です。** TrayAppはWorklist/TimelineがすべてlocalhostにあるDocker
Compose環境を前提にコードが書かれており、既定値（`RemoteHost`未指定時）は`localhost`になって
います。VM環境ではWorklist・TimelineともにこのVMのIP(`192.168.93.128`)上にあるため、
`RemoteHost`を指定しないと以下の2つの不具合が起きます:

- WorklistのオリジンがCORS許可リスト(`http://localhost:3100`)と一致せず、
  タイムライン起動ボタンを押しても「常駐アプリが起動していません」という
  誤ったエラーになる（実際にはTrayAppは起動している）。
- 仮にCORSを通っても、TrayAppが開こうとするTimelineのURLが`http://localhost:5230/...`の
  ままになり、ホストPC自身の5230番ポート（何も存在しない）を開こうとしてしまう。

`$env:RemoteHost = "192.168.93.128"` を設定してから`dotnet run`することで、この2つが
`192.168.93.128`基準に切り替わり、正しく動作します。

Worklist(`http://192.168.93.128:3100`)からタイムライン起動を試す際は、
このトレイアプリが**Worklistを見ているのと同じPC上で**起動している必要があります
（トレイアプリのHTTP API `localhost:5299` はそのPC自身に対してのみ待受しているため）。

---

## 27. トラブルシューティング

**IISで502.5 - Process Failure エラーが出る**
→ ASP.NET Core Hosting Bundle未導入、またはIIS再起動忘れ（12章3参照）。イベントビューアー
（Windows ログ > Application）にKestrelプロセスの起動失敗理由が出ていることが多い。

**`net start w3svc` 等の後もアプリが403/404になる**
→ アプリケーションプールの「.NET CLR バージョン」が「マネージド コードなし」になっているか
再確認（18章3）。

**NSSMでサービス登録したのに数秒で停止する**
→ `services.msc`でそのサービスのプロパティ→「回復」タブでログを追わずとも、NSSMの
「I/O」タブで指定したstdout/stderrログファイル（19章2）を確認すると、アプリ側の例外メッセージが
そのまま出ていることが多い。大抵は接続文字列やappsettings.Production.jsonの記述ミス。

**Worklist/Viewerが起動してもGraphQL通信がCORSエラーになる**
→ `backend/DicomTool.Api/appsettings.json`の`Cors:AllowedOrigins`にVM上のURL
（`http://192.168.93.128:3100`等）が含まれているか確認。ローカル開発用の`localhost`ベースの値の
ままだと、VM上のIPアドレスでアクセスした際にCORSで弾かれる。`appsettings.Production.json`で
上書きすること。

**DICOM SCPへの外部からのC-STOREが失敗する（タイムアウトする）**
→ 24章のファイアウォール規則(`11112`)が正しく適用されているか確認。また、送信元
（他のPACSやモダリティ機器）からこのVMのIP(`192.168.93.128`)への経路がVMware NATの外側にある場合、
NAT設定でポートフォワーディングの追加設定が必要になることがある
（仮想ネットワークエディター → VMnet8 → NAT設定 → ポートフォワーディング編集）。
