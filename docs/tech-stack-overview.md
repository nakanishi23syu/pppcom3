# 使用技術かんたん解説

> このプロジェクト(dicom-tool-3)を動かすために使われている主要な技術を、それぞれ
> 「何なのか」「なぜこのプロジェクトで使っているのか」「たとえるなら何か」の3点で
> ざっくり解説します。専門知識は前提にしていません。もっと詳しく知りたくなったら、
> 各技術の項目に書いてある関連ドキュメント・ファイルを覗いてみてください。
>
> ポート番号や全体構成は [`docs/CONTRACT.md`](./CONTRACT.md)・
> [`docs/architecture.md`](./architecture.md)・
> [`docs/infrastructure-diagram.md`](./infrastructure-diagram.md) が正です。
> このドキュメントは「技術そのものの説明」に専念し、値の重複は避けています。

---

## バックエンド(サーバー側)の技術

### ASP.NET Core（Webサーバーの土台）

C#でWebサーバーやAPIを作るための、Microsoft製の土台（フレームワーク）です。
「HTTPリクエストを受け取って処理し、レスポンスを返す」という、Webサーバーなら必ず必要になる
基礎工事の部分を全部やってくれます。このプロジェクトでは`backend/DicomTool.Api`
（GraphQL API）、`services/DicomTool.DicomScp`（DICOM SCPの管理用REST API）、
`services/DicomTool.TrayApp`（トレイアプリ内のローカルAPI）が、いずれもこのASP.NET Core上に
構築されています。たとえるなら、家を建てるときの「基礎・柱・配管」に相当する、家(アプリ)の
中身より先に必要な共通インフラです。

### GraphQL / HotChocolate（backendのAPI方式）

GraphQLは「フロントエンドが欲しいデータの形を自分で指定して問い合わせる」タイプのAPI方式です
（対する伝統的なREST APIは、サーバー側があらかじめ決めた形のデータをエンドポイントごとに返す
方式）。HotChocolateは、C#/.NETでGraphQLサーバーを作るためのライブラリで、
`backend/DicomTool.Api`のAPI方式として採用しています。たとえるなら、レストランで
「あらかじめ決まったコースメニューから選ぶ」のがREST、「食べたい食材だけを注文書に書いて
一度に出してもらう」のがGraphQL、というイメージです。Worklist/Viewer/Timelineの3つの
フロントエンドがそれぞれ必要なデータだけを効率よく取得できます。

### Entity Framework Core（DBとC#コードの橋渡し=ORM）

Entity Framework Core（EF Core）は、C#のクラス（オブジェクト）とデータベースのテーブルを
自動的に対応づけてくれる「ORM（Object-Relational Mapper）」というツールです。SQL文を
手書きしなくても、C#のコードを書くだけでデータベースの読み書きができるようになります。
`shared/DicomTool.Shared`にエンティティ（`UserStudy`・`UserSeries`・`UserSop`等）と
`DicomDbContext`が定義されており、`backend/DicomTool.Api`と`services/DicomTool.Worker`の
両方がこれを参照することで、同じテーブル定義を二重管理する事故を防いでいます。たとえるなら、
日本語（C#のオブジェクト）と英語（SQL/データベース）の間に立つ「同時通訳者」のような存在です。

### PostgreSQL（DB本体）

実際にデータを保存しておく、オープンソースのデータベース製品本体です。このプロジェクトでは
検査・シリーズ・画像のレコード（Study/Series/Sop）や、Temporalワークフローの実行履歴が
このPostgreSQLに保存されます。たとえるなら、EF Coreが「通訳者」なら、PostgreSQLは実際に
データが保管されている「倉庫」そのものです。

### Temporal（ワークフローエンジン、非同期処理の管理）

Temporalは、「複数ステップからなる処理を、失敗しても正しく再開できるように実行し続ける」
専門のエンジンです。このプロジェクトでは「画像ファイルをストレージへ保存する」→
「データベースへ登録する」という2ステップの処理を、途中でプロセスが落ちても中途半端な状態が
残らないようTemporalに任せています。詳しくは
[`docs/temporal-workflow-guide.md`](./temporal-workflow-guide.md)を参照してください。
たとえるなら、宅配便の「配送状況を追跡し、失敗した配送は自動的に再配達を手配してくれる」
配送管理システムのようなものです。

### fo-dicom（.NETでDICOM画像を読み書きするライブラリ）

fo-dicomは、.NET(C#)でDICOM形式のファイルやDICOM通信（C-ECHO/C-STORE等）を扱うための、
定番のオープンソースライブラリです。DICOM独自の通信プロトコル（アソシエーションの確立、
PDUの組み立て・解析など）を自前で実装するのは非常に大変ですが、fo-dicomがその複雑な部分を
一手に引き受けてくれます。`services/DicomTool.DicomScp`と`services/DicomTool.Worker`が
これを利用しています。たとえるなら、外国語の読み書き・会話を全部代行してくれる
「専門の翻訳者兼交渉人」のような存在です。DICOM周りのたとえ話は
[`docs/dicom-protocol-for-beginners.md`](./dicom-protocol-for-beginners.md)にまとめています。

---

## フロントエンド(ブラウザ側)の技術

### Nuxt 3 / Vue 3（Worklist・Viewerのフロントエンド）

Vue 3は、ブラウザ上で動く「見た目のあるアプリ（UI）」を作るためのJavaScriptフレームワークです。
Nuxt 3は、そのVueを土台にして「ページ遷移・ビルド設定・サーバー機能」などをあらかじめ
使いやすく組み立ててくれる、いわば「Vueの発展パッケージ」です。このプロジェクトでは
検査一覧・アップロード画面のWorklist(`frontend/worklist`)と、画像表示のViewer
(`frontend/viewer`)がNuxt 3で作られています。たとえるなら、Vueが「レゴブロックの基本パーツ」
だとすると、Nuxtは「そのパーツを組み合わせやすいように整理された、あらかじめ土台の付いた
セット」のようなものです。

### Blazor WebAssembly（Timelineのフロントエンド、C#がブラウザで動く仕組み）

Blazor WebAssemblyは、通常JavaScriptで書くブラウザ上のアプリを、C#で書けるようにする
Microsoftの技術です。C#のコードがWebAssembly(WASM)という形式にコンパイルされ、ブラウザの中で
直接実行されます。患者タイムライン表示のTimeline(`frontend/timeline`)がこれで作られています。
たとえるなら、本来英語（JavaScript）しか通じないはずの場所（ブラウザ）で、あらかじめ用意した
自動翻訳機（WebAssembly）を使うことで、母国語（C#）のまま会話できるようにする仕組みです。
バックエンドと同じC#言語で書けるため、EF Coreのエンティティ定義などを共有しやすい……はずですが、
Blazor WebAssemblyはブラウザのサンドボックス内で動く制約上、DB直結ライブラリはそのまま
持ち込めないため、このプロジェクトではポート番号等の定数だけ手動で複製しています
（`docs/architecture.md` 4章参照）。

---

## インフラ・運用の技術

### IIS（Windows上のWebサーバー、リバースプロキシ）

IIS（Internet Information Services）は、Windows Serverに標準搭載されているWebサーバー機能です。
このプロジェクトのVM環境では、Backend API・Timelineの配信窓口として使われるほか、Node.js
（Worklist/Viewer）へのアクセスを取り次ぐ「リバースプロキシ」としても使われます（
[`VM構築手順.md`](./VM構築手順.md) 11〜14章参照）。たとえるなら、ビル(VM)の入り口にいる
「受付係」で、来客(HTTPリクエスト)を見て「この人はこちらのオフィス(IISが直接処理するASP.NET
Coreアプリ)へ」「あちらの人はこちらのオフィス(Node.jsプロセス)へ転送します」と案内する役目です。

### NSSM（普通のexeをWindowsサービス化するツール）

NSSM（Non-Sucking Service Manager）は、普通のコンソールアプリ（`dotnet.exe`や`node.exe`の
プロセス）を、コード変更なしにWindowsの「サービス」（サインアウトしても動き続けるバックグラウンド
プロセス）に変えてくれる無料ツールです。DICOM SCP・Temporal Worker・Temporal Server・
Worklist/ViewerのNode.jsプロセスなど、常時起動しておきたいプロセスをこれでサービス化しています
（[`VM構築手順.md`](./VM構築手順.md) 16章参照）。たとえるなら、普段は誰かが手動で
「よーい、スタート！」しないと動かない機械に、自動で電源が入り続ける「常時稼働スイッチ」を
外から取り付けるようなものです。

### SSH / OpenSSH（リモートPC操作）

SSH（Secure Shell）は、離れた場所にあるコンピュータへ暗号化された安全な通信路ごしに接続し、
コマンドを実行したりファイルをやり取りしたりするための仕組みです。OpenSSHはその代表的な
無料実装です。このプロジェクトのVM運用では、後述のWinSCPによるファイル転送がSSHが使う
暗号化技術（SFTP/SCPプロトコル）の上に成り立っています。また、Windows ServerにOpenSSH
サーバー機能を有効化すれば、リモートデスクトップ(RDP)を使わずコマンドラインだけでVMを
操作することもできます。たとえるなら、玄関(画面)を開けずに、鍵のかかった専用の通話回線
（暗号化通信）越しに家の中の作業を代行してもらうようなものです。

### Playwright（ブラウザ自動操作テストツール）

Playwrightは、プログラムからブラウザを自動操作し、画面が正しく表示されているか・
ボタンを押すと期待通りに動くかを自動でテストするためのツールです。このプロジェクトの開発中、
`frontend/timeline`（Blazor WebAssembly）が実際のブラウザで正しくレンダリングされるかどうかの
確認にPlaywrightが使われました（[`docs/architecture.md`](./architecture.md) 5章の実装状況
サマリー参照）。たとえるなら、人間の代わりに実際に画面をクリックして「ちゃんと動くか」を
確認してくれる、疲れ知らずの検品ロボットです。

### WinSCP（ファイル転送ツール）

WinSCPは、Windows上で動くファイル転送クライアントで、SFTP/SCP（前述のSSHの仲間の
プロトコル）を使ってリモートのサーバー（このプロジェクトではVM）へファイルを安全に
アップロードできます。リポジトリ直下の`deploy.bat`が、WinSCPのコマンドライン版
(`WinSCP.com`)を呼び出し、あらかじめ用意したスクリプト(`deploy.txt`、VMのログイン情報を
含むため`.gitignore`対象)に従ってビルド成果物一式をVMへ転送する仕組みになっています。
たとえるなら、荷物（ビルド済みのアプリ一式）を鍵のかかったトラック（暗号化通信）に積んで、
決まった配送手順書（スクリプト）通りに届け先（VM）まで運んでくれる、専属の配送業者です。
