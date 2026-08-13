# PostgreSQLクエリログ確認手順 ― アプリが実行したSQLをランタイムで見る

> SQL Serverの「SSMSクエリプロファイラー」に相当する、アプリ側（API/Worker等）が実際に
> PostgreSQLへ投げたSQL文を確認する2つの方法をまとめたものです。**何も設定していない
> まっさらなPostgreSQLサーバー**から、両方の方法を使えるようにするまでの手順を書いています。
>
> 会社のPC（pgAdmin4がインストールされ、対象のPostgreSQLサーバーに管理者としてログイン
> できるPC）で同じ環境を作ることを想定しているため、以下のコマンドは**そのPC上に直接
> ログインして（リモートデスクトップ等で）、その場で開いたPowerShell/pgAdmin4から実行する**
> 前提で書いています。SSH等のリモート接続コマンドは出てきません。
>
> この手順は`dicom-pacs-vm`（PostgreSQL 18）で実際に動作確認済みです（検証時のみ、
> このプロジェクト固有の事情でSSH経由で操作しましたが、その詳細は末尾の
> [付録](#付録-このリポジトリのvmでの検証記録参考)に分けてあります）。

---

## 0. 2つの方法の違い

| | 方法1: ログ出力 | 方法2: `pg_stat_statements`拡張機能 |
| --- | --- | --- |
| 記録の粒度 | 実行された**1回1回**を全部記録（実際の値つき） | クエリの**形**ごとに集計（値は`$1`,`$2`のようにプレースホルダ化） |
| 見る場所 | ログファイル（テキスト） | DB内のビュー（pgAdmin4でSQLを書くだけ） |
| 「いつ・何の値で」実行されたか | わかる（タイムスタンプ・実際の値つき） | わからない（累積の呼び出し回数だけ） |
| フィルタ・検索の仕方 | `Select-String`（PowerShellのテキスト検索） | `WHERE query ILIKE '%...%'`（SQL） |
| 得意なこと | 「今まさに何が走ったか」をリアルタイムに追う | 「どのクエリが一番遅い/多く呼ばれてるか」をランキングする |
| 有効化に必要な操作 | 設定ファイル編集＋reload（サービス無停止） | 設定ファイル編集＋サービス**完全再起動**＋DBに1回だけコマンド実行 |

どちらもPostgreSQL公式インストーラに標準で同梱されている機能で、**別途何かをダウンロード
する必要はありません**。両方併用して問題ありません。

---

## 1. 事前確認：バージョンとパスを確認する

インストールされているPostgreSQLのバージョンによってパス中の数字（`18`等）が変わるので、
最初に確認する。

pgAdmin4のクエリツールで対象サーバーに接続し、以下を実行:

```sql
SHOW config_file;
SHOW data_directory;
```

結果はだいたい以下の形（`<version>`部分がインストールしたバージョン番号）:

```
C:\Program Files\PostgreSQL\<version>\data\postgresql.conf
C:\Program Files\PostgreSQL\<version>\data
```

以降、このパスを`<PGDATA>`と表記する（例: `C:\Program Files\PostgreSQL\18\data`）。

---

## 2. 方法1: 全SQLをログファイルに出力する

### 2-1. 設定ファイルを編集する

対象PCに直接ログインし、管理者権限のメモ帳（`Program Files`配下は一般権限だと保存できない
ため必須）で以下を開く。

```
<PGDATA>\postgresql.conf
```

以下の行を探し、コメント（`#`）を外して値を書き換える（見つからなければファイル末尾に
追記でもよい。同じパラメータが複数回書かれている場合、**最後に書かれた値**が有効になる）。

```conf
log_statement = 'all'
log_min_duration_statement = 0
lc_messages = 'C'
```

- `log_statement = 'all'` … 実行された全SQL文をログに記録する（`none`/`ddl`/`mod`/`all`から選べる）
- `log_min_duration_statement = 0` … 実行時間つきで記録する（`0`=全件、`100`なら100ms以上のみ）
- `lc_messages = 'C'` … ログの見出し部分（後述の文字化け対策。**重要**、3章参照）

`logging_collector`はデフォルトで`on`になっていることが多いが、念のため以下もコメントを外し
`on`になっているか確認する。

```conf
logging_collector = on
```

保存する。

> **より確実な代替手段（ファイル編集がうまくいかない場合はこちらを推奨）**
>
> `Program Files`配下のファイル編集は、後述のトラブルシュート（5章）にある**Windowsの
> UAC仮想化**という罠にハマりやすく、「保存はできたのに実際には反映されていない」という
> 事象が起きがち。確実なのは、ファイルを直接編集せず**pgAdmin4のクエリツールからSQLで
> 設定する**方法。
>
> ```sql
> ALTER SYSTEM SET log_statement = 'all';
> ALTER SYSTEM SET log_min_duration_statement = 0;
> SELECT pg_reload_conf();
> ```
>
> `ALTER SYSTEM`はサーバーが確実に読む`postgresql.auto.conf`（`<PGDATA>`直下、
> `postgresql.conf`とは別ファイル）に書き込み、`pg_reload_conf()`でその場で反映される。
> メモ帳の管理者権限もファイルパスも意識する必要がなく、pgAdmin4にログインできてさえいれば
> 完結する。以降の2-2（`pg_ctl reload`）は不要になる。

### 2-2. 設定を反映させる（サービス停止不要）

`log_statement`等は**reloadだけで反映される**パラメータなので、サービスを止めずに済む。
対象PC上で管理者権限のPowerShellを開き、以下のどちらかを実行する。

```powershell
# 方法A: pg_ctl reload を直接叩く
& "C:\Program Files\PostgreSQL\<version>\bin\pg_ctl.exe" reload -D "C:\Program Files\PostgreSQL\<version>\data"

# 方法B: Windowsサービスとしてreloadと同等の操作(再起動)をする場合
Restart-Service postgresql-x64-<version>
```

方法Aはセッションを切らずに反映できるのでおすすめ。方法Bは接続中のセッションが切れる点に
注意（方法1だけならAで十分）。

**注意（重要）:** 設定を反映した**後**に実行されたSQLしか記録されない。過去に遡って見る
ことはできない。

### 2-3. 動作確認

対象PCから、実際にアプリを操作するか、APIに直接リクエストを送ってSQLを発生させる。

```powershell
# 例: GraphQL APIに実クエリを投げる場合
curl -X POST http://<APIのホスト>:<ポート>/graphql `
  -H "Content-Type: application/json" `
  -d '{"query":"{ studies { studyInstanceUid } }"}'
```

直後に、最新のログファイルを確認する。

```powershell
Get-Content -Path (Get-ChildItem "<PGDATA>\log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName) -Tail 20
```

以下のように実行されたSQL文が表示されれば成功。

```
LOG:  duration: 3.747 ms  execute <unnamed>: SELECT u."Id", u."AccessionNumber", ... FROM "user_study" AS u ...
```

### 2-4. リアルタイムに流れを追う（SSMSプロファイラーに一番近い体験）

Windowsには`tail -f`が無いので、PowerShellの`Get-Content -Wait`を使う。

```powershell
Get-Content -Path (Get-ChildItem "<PGDATA>\log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName) -Wait -Tail 20
```

このコマンドを実行したまま別ウィンドウでアプリを操作すると、その場でSQLが流れてくる。

### 2-5. 実行されたクエリに対してワイルドカード検索する

`Select-String`に正規表現パターンを渡す（`.*`がワイルドカード相当）。

```powershell
# 例: "user_study" を含む行だけ、大小文字無視で
Get-Content "<PGDATA>\log\postgresql-*.log" | Select-String -Pattern "user_study" -CaseSensitive:$false

# 例: リアルタイムで流しながら特定パターンだけ拾う
Get-Content -Path (Get-ChildItem "<PGDATA>\log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName) -Wait -Tail 0 | Select-String -Pattern "INSERT.*user_sop"
```

### 2-6. 無効化したい場合

2-1で追記した3行を削除（またはコメントアウト）し、再度2-2のreloadを実行する。

**常時onのままにする場合の注意:** 全SQLを記録し続けるとログファイルが際限なく増える。
`<PGDATA>\log`配下のディスク使用量を時々確認し、不要な過去ログは削除するか、
`postgresql.conf`の`log_rotation_age`/`log_rotation_size`で自動ローテーションを設定する。

---

## 3. 文字化け対策について（`lc_messages`）

日本語Windows環境のPostgreSQLは、ログの見出し部分（「期間」「実行」「ミリ秒」等）を
日本語に翻訳して出力する。しかしこの翻訳メッセージのバイト列と、ログファイル本体の
エンコーディング（UTF-8）が食い違うことがあり、`Get-Content`で読むと以下のように
見出し部分だけ文字化けすることがある（SQL文自体はほぼASCIIなので化けない）。

```
LOG:  期間: 1.202 ミリ私E パ、ース<unnamed> : SELECT ...
```

2-1で設定した`lc_messages = 'C'`は、ログの見出し部分を英語（ASCII）に固定することで
この問題を根本的に回避する設定。実際に設定・反映した後は以下のように綺麗に表示される
ことを確認済み。

```
LOG:  duration: 3.747 ms
LOG:  duration: 21.808 ms  parse <unnamed>: SELECT ...
LOG:  duration: 23.253 ms  bind <unnamed>: SELECT ...
LOG:  execute <unnamed>: SELECT ...
```

SQL文の検索・フィルタが目的であれば見出しが英語でも支障はない（むしろ`Select-String`で
"duration"や"execute"といった単語もそのまま検索しやすくなる）。

---

## 4. 方法2: `pg_stat_statements`拡張機能を使う

### 4-1. 「拡張機能」とは何か（誤解しやすいポイント）

PostgreSQLの「拡張機能」は、SQL Serverでの「追加ソフトをダウンロードして入れる」ような
ものではない。**PostgreSQL公式インストーラに最初から同梱されている機能**で、以下のように
既にファイルが存在している（未設定でも、インストール時点で配置済み）。

```
C:\Program Files\PostgreSQL\<version>\lib\pg_stat_statements.dll
C:\Program Files\PostgreSQL\<version>\share\extension\pg_stat_statements.control
```

「有効化」とは、この既にあるファイルを

1. サーバー起動時に読み込むよう設定ファイルに書く
2. 対象データベースの中に、それが提供するビューを作成する

という2つのスイッチを入れるだけの作業であり、新規ダウンロードは発生しない。

### 4-2. 設定ファイルを編集する

管理者権限のメモ帳で`<PGDATA>\postgresql.conf`を開き、以下の行を探してコメントを外し
値を設定する（無ければ末尾に追記）。

```conf
shared_preload_libraries = 'pg_stat_statements'
```

すでに他の値が入っている場合は`'pg_stat_statements,他の値'`とカンマ区切りで追記する。
保存する。

### 4-3. サービスを完全に再起動する（reloadでは効かない）

`shared_preload_libraries`は**サーバー起動時にしか読み込まれない**パラメータなので、
方法1と違いreloadでは反映されない。管理者権限のPowerShellで:

```powershell
Restart-Service postgresql-x64-<version>
```

（接続中のセッションは一瞬切れる。アプリ側は通常、次のクエリ発行時に自動再接続する。）

### 4-4. 対象データベースに拡張を作成する（1回だけ）

pgAdmin4のクエリツールで対象データベースに接続し、以下を実行する。

```sql
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
```

エラーなく終われば完了。これでビュー`pg_stat_statements`が使えるようになる。

### 4-5. 使い方

#### よく呼ばれる/遅いクエリのランキング

```sql
SELECT query, calls, total_exec_time, mean_exec_time, rows
FROM pg_stat_statements
ORDER BY total_exec_time DESC
LIMIT 20;
```

#### ワイルドカード検索（`ILIKE`で大小文字無視）

```sql
SELECT query, calls, mean_exec_time
FROM pg_stat_statements
WHERE query ILIKE '%user_study%'
ORDER BY calls DESC;
```

`%`が任意文字列、`_`が任意1文字のワイルドカード（SQLの`LIKE`構文）。pgAdmin4のクエリ
ツールにそのまま書くだけで完結する。

#### カウンタのリセット（計測をやり直したい時）

```sql
SELECT pg_stat_statements_reset();
```

### 4-6. 制約・注意点

- `pg_stat_statements.max`（既定5000）を超えると、古い・呼び出し頻度の低いクエリパターン
  から追い出される。
- サーバー再起動でカウンタは消える（揮発性。ログファイルのような永続記録ではない）。
- 値が`$1`のようにプレースホルダ化されるため、「あのタイミングでどの値のクエリが飛んだか」
  というデバッグには使えない。そこは方法1のログと併用するのがベスト。
- **「実行された時刻」を持つカラムが存在しないため、実行時刻順（最新順）でのソートはできない。**
  `pg_stat_statements`はクエリの形ごとに`calls`(呼び出し回数)や`total_exec_time`(累計実行時間)
  を集計する仕組みであり、1回ごとの実行ログではないため。（PostgreSQL 14以降にある
  `stats_since`は「そのクエリの集計を開始した時刻」であって「最後に実行された時刻」ではない
  ので混同しないこと。）「直近で何が実行されたか」を時系列で見たい場合は、この方法2ではなく
  方法1（ログ出力）を使う。どうしても方法2の枠組みで直近の変化だけを見たい場合は、
  `SELECT pg_stat_statements_reset();`で一旦カウンタをリセットしてから対象の操作を行い、
  `calls > 0`のものだけを見る、という擬似的なやり方もあるが、他のセッションの実行分も
  混ざってしまうため共用DB・本番DBでは実用的でない。
- `CREATE EXTENSION`はデータベースごとに必要（複数DBで使いたい場合はDBごとに実行）。
- `CREATE EXTENSION`の実行にはスーパーユーザー権限（またはそれに準ずる権限）が必要。
  アプリ用の一般ロールでは実行できないことが多いので、`postgres`等の管理者ロールで
  pgAdmin4にログインして実行する。

### 4-7. 無効化したい場合

```sql
DROP EXTENSION IF EXISTS pg_stat_statements;
```

を実行した上で、`postgresql.conf`の`shared_preload_libraries`の行を削除し、
再度サービスを完全再起動する。

---

## 5. トラブルシュート

### ログファイルはある・更新もされている・でもSQLが1行も出ない（方法1）

これは「設定ファイルはちゃんと編集したはずなのに、実際の動作には反映されていない」ケースで、
原因がいくつか考えられるため、**上から順に**切り分ける。

#### 手順1: サーバーが今実際に読んでいる設定値を確認する

編集したファイルの中身ではなく、**サーバープロセスが今メモリ上に持っている値**を見るのが
最優先。pgAdmin4のクエリツールで実行する。

```sql
SHOW log_statement;
```

これが`all`になっていなければ、ファイルを編集したつもりでも実際には反映されていない
（＝以下の手順2〜4のどれかが原因）。`all`になっているのにログに出ない場合は手順5へ進む。

#### 手順2: `postgresql.auto.conf`に古い設定が残っていないか確認する（見落としやすい）

過去に誰か（あるいは監視ツール等）が`ALTER SYSTEM`コマンドで設定した値があると、
`<PGDATA>\postgresql.auto.conf`という**別ファイル**に書き込まれ、こちらが
`postgresql.conf`より**優先**される。`postgresql.conf`を正しく編集しても、この
ファイルに古い`log_statement = 'none'`等が残っていると上書きされてしまう。

```sql
-- 現在有効なALTER SYSTEM由来の設定一覧を確認
SHOW log_statement;
SELECT name, setting, source FROM pg_settings WHERE name = 'log_statement';
```

`source`列が`configuration file`ではなく`override`や別の値になっている場合は、
2-1のコラムで紹介した`ALTER SYSTEM SET log_statement = 'all';` + `SELECT pg_reload_conf();`を
SQLで実行し直すのが一番確実（ファイルの直接編集より、この方法で上書きする方が早い）。

#### 手順3: Windowsの「UAC仮想化」でファイルが別の場所に保存されていないか確認する

メモ帳を**完全な管理者権限**（右クリック→「管理者として実行」）で開かずに
`postgresql.conf`を編集して保存すると、Windowsが気を利かせて（？）実際には
`Program Files`配下ではなく、以下のようなユーザーごとの「隠し置き場」に保存して
しまうことがある（本人は保存できたように見えるため非常に気づきにくい）。

```text
C:\Users\<ユーザー名>\AppData\Local\VirtualStore\Program Files\PostgreSQL\<version>\data\postgresql.conf
```

このパスにファイルが存在し、かつ中に自分が編集したはずの内容が入っている場合はこれが原因。
対処: そのファイルは削除し（本物と混同する元になるため）、今度こそメモ帳を
「管理者として実行」で起動してから`C:\Program Files\PostgreSQL\<version>\data\postgresql.conf`
を開き直して編集する。**確実性を優先するなら、そもそもファイル編集をやめて2-1コラムの
`ALTER SYSTEM`方式に切り替える方が早い**（この問題自体が起きなくなる）。

#### 手順4: 複数のPostgreSQLインスタンス／バージョンが入っていないか確認する

会社のPCでは、過去の検証等で複数バージョンのPostgreSQLが同居していることがある。
編集した`<PGDATA>`と、実際にサービスが使っている`<PGDATA>`が違うと、いくら編集しても
反映されない。

```sql
SHOW config_file;
SHOW data_directory;
SHOW port;
```

の結果が、1章で確認したパス・ポートと一致しているか再確認する。一致していなければ、
実際に動いているインスタンス側のファイルを編集し直す。

#### 手順5: `all`になっているのにログにSQLが出ない場合

- 設定変更**前**に実行したクエリは出ない。手順1のSQL実行後、新規にアプリを操作してから
  ログを確認する。
- `logging_collector = on`になっているか再確認する（`SHOW logging_collector;`）。
  `off`だと`log_statement`をどれだけ設定してもファイルには書き出されない
  （標準出力/Windowsイベントログ等、別の出力先に流れてしまう）。
- 見ているログファイルが、設定変更**後**に生成された最新のものか確認する
  （2-3の「最新のログファイルを取得するコマンド」を使い直す）。
- `log_destination`が既定の`stderr`から変更されていないか確認する
  （`SHOW log_destination;`）。会社の環境によっては`csvlog`や`jsonlog`に変更されている
  ことがあり、その場合はテキストの`.log`ファイルではなく`.csv`/`.json`拡張子の
  別ファイルに出力される。

### `pg_stat_statements`が見つからない・クエリが空（方法2）

- `SHOW shared_preload_libraries;`を実行し、`pg_stat_statements`が含まれているか確認する。
  含まれていなければ4-2〜4-3をやり直す（サービスの**完全再起動**が必須、reloadでは不可）。
- `CREATE EXTENSION`を実行し忘れていないか確認する（DBごとに必要）。
- 拡張作成**前**に実行されたクエリは集計されない。

### `postgresql.conf`を編集して保存できない

- メモ帳を管理者権限で起動していない可能性が高い（`Program Files`配下は一般権限だと
  書き込み拒否される）。メモ帳を右クリック→「管理者として実行」で開き直す。

---

## 付録: このリポジトリのVMでの検証記録（参考）

このドキュメントの内容は、`dicom-pacs-vm`（`192.168.93.128`、PostgreSQL 18）上で実際に
両方の方法を有効化し、動作確認した上で書いている。

- **方法1**: `log_statement='all'`, `log_min_duration_statement=0`, `lc_messages='C'`を
  設定し`pg_ctl reload`で反映。GraphQL API（`http://192.168.93.128:5030/graphql`）に
  実クエリを送信し、ログファイルへSQLが記録されること、文字化けが解消されていることを確認済み。
- **方法2**: `shared_preload_libraries='pg_stat_statements'`を設定し、PostgreSQLサービスを
  完全再起動（`net stop`/`net start postgresql-x64-18`）。OSレベルの設定・再起動はここまで
  検証済みだが、`CREATE EXTENSION`の実行にはデータベースの管理者パスワードが必要で、
  この検証環境ではその認証情報を保持していないため未実行。**pgAdmin4から`CREATE EXTENSION
  IF NOT EXISTS pg_stat_statements;`を1回実行すれば使えるようになるはず**（4-4参照）。

このVMは`~/.ssh/config`のエイリアス`dicomvm`経由でホストPCから操作しており、この付録内の
検証作業のみSSHコマンドを使っている。本編（1〜5章）はSSHを使わず、対象PC上で直接実行する
コマンドとして書いてある。
