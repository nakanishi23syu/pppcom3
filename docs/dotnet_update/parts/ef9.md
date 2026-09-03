## Entity Framework Core 9

### 保留中のモデルの変更がある場合に移行を適用すると例外がスローされます
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#applying-migrations-with-pending-model-changes-now-throws-an-exception

【前提知識】

- **マイグレーション（Migration）とは**
  EF Coreで「C#のクラス定義（モデル）」と「実際のDBのテーブル定義（スキーマ）」を一致させ続けるための仕組み。モデルを変更したら`dotnet ef migrations add <名前>`で「変更差分」をC#ファイルとして記録し、`dotnet ef database update`（またはコード内の`Database.Migrate()`）でその差分を実際のDBに適用する。
- **モデルスナップショット（ModelSnapshot）とは**
  「最後にマイグレーションを作った時点でのモデル全体の姿」を記録したC#ファイル（`XxxModelSnapshot.cs`）。EF Coreは新しいマイグレーションを作るとき、「今のモデル」と「このスナップショット」を比較して差分を計算する。このリポジトリでは`shared/DicomTool.Shared/Migrations/DicomDbContextModelSnapshot.cs`がこれにあたる。
- **`Migrate()`/`MigrateAsync()`とは**
  DbContextが持つ「まだDBに適用されていないマイグレーションを、古い順に全部適用する」メソッド。このリポジトリでは`backend/DicomTool.Api/Program.cs`の起動処理内で`db.Database.Migrate()`として呼ばれている。
- **「保留中のモデルの変更（pending model changes）」とは**
  開発者がエンティティクラス（例:`UserStudy.cs`）にプロパティを追加する等でモデルを変更したのに、`dotnet ef migrations add`で対応するマイグレーションファイルをまだ作っていない状態のこと。モデルとスナップショットが食い違っている状態、と言い換えられる。

【説明】

EF Core 9より前は、モデルに「まだマイグレーション化されていない変更」が残っていても、`Migrate()`/`MigrateAsync()`はそれに気づかず、単に「既存のマイグレーションファイルのうち未適用のものだけ」を適用して何事もなく成功していた。つまり、モデルを変更したのにマイグレーションを作り忘れても、アプリはエラーなく起動してしまい、DBのテーブルとC#のモデルがズレたまま気づかない、という事故が起きやすかった。

EF Core 9.0以降は、`dotnet ef database update`コマンド、および`Migrate()`/`MigrateAsync()`呼び出し時に、EF Coreが「今のモデル」と「最後のマイグレーションが作られた時点のモデル」を比較し、差分（保留中の変更）が見つかった場合は例外をスローするようになった。メッセージはおおむね「コンテキスト'DbContext'のモデルには保留中の変更があります。データベースを更新する前に、新しい移行を追加してください」という内容になる。この検査が不要な場合は、`ConfigureWarnings`で`RelationalEventId.PendingModelChangesWarning`を`Ignore`に指定することで従来通り抑制できる。

【放置したときの影響】

.NET 10 / EF Core 10へ上げる過程でEF Core 9を経由する際、「アプリのエンティティクラスは変更したのにマイグレーションを作り忘れていた」というケースがあると、これまで気づかなかったズレがここで初めて表面化し、**アプリの起動そのものが例外で失敗する**（このリポジトリでは`backend/DicomTool.Api`の起動時、`db.Database.Migrate()`の行で例外が飛ぶ）。逆に言えば、これは「今まで見逃していた設定ミスを教えてくれる」変更でもあるため、無理に抑制するより直すべき性質の変更。影響度は「高」。

【プロジェクトでの調べ方】

1. `backend/DicomTool.Api/Program.cs`の296〜309行目付近で`db.Database.Migrate()`が呼ばれていることを確認済み（`db.Database.IsRelational()`が真、つまり本番のPostgreSQL接続時のみ）。EF Core 9以降にアップグレードした際、まずこの起動処理が例外なく通るかどうかを実際に動かして確認するのが一番早い。
2. `shared/DicomTool.Shared/Data/DicomDbContext.cs`のエンティティ定義（`UserStudy`/`UserSeries`/`UserSop`/`AppUser`）と、`shared/DicomTool.Shared/Migrations/DicomDbContextModelSnapshot.cs`を見比べ、モデル変更後にマイグレーション追加を忘れている項目がないか確認する。
3. `dotnet ef migrations add TmpCheck --project shared/DicomTool.Shared --startup-project backend/DicomTool.Api`のようなコマンドを一度打ってみて、「差分なし」（＝空のマイグレーションが生成される、もしくは`No changes were detected`と出る）ことを確認するのも有効（差分があれば生成されたファイルは削除する）。
4. なお、`services/DicomTool.Worker`側は`Migrate()`を意図的に呼んでいない（`docs/CONTRACT.md`7章の規約で、マイグレーション適用は`DicomTool.Api`だけの責務としているため、Program.cs内のコメントにもその旨が明記されている）。この検査の影響を受けるのは実質`DicomTool.Api`のみ。

【改修方法】

- モデル変更を反映し忘れているなら、通常通り`dotnet ef migrations add <変更内容がわかる名前>`でマイグレーションを追加する。これが最も基本的かつ推奨される対処。
- 意図せずアプリのモデルにだけ差分があるように見える場合（例えば`DicomDbContextFactory.cs`のような設計時ファクトリの構成と、実行時の`Program.cs`側の構成が食い違っているケース）は、両者のモデル構成が一致しているか確認する。
- どうしてもこの検査を一時的に無効化したい場合のみ、`AddDbContext`のオプションで以下のように警告を無視する。
  ```csharp
  options.UseNpgsql(connectionString)
      .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
  ```
  ただし、これは「ズレを検知できなくする」だけなので、根本原因（マイグレーション追加忘れ）を放置しないこと。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### 明示的なトランザクションで移行を適用すると例外がスローされます
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#applying-migrations-in-an-explicit-transaction-now-throws-an-exception

【前提知識】

- **トランザクション（Transaction）とは**
  複数のDB操作を「全部成功するか、全部失敗して元に戻す（ロールバック）か」のどちらかに揃えるための仕組み。`BeginTransaction`で開始し、`Commit`で確定、失敗時は`Rollback`（もしくは`using`スコープを抜けることで自動ロールバック）する。
- **実行戦略（Execution Strategy）とは**
  クラウドDB（Azure SQL等）で一時的な接続断が起きた際、EF Coreが自動的に処理をリトライするための仕組み。`CreateExecutionStrategy().ExecuteAsync(...)`のように、リトライさせたい処理をラムダ式で囲んで使う。
- 元ネタで示されている「以前によく使われていたパターン」は、`ExecuteAsync`（リトライ対応）の中で自前の`BeginTransaction`〜`CommitAsync`を使い、その間に`MigrateAsync()`を呼ぶという書き方。

【説明】

EF Core 9より前は、`Migrate()`/`MigrateAsync()`自体はトランザクションを意識せず、呼び出し側が用意した外部のトランザクションの中でマイグレーションSQLを実行することも可能だった。

EF Core 9.0以降、`Migrate()`/`MigrateAsync()`は内部で自らトランザクションを開始し、かつ実行戦略（Execution Strategy）を使ってコマンドを実行するように変更された。ところが、EF Coreの実行戦略は「外側に既にユーザーが開始した明示的なトランザクションがある状態」との併用を許可しない設計になっている。そのため、上記のような「外部で`BeginTransaction`してから`MigrateAsync()`を呼ぶ」パターンを実行すると、`MigrationsUserTransactionWarning`という警告がエラー扱いで例外としてスローされるようになった。

理由は、明示的な外部トランザクションの中では、EF Coreがマイグレーション適用に必要な「同時実行保護のためのデータベースロック」を正しく取得できず、複数プロセスが同時にマイグレーションを試みた際の競合を防げなくなるため。

【放置したときの影響】

このパターンでマイグレーションを適用しているコードがあると、EF Core 9以降へのアップグレード後に**起動時（またはマイグレーション実行時）に例外で落ちる**。影響度は「高」。

【プロジェクトでの調べ方】

1. `backend/DicomTool.Api/Program.cs`の286〜312行目（`db.Database.Migrate()`を呼んでいる箇所）を確認したところ、`using (var scope = app.Services.CreateScope())`のブロック内で`Migrate()`を直接呼んでいるだけで、その前後に`BeginTransaction`や`CreateExecutionStrategy().ExecuteAsync`のような外部トランザクション/実行戦略のラップは**存在しない**。よってこの項目は**このリポジトリでは現状該当しない**。
2. 念のため、リポジトリ全体を`BeginTransaction`または`ExecuteAsync`でgrepし、`Migrate`と組み合わせて使われている箇所がないか確認するとよい（今回の調査では該当なし）。
3. 今後Azure SQL等のクラウドDBに接続先を変更し、リトライ対応の実行戦略を導入する場合に、このパターンを書いてしまわないよう注意する。

【改修方法】

- このリポジトリでは現状該当箇所がないため改修不要。
- 仮に今後このパターンのコードが追加された場合は、公式の推奨どおり「DB呼び出しがマイグレーション適用の1つだけ」なら、外部トランザクションと実行戦略のラップを削除し、単に`await dbContext.Database.MigrateAsync(cancellationToken);`とだけ書く。
- どうしても外部トランザクションが必要な特殊な事情がある場合のみ、警告を明示的に無視する。
  ```csharp
  options.ConfigureWarnings(w => w.Ignore(RelationalEventId.MigrationsUserTransactionWarning));
  ```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### Microsoft.EntityFrameworkCore.Design EF ツールの使用時に見つかりません
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#microsoftentityframeworkcoredesign-not-found-when-using-ef-tools

【前提知識】

- **`Microsoft.EntityFrameworkCore.Design`とは**
  `dotnet ef migrations add`や`dotnet ef database update`といった、開発時（設計時）にだけ使うEF Coreのコマンドラインツール（`dotnet-ef`）が内部的に読み込むアセンブリ。実行時（本番稼働時）には不要なため、通常は`PrivateAssets="all"`を付けて参照する（＝「自分のビルド成果物には含めない、開発時専用の依存」という指定）。
- **`deps.json`とは**
  .NETアプリをビルドすると生成される、「このアプリが依存しているアセンブリの一覧」を記録したJSONファイル。`dotnet`コマンドやツール類が実行時にどのDLLを読み込むべきかをここから解決する。

【説明】

以前は、`PrivateAssets="all"`を付けて`Microsoft.EntityFrameworkCore.Design`を参照しているだけで、`dotnet ef`コマンドは問題なく動作していた。これは、.NET SDKが`deps.json`に（本来公開されないはずの）プライベート参照のアセンブリ情報まで書き込んでしまうという、いわば「意図されていなかったが実際には効いていた」挙動にEF Coreツールが依存していたため。

.NET SDK 9.0.200以降でこの挙動自体が修正（本来の仕様通りに）されたことで、EFツール実行時に「ファイルまたはアセンブリ'Microsoft.EntityFrameworkCore.Design'を読み込めませんでした」という例外が出るようになった。この問題はEF Core / .NET SDK側の不具合という扱いで、EF Core 10で正式に修正される予定とされている。

【放置したときの影響】

`dotnet ef migrations add`や`dotnet ef database update`などの**開発時のコマンドが失敗する**。アプリ自体の実行時の動作には影響しない（あくまで開発ツールが動かなくなる問題）ため影響度は「ミディアム」。

【プロジェクトでの調べ方】

1. マイグレーション関連のcsprojで`Microsoft.EntityFrameworkCore.Design`の参照方法を確認する。`backend/DicomTool.Api/DicomTool.Api.csproj`・`services/DicomTool.Worker/DicomTool.Worker.csproj`・`services/DicomTool.DicomScp/DicomTool.DicomScp.csproj`・`shared/DicomTool.Shared/DicomTool.Shared.csproj`のいずれも
   ```xml
   <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.9">
     <PrivateAssets>all</PrivateAssets>
     ...
   </PackageReference>
   ```
   の形で参照しており、典型的な該当パターンに一致する。
2. 実際に`dotnet ef migrations add TmpCheck --project shared/DicomTool.Shared --startup-project backend/DicomTool.Api`を実行し、「アセンブリを読み込めません」という例外が出るかどうかで、手元の.NET SDKのバージョンがこの問題を踏む状態かを確認できる（`dotnet --version`で9.0.200以降かどうかも合わせて確認する）。
3. 現状このリポジトリはEF Core 10.0.9を使用しているため、EF Core 10側の修正が入っていれば問題自体が発生しない可能性が高い。もし発生した場合のみ次の改修方法を適用する。

【改修方法】

EF Core 10で正式修正されるまでの回避策として、公式ドキュメントは`PackageReference`に`<Publish>true</Publish>`を追加する方法を挙げている。

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.9">
  <PrivateAssets>all</PrivateAssets>
  <Publish>true</Publish>
</PackageReference>
```

ただし副作用として、`dotnet publish`等の出力/発行フォルダに`Microsoft.EntityFrameworkCore.Design.dll`がコピーされるようになる（本来は開発時専用のDLLなので、これが増えるだけで実害は小さいが気に留めておく）。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### EF.Functions.Unhex() により byte[]? が返されるようになりました
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#efunctionsunhex-now-returns-byte

【前提知識】

- **`EF.Functions.Unhex()`とは**
  LINQクエリの中でSQLite特有の`unhex()`関数（16進数文字列をバイナリに変換する）を呼び出すためのEF Core側のヘルパーメソッド。SQLiteプロバイダ固有の機能。
- **`byte[]`と`byte[]?`の違い**
  C#の「null許容参照型」機能の話。`byte[]`はコンパイラ上「nullは入らないはず」という注釈、`byte[]?`は「nullが入る可能性がある」という注釈。実行時の型自体は変わらないが、コンパイラの警告（Nullable警告）が変わる。

【説明】

以前は`EF.Functions.Unhex()`のC#側のメソッドシグネチャが`byte[]`（non-null）を返す注釈になっていたが、実際にはSQLiteの`unhex()`関数は無効な入力に対してNULLを返すことがあり、注釈と実際の挙動が矛盾していた（＝コンパイラ上は「nullにならない」はずなのに、実行時にnullが返ってくることがあった）。

EF Core 9.0以降、このメソッドの戻り値の注釈が`byte[]?`（null許容）に修正され、SQLiteの実際の挙動と一致するようになった。

【放置したときの影響】

コンパイル時のNullable警告が新たに出るようになる程度で、実行時の挙動自体は変わらない（元々nullが返り得た、という事実に注釈が追いついただけ）。影響度は「低」。

【プロジェクトでの調べ方】

- リポジトリ全体を`EF.Functions.Unhex`または`Unhex(`でgrepして呼び出し箇所を探す。今回の調査では、このプロジェクトはPostgreSQL（Npgsql）を使っており、SQLiteプロバイダ自体を参照していない（`Microsoft.EntityFrameworkCore.Sqlite`パッケージへの参照なし）ため、**この関数を使う余地自体がなく、このプロジェクトでは無関係**。

【改修方法】

- このリポジトリでは対象コードがないため改修不要。
- 仮に将来SQLiteプロバイダを使う機会があり、かつ`EF.Functions.Unhex()`の戻り値を`byte[]`（non-null前提）で受けているコードがある場合は、呼び出し元でnullチェックを追加するか、nullにならない確信があるならnull許容演算子`!`を付ける。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### コンパイル済みモデルが値コンバーター メソッドを直接参照するようになりました
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#the-compiled-model-now-references-value-converter-methods-directly

【前提知識】

- **値コンバーター（Value Converter）とは**
  C#のプロパティの型と、DBに実際に保存する型が異なる場合に、その変換方法を教えるEF Coreの仕組み。例えば「C#では`enum`だが、DBには文字列で保存したい」といったケースで`HasConversion(...)`を使って変換用のメソッド（ラムダ式）を指定する。
- **コンパイル済みモデル（Compiled Model）とは**
  通常EF Coreはアプリ起動時にリフレクション（実行時に型情報を読み取る仕組み。やや低速）でモデルを組み立てるが、`dotnet ef dbcontext optimize`コマンドを使うと、モデル構築処理をあらかじめC#コードとして生成しておき、起動を高速化できる。この生成されたC#コードが「コンパイル済みモデル」。
- **NativeAOT（Native Ahead-Of-Time compilation）とは**
  .NETアプリを実行時のJITコンパイルなしで、ネイティブの実行ファイルに事前コンパイルする技術。リフレクションを多用するコードとは相性が悪いため、EF CoreがNativeAOT対応を進める中でコンパイル済みモデルの生成方式も変更が入っている。

【説明】

以前、`dotnet ef dbcontext optimize`で生成されるコンパイル済みモデルは、値コンバーターの「型」（クラス）を参照する形でコードを生成していた。この場合、変換に使うメソッド自体がクラスの`private`メンバーであっても、型を経由してアクセスするため問題なく動作していた。

EF Core 9.0以降は、NativeAOTサポートのために生成方式が変わり、変換用メソッド自体を直接呼び出すコードが生成されるようになった。このとき、そのメソッドが`private`（外部からアクセス不可）だと、生成されたコード側からアクセスできず**コンパイルエラー**になる。

【放置したときの影響】

`dotnet ef dbcontext optimize`でコンパイル済みモデルを使っているプロジェクトに限り、値コンバーターのメソッドが`private`だと**ビルドが失敗する**。コンパイル済みモデルを使っていないプロジェクトには影響しない。影響度は「低」。

【プロジェクトでの調べ方】

1. `shared/DicomTool.Shared/Data/DicomDbContext.cs`の`OnModelCreating`を確認したが、`HasConversion`を使った値コンバーターの定義は存在しない（プロパティは`Guid`/`string`/`DateTime`/`bool`/`int`など、素直な型のみを使っている）。
2. リポジトリ全体を`HasConversion`または`ValueConverter`でgrepしても該当箇所はなし。
3. `dotnet ef dbcontext optimize`によるコンパイル済みモデルの生成もこのリポジトリでは行っていない（該当するプロジェクト設定やコマンドはCONTRACT.md等のドキュメントにも記載なし）。
4. 以上より、**このプロジェクトでは現状無関係**。

【改修方法】

- このリポジトリでは対象コードがないため改修不要。
- 仮に将来値コンバーターを追加し、かつコンパイル済みモデルを使う場合は、変換メソッドを`public`または`internal`にしておく。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### SqlFunctionExpression の null 値許容引数のアリティを検証
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#sqlfunctionexpression-validates-arity-of-nullability-arguments

【前提知識】

- **`SqlFunctionExpression`とは**
  EF Coreが独自にDB関数呼び出しをモデリングするために使う内部的な式木（Expression Tree）のクラス。通常のアプリ開発でLINQを書く分には直接触ることはなく、「カスタムSQL関数をEF Coreに登録する」ような高度な拡張（プロバイダ開発や、独自のDB関数マッピング）をする場合にのみ登場する。
- **「アリティ（arity）」とは**
  関数が受け取る「引数の個数」のこと。
- **`argumentsPropagateNullability`とは**
  「この引数がnullなら、関数全体の結果もnullとして扱ってよいか」を引数ごとにtrue/falseで指定する設定。SQLが生成する`CASE WHEN ... IS NULL THEN NULL ELSE ...`のような分岐に影響する。

【説明】

以前は、`SqlFunctionExpression`を構築する際、実際の引数の個数（`arguments`）と、null値許容伝播の指定（`argumentsPropagateNullability`）の要素数が一致していなくても、特にチェックされずに通っていた。

EF Core 9.0以降は、この2つの数が一致しない場合に例外をスローするよう検証が追加された。数が食い違っていると、意図しない引数に対してnull伝播の判定が行われてしまい、予期しないSQLや実行時エラーの原因になり得るため。

【放置したときの影響】

**独自にカスタムSQL関数をEF Coreへマッピングしている場合に限り**、その定義に不整合があるとアプリ起動時またはクエリ実行時に例外が出るようになる。通常のLINQクエリを書くだけの利用では影響しない。影響度は「低」。

【プロジェクトでの調べ方】

- リポジトリ全体を`SqlFunctionExpression`でgrepしたが該当箇所なし。このプロジェクトは`Npgsql.EntityFrameworkCore.PostgreSQL`が提供する標準機能の範囲でしかDBアクセスしておらず、カスタムSQL関数のマッピング（`DbFunction`属性や`HasDbFunction`、独自`SqlFunctionExpression`構築など）は行っていない。**このプロジェクトでは無関係**。

【改修方法】

- このリポジトリでは対象コードがないため改修不要。
- 仮に将来カスタムSQL関数マッピングを追加する場合は、`argumentsPropagateNullability`の要素数を`arguments`の要素数に必ず揃える（迷った場合は全要素`false`にしておくのが無難）。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### ToString() メソッドが null インスタンスの空の文字列を返すようになりました
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#tostring-method-now-returns-an-empty-string-for-null-instances

【前提知識】

- **`bool?`（null許容のbool）とは**
  C#では`bool`は「true/false」の2値しか持てないが、`bool?`（`Nullable<bool>`の略記）は「true/false/null（値なし）」の3値を持てる型。DBの`NULL`をC#で表現する際によく使う。
- LINQ to Entitiesでは、C#のメソッド呼び出し（`x.SomeProperty.ToString()`など）がそのままSQLに変換される。このとき、C#での`.ToString()`の挙動とSQLでの変換結果を一致させる必要があるが、これまでnull絡みのケースで食い違いがあった。

【説明】

以前は、EF CoreのLINQクエリ内で`.ToString()`を呼んだ際の「null値の扱い」が、対象の型やケースによって不統一だった。例えば、`bool?`型のプロパティ自体がnullの場合は`.ToString()`の結果がnullになる一方で、（プロパティではなく）`bool?`型のnull定数式に対して`.ToString()`を呼んだ場合は"True"という文字列が返る、といった矛盾があった。

EF Core 9.0以降は、この挙動が統一され、null値に対する`.ToString()`は常に空文字列（`""`）を返すようになった。これは、通常のC#における`Nullable<T>.ToString()`（nullの場合は空文字列を返す）の挙動とも一致する形になっている。

【放置したときの影響】

**LINQクエリの中で、null許容型のプロパティに対して`.ToString()`を呼んでいる箇所がある場合のみ**、返ってくる値がこれまでの"null"や"True"等から空文字列`""`に変わり、その結果を使った以降の処理（文字列比較、null判定など）の挙動が変わる可能性がある。影響度は「低」（該当パターンを使っていなければ影響なし）。

【プロジェクトでの調べ方】

1. リポジトリ全体を`.ToString()`でgrepし、LINQクエリ（`Where`/`Select`等のラムダ式やGraphQLのクエリ実装内）で使われている箇所がないか確認した。
2. 見つかった`.ToString()`呼び出しは、`services/DicomTool.DicomScp/Services/DicomScpService.cs`（`study.Series.Count.ToString()`等、集計結果の`int`に対するもの）、`services/DicomTool.DicomScp/Services/DicomScuTestService.cs`（`receivedStatus?.Code.ToString()`、DICOM通信のステータスコードに対するもの）、`backend/DicomTool.Api/GraphQL/Mutation.cs`（`input.TargetType.ToString()`、enumに対するもの）のみで、いずれも**C#側のインメモリな値に対する呼び出しであり、EF CoreのLINQクエリ（DB問い合わせ）の中で使われているものではない**。
3. `shared/DicomTool.Shared/Data/DicomDbContext.cs`や、DB検索を行っているクエリコード（`backend/DicomTool.Api/GraphQL/Query.cs`等）にも、DBプロパティに対する`.ToString()`呼び出しは見当たらなかった。
4. 以上より、**このプロジェクトでは現状無関係**（今後クエリ内でnull許容プロパティに`.ToString()`を使うコードを書く場合にのみ注意すればよい）。

【改修方法】

- このリポジトリでは対象コードがないため改修不要。
- 仮に将来「nullの場合は文字列としてもnullのままにしたい」という古い挙動が必要になった場合は、クエリを明示的に書き換える。
  ```csharp
  // 例: nullなら文字列としてもnullを返したい場合
  var result = x.NullableBool == null ? null : x.NullableBool.ToString();
  ```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### 共有フレームワークの依存関係が 9.0.x に更新されました
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#shared-framework-dependencies-updated-to-90x

【前提知識】

- **共有フレームワーク（Shared Framework）とは**
  ASP.NET Coreアプリをビルドすると、`System.Text.Json`のような超基本的な.NET標準ライブラリの一部は、サーバーに元々インストールされている「.NET共有ランタイム」から実行時に解決され、アプリの発行（publish）フォルダにわざわざコピーされないことがある。これによりデプロイ物のサイズを小さくできる。
- **`Microsoft.NET.Sdk.Web`とは**
  ASP.NET Core Webアプリ用のプロジェクトSDK。`<Project Sdk="Microsoft.NET.Sdk.Web">`のように`.csproj`の先頭に指定する。

【説明】

以前、`net8.0`を対象にした`Microsoft.NET.Sdk.Web`アプリは、`System.Text.Json`等の基本パッケージを、共有フレームワーク（.NET 8ランタイムに元々含まれるもの）経由で解決していたため、これらのアセンブリは通常デプロイ物に含まれなかった。

EF Core 9.0は引き続き`net8.0`をサポートするが、内部的に`System.Text.Json`、`Microsoft.Extensions.Caching.Memory`、`Microsoft.Extensions.Configuration.Abstractions`、`Microsoft.Extensions.Logging`、`Microsoft.Extensions.DependencyModel`の**9.0.x版**を参照するようになった。`net8.0`向けにビルドしたアプリの場合、これらは共有フレームワークのバージョン（8.0.x）と食い違うため、共有フレームワークを利用してのデプロイ回避ができなくなり、これらのアセンブリが明示的にデプロイ物に含まれるようになる。

理由は、バージョンを揃えることで最新のセキュリティ修正を取り込みやすくし、またEF Core内部のサービスモデルを単純化するため。

【放置したときの影響】

このリポジトリはすでに`net10.0`を対象にしているため、直接の影響はない（この項目は「`net8.0`のまま据え置き、EF Coreだけ9系にアップグレードする」ケースに関する注意点）。一般論としては、対象フレームワークとEF Coreのバージョンにズレがあるプロジェクトで、発行時のデプロイ物サイズがわずかに増える程度で、機能的な破壊は基本的にない。影響度は「低」。

【プロジェクトでの調べ方】

- `backend/DicomTool.Api/DicomTool.Api.csproj`・`shared/DicomTool.Shared/DicomTool.Shared.csproj`など全csprojで`<TargetFramework>net10.0</TargetFramework>`になっていることを確認済み。EF Coreのバージョンも`10.0.9`であり、対象フレームワークとEF Coreのメジャーバージョンが揃っている。**この項目が問題になる「net8.0のままEF Core 9系だけ上げる」という状況にはそもそも該当しない。**

【改修方法】

- このリポジトリでは対象外のため改修不要。
- 一般論として、この変更による挙動を避けたい（＝共有フレームワーク経由のデプロイ回避を維持したい）場合は、アプリの対象フレームワークをEF Coreのメジャーバージョンと合わせる（EF Core 9ならnet9.0、EF Core 10ならnet10.0）のが公式の推奨。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### EF ツールで .NET Framework プロジェクトがサポートされなくなりました
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#net-framework-projects-no-longer-supported-by-ef-tools

【前提知識】

- **.NET Framework（旧来の.NET）と.NET（.NET 5以降、モダンな.NET）の違い**
  「.NET Framework」はWindows専用の古い実行環境（バージョン4.8等で開発終了）で、「.NET」（.NET 5/6/7/8/9/10...）はクロスプラットフォーム対応の現行の実行環境。両者は名前が似ているが別物で、プロジェクトファイル（`.csproj`）の`<TargetFramework>`に`net48`のように書けば前者、`net10.0`のように書けば後者になる。
- **EFツール（`dotnet-ef`）とは**
  「Microsoft.EntityFrameworkCore Design EF ツール」の項でも触れた、`dotnet ef migrations add`等を実行するコマンドラインツール。

【説明】

以前は、`dotnet-ef` CLIやVisual Studioのパッケージマネージャーコンソールのツール（`Add-Migration`等）が、`.NET Framework`（例: `net48`）を対象とするプロジェクトに対しても動作していた。

EF Core 9.0以降は、EFツールが対応するEF Core自体のバージョンが.NET Frameworkでは動かなくなったため、スタートアッププロジェクト（DBコンテキストの構成を読み込む起点となるプロジェクト）が.NET Frameworkを対象にしていると、EFツール実行時にエラーになる。

【放置したときの影響】

**.NET Framework（`net48`等）を対象にしたプロジェクトでマイグレーション管理をしている場合のみ**、EFツールが使えなくなる。影響度は「低」（大半のモダンな.NETプロジェクトには無関係）。

【プロジェクトでの調べ方】

- 全csproj（`backend/`・`services/`・`shared/`・`frontend/timeline/`配下）の`<TargetFramework>`を確認したところ、いずれも`net10.0`（モダンな.NET）であり、`net48`のような.NET Framework向けの指定は存在しない。**このプロジェクトでは無関係**。

【改修方法】

- このリポジトリでは対象外のため改修不要。
- 仮に.NET Frameworkプロジェクトが絡む場合は、そのプロジェクトを.NET（8以降）に更新する必要がある。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### EF.Constant()およびEF.Parameter()は、コンパイルされたクエリ内で動作しなくなりました
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#efconstant-and-efparameter-no-longer-work-inside-compiled-queries

【前提知識】

- **コンパイル済みクエリ（Compiled Query）とは**
  `EF.CompileQuery`/`EF.CompileAsyncQuery`を使って、頻繁に実行するLINQクエリのSQL変換処理をあらかじめ行っておき、実行のたびに変換し直すコストを省く仕組み。パフォーマンスチューニングの一種で、通常のアプリでは使わないことが多い、やや高度な機能。
- **`EF.Constant()`/`EF.Parameter()`とは**
  クエリ中の値を「SQLパラメータとして送るか」「SQL文の中に定数として埋め込むか」を明示的に指定するためのヒント。`EF.Constant()`は「定数として埋め込む」ことを強制し、`EF.Parameter()`は「パラメータとして送る」ことを強制する。
- **クエリキャッシュとは**
  同じ形のLINQクエリを毎回SQLへ変換し直すのは無駄なので、変換結果をキャッシュして使い回す仕組み。EF Coreの内部最適化の1つ。

【説明】

以前は、`EF.CompileQuery`/`EF.CompileAsyncQuery`で作ったコンパイル済みクエリの中でも、`EF.Constant()`/`EF.Parameter()`が問題なく使えていた。

EF Core 9.0で`EF.Constant()`の内部実装が変更され、クエリキャッシュより後の段階（クエリが実際に実行される直前）で処理されるようになった。この変更が、「クエリの変換結果をあらかじめ固定してしまう」コンパイル済みクエリの仕組みと相性が悪くなり、両方を組み合わせると`InvalidCastException`がスローされるようになった。

【放置したときの影響】

**コンパイル済みクエリ（`EF.CompileQuery`等）を使い、かつその中で`EF.Constant()`/`EF.Parameter()`を呼んでいる場合のみ**、実行時に例外が発生する。この2つの機能自体、通常のアプリ開発ではあまり使われない高度な最適化手段のため、該当するプロジェクトは限られる。影響度は「低」。

【プロジェクトでの調べ方】

- リポジトリ全体を`EF.CompileQuery`、`EF.CompileAsyncQuery`、`EF.Constant`、`EF.Parameter`でgrepしたが、いずれも該当箇所なし。このプロジェクトのDBアクセスはGraphQL経由の通常のLINQクエリ（`backend/DicomTool.Api/GraphQL/Query.cs`等）のみで、コンパイル済みクエリという高度な最適化は使われていない。**このプロジェクトでは無関係**。

【改修方法】

- このリポジトリでは対象コードがないため改修不要。
- 仮に将来コンパイル済みクエリを導入し、かつ`EF.Constant()`/`EF.Parameter()`と組み合わせたくなった場合は、どちらかを諦める必要がある。コンパイル済みクエリからConstant/Parameter呼び出しを削除するか、そのクエリではコンパイル済みクエリの利用自体をやめる。`EF.Constant()`を削除すると値が通常のSQLパラメータとして送られるようになり、SQLサーバー側のクエリプラン最適化の効き方が変わってパフォーマンスに影響する可能性がある点に注意。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### 一部の NoTrackingWithIdentityResolution クエリが JSON コレクションで禁止されるようになりました
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#some-notrackingwithidentityresolution-queries-are-now-disallowed-with-json-collections

【前提知識】

- **JSONマップされたエンティティ（Owned Entity in JSON column）とは**
  EF Coreの機能の1つで、C#のクラス（コレクションを含む）を、DBのリレーショナルな複数テーブルにではなく、1つのカラムにJSON文字列としてまとめて保存する仕組み（`OwnsMany(...).ToJson()`のように設定する）。
- **`AsNoTrackingWithIdentityResolution()`とは**
  EF Coreの「変更トラッキング（Change Tracking、DBから読み込んだエンティティの変更を検知する仕組み）」を無効化しつつ（`AsNoTracking`相当で読み取り専用・高速化）、同じ主キーを持つエンティティが複数回登場するクエリ結果でも、C#オブジェクトとしては同一インスタンスに統合してくれる（Identity Resolution）、という特殊な読み取りモード。
- **ストリーミング（Streaming）とは**
  DBからの読み込み結果を、全部メモリに貯めてから処理するのではなく、行が届くたびに逐次処理していく方式。メモリ効率は良いが、「後から来る行の情報で前の行の解釈を変える」といった処理とは相性が悪いことがある。

【説明】

以前は、JSONカラムにマップされたエンティティコレクションを含むクエリで`AsNoTrackingWithIdentityResolution`を使うと、EF Coreがどの順番でJSONの断片を読み進めるか（具体化順序）によっては、誤った結果を返したり、データが壊れたように見えたり、`Invalid token type: 'StartObject'`という原因のわかりにくい例外が出たりすることがあった。

EF Core 9.0以降、こうした問題が起きうる特定のJSONコレクションクエリパターン（例: JSONコレクションのナビゲーションプロパティに対して直接`Where`/`Skip`/`Take`等を使うクエリ）では、`AsNoTrackingWithIdentityResolution`の使用自体が制限され、危険なパターンを検知すると例外がスローされるようになった。理由は、ID解決用の変更トラッカーの仕組みが、JSONのストリーミング特性とかみ合わず、キー値を正しく反映できないままデータ破損に繋がる恐れがあったため。

【放置したときの影響】

**JSONカラムマッピング（`.ToJson()`）を使い、かつ`AsNoTrackingWithIdentityResolution`を組み合わせて特定のクエリパターンを書いている場合のみ**、クエリ実行時に例外が発生する。それ以外では影響しない。影響度は「低」。

【プロジェクトでの調べ方】

1. `shared/DicomTool.Shared/Data/DicomDbContext.cs`の`OnModelCreating`を確認したところ、`ToJson()`を使ったJSONカラムマッピングは行われていない（`UserStudy`/`UserSeries`/`UserSop`/`AppUser`はいずれも通常のリレーショナルテーブル・外部キーによる関連付けで、`HasMany(...).WithOne(...)`という通常のリレーション定義のみ）。
2. リポジトリ全体を`ToJson()`、`AsNoTrackingWithIdentityResolution`でgrepしても該当箇所なし。
3. 以上より、**このプロジェクトでは現状無関係**。

【改修方法】

- このリポジトリでは対象コードがないため改修不要。
- 仮に将来JSONカラムマッピングを導入する場合、ID解決（同一インスタンスへの統合）が必要なら`AsTracking()`を使った通常の追跡クエリに切り替え、ID解決が不要なら単純な`AsNoTracking()`を使う。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### 保留中のすべての移行は、1 つのトランザクションで適用されます
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#all-pending-migrations-are-now-applied-in-a-single-transaction

【前提知識】

- 「マイグレーション」「トランザクション」の意味は、この文書の1つ目の項目（「保留中のモデルの変更がある場合に移行を適用すると例外がスローされます」）の【前提知識】を参照。
- **「保留中の移行が複数ある」とは**
  例えば、しばらくDBを更新していない間にマイグレーションファイルが3個追加されていた場合、`Migrate()`を1回呼ぶとその3個を古い順にまとめて適用する。この「3個の適用」全体を指して「保留中のすべての移行」と呼ぶ。

【説明】

以前は、複数の保留中マイグレーションを適用する際、既定では**マイグレーションごとに個別のトランザクション**で囲んでいた。そのため、例えば3個中2個目の適用中にエラーが起きた場合、「1個目は適用済み、2個目・3個目は未適用」という中途半端な状態がDBに残る可能性があった。

EF Core 9.0以降は、既定の挙動が変わり、**保留中の全マイグレーションを1つの大きなトランザクション**で囲んで適用するようになった。これにより、途中でエラーが起きた場合は全体がロールバックされ、「DBが完全に最新の状態になっているか、エラー前とまったく同じ状態のままか」のどちらかにしかならず、中間状態を避けられるようになった。なお、この挙動はEF Core 10で元（マイグレーションごとに個別トランザクション）に戻されている。

【放置したときの影響】

通常のアプリでは、「複数マイグレーションが未適用のまま溜まった状態で一気に適用する」機会自体が少なく、また挙動としてはむしろ安全側（オールオアナッシング）への変更のため、実害があるケースは限定的。ただし、「1つのトランザクションで複数マイグレーションのDDL（テーブル作成・変更等のSQL）をまとめて実行する」ことに対応していないDB/DDLパターン（一部のDBでは特定のDDL文をトランザクション内で実行できない制約がある）を使っている場合には影響が出ることがある。影響度は「低」。

【プロジェクトでの調べ方】

1. `backend/DicomTool.Api/Program.cs`の`db.Database.Migrate()`呼び出し（302〜305行目）は、マイグレーションの適用方法についてオプションを特にカスタマイズしておらず、既定の挙動をそのまま使っている。
2. 使用DBはPostgreSQL（`Npgsql.EntityFrameworkCore.PostgreSQL`）であり、PostgreSQLは基本的にDDL文もトランザクション内で実行できるため、「1つのトランザクションにまとめられない」という致命的な問題は起きにくい。
3. `shared/DicomTool.Shared/Migrations/`配下の既存マイグレーションは`20260807115813_InitialCreate`の1つのみで、現時点では「複数の未適用マイグレーションが溜まった状態で一括適用する」状況自体がまだ発生していない。今後マイグレーションが増えてきたら、まとめて適用するテストを一度行っておくと安心。

【改修方法】

- 現状は特に問題が確認されていないため改修不要。
- もし将来「マイグレーションごとに個別トランザクションにしたい（EF Core 9より前の挙動に戻したい）」という要件が出た場合は、公式の案内どおりEF Core 10へアップグレードすることでこの挙動が元に戻る。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

#### Azure Cosmos DBの破壊的変更

このリポジトリはDB providerとして`Npgsql.EntityFrameworkCore.PostgreSQL`（PostgreSQL）のみを使用しており、`Microsoft.EntityFrameworkCore.Cosmos`パッケージへの参照はいずれのcsprojにも存在しない（`backend/DicomTool.Api.csproj`ではテスト用に`Microsoft.EntityFrameworkCore.InMemory`を使っているのみ）。そのため、以下のAzure Cosmos DB関連の10項目は**すべてこのプロジェクトでは無関係**。今後Cosmos DBプロバイダーを導入する可能性がある場合に備えて、内容だけ簡潔に記録しておく。

### ディスクリミネーター プロパティの名前が、$type ではなく Discriminator に変更されました
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#the-discriminator-property-is-now-named-type-rather-than-discriminator

【前提知識】

- **ディスクリミネーター（Discriminator）とは**
  1つのテーブル（またはJSON文書）に複数の派生型（継承関係にあるクラス）のデータを混在して保存する際、「このレコードは実際にはどの派生クラスのインスタンスか」を区別するために追加される特別なカラム/プロパティ。

【説明】

以前、Cosmos DBプロバイダーがJSON文書に埋め込むディスクリミネータープロパティの名前は既定で`Discriminator`だった。EF Core 9.0以降は既定の名前が`$type`に変更された。理由は、System.Text.Jsonのポリモーフィズム（多態性）サポート等、JSONエコシステム全体で`$type`という命名が広く使われる慣習に合わせたため。旧バージョンで作られたドキュメントは古い`Discriminator`名のプロパティを持ったままなので、アップグレード後にそのままクエリすると読み取れなくなる。

【放置したときの影響】

Cosmos DBを使っていないこのプロジェクトには影響なし。（Cosmos DB利用時は「高」相当の影響とされる項目。）

【プロジェクトでの調べ方】

- 全csprojを`Cosmos`でgrepし、`Microsoft.EntityFrameworkCore.Cosmos`への参照がないことを確認済み。**このプロジェクトでは無関係**。

【改修方法】

- 対象コードがないため改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### id プロパティには、既定で EF キー プロパティのみ含まれるようになりました
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#the-id-property-now-contains-only-the-ef-key-properties-by-default

【前提知識】

- Cosmos DBの各JSON文書は`id`という特別なプロパティ（文書内で一意な識別子）を持つ。

【説明】

以前、EFはエンティティ型のディスクリミネーター値をこの`id`プロパティに埋め込んでいた（例:`Blog|8`のように型名とキー値を連結）。EF Core 9.0以降は、`id`にはキー値のみが含まれるようになった（例:`8`のみ）。理由は、型ごとに独立したキー空間を持つ設計の方が、リレーショナルDBよりもCosmos DBのようなNoSQLでは一般的な慣習であるため。

【放置したときの影響】

Cosmos DBを使っていないこのプロジェクトには影響なし。（Cosmos DB利用時は「高」相当の影響とされる項目。）

【プロジェクトでの調べ方】

- 上記と同様、Cosmos DBプロバイダーへの参照なしを確認済み。**このプロジェクトでは無関係**。

【改修方法】

- 対象コードがないため改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### JSON id プロパティがキーにマップされる
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#the-json-id-property-is-mapped-to-the-key

【前提知識】

- **シャドウプロパティ（Shadow Property）とは**
  C#のクラスには存在しないが、EF Coreがモデル内部で「見えないプロパティ」として保持し、DB側にだけ対応するカラムを作る仕組み。

【説明】

以前、どのプロパティも明示的に`id`にマップされていない限り、EFは`id`用に別のシャドウプロパティを自動生成しており、結果としてキー値が2箇所（本来のキープロパティと、シャドウの`id`）に重複して保存されていた。EF Core 9以降は、規約によりキープロパティ自体が直接`id`にマップされるようになり、この重複がなくなった。

【放置したときの影響】

Cosmos DBを使っていないこのプロジェクトには影響なし。（Cosmos DB利用時は「高」相当の影響とされる項目。）

【プロジェクトでの調べ方】

- Cosmos DBプロバイダーへの参照なしを確認済み。**このプロジェクトでは無関係**。

【改修方法】

- 対象コードがないため改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### Azure Cosmos DB プロバイダー経由での I/O の同期はサポートされなくなりました
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#sync-io-is-no-longer-supported-by-the-azure-cosmos-db-provider

【前提知識】

- **同期I/Oと非同期I/Oの違い**
  DBアクセスのようなI/O処理には、呼び出したスレッドを処理完了までブロックし続ける「同期」（`ToList()`、`SaveChanges()`等）と、待っている間に他の作業ができる「非同期」（`ToListAsync()`、`SaveChangesAsync()`等、`await`を伴う）がある。

【説明】

以前、`ToList()`や`SaveChanges()`のような同期メソッドを呼ぶと、内部的に`.GetAwaiter().GetResult()`で非同期処理を同期的にブロックして待っていた（デッドロックの原因になりやすい書き方）。EF Core 9.0以降、Cosmos DBプロバイダー経由の同期I/O呼び出しは既定で例外をスローするようになった。理由は、非同期メソッドの同期ブロックがデッドロックの温床になること、また元々Cosmos DB SDK自体が非同期APIしかサポートしていないため。

【放置したときの影響】

Cosmos DBを使っていないこのプロジェクトには影響なし。（Cosmos DB利用時は「ミディアム」相当の影響とされる項目。）

【プロジェクトでの調べ方】

- Cosmos DBプロバイダーへの参照なしを確認済み。**このプロジェクトでは無関係**。なお、このプロジェクトはPostgreSQL（Npgsql）を使っており、GraphQLの各リゾルバ（`backend/DicomTool.Api/GraphQL/Query.cs`等）はもともと非同期API（`ToListAsync()`等）を使う設計になっている。

【改修方法】

- 対象コードがないため改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### SQL クエリで JSON 値を直接プロジェクションする必要があります / 未定義の結果が自動フィルター
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#sql-queries-must-directly-project-json-values--undefined-results-are-now-automatically-filtered-from-query-results

【前提知識】

- **プロジェクション（Projection）とは**
  クエリ結果として「どの列（プロパティ）だけを取り出すか」を指定すること。LINQの`Select`に対応する。
- Cosmos DBのクエリ言語はSQLに似た独自のSQL（Cosmos DB SQL API）で、`SELECT`に`VALUE`という修飾子を付けられる。

【説明】

以前、自前のSQLクエリで`SELECT c["City"] FROM root c`のように書くと、各結果を`{"City": "Tokyo"}`のようなJSONオブジェクトにラップして返していた。EF Core 9.0以降は`SELECT VALUE c["City"] FROM root c`のように`VALUE`修飾子を使うようになり、値そのものが直接返るようになった。これに伴い、「プロパティが存在しない（undefined）結果を自動的にフィルターして除外する」というCosmos DBの挙動も影響を受け、以前は「1件のnull結果」として返っていたものが、新方式では結果自体の件数が減る、という違いが生じる。

【放置したときの影響】

Cosmos DBを使っていないこのプロジェクトには影響なし。（Cosmos DB利用時は「ミディアム」相当の影響とされる項目。）

【プロジェクトでの調べ方】

- Cosmos DBプロバイダーへの参照なしを確認済み。またこのプロジェクトは自前のCosmos DB SQLクエリ（`FromSqlRaw`相当のCosmos DB版等）も使っていない。**このプロジェクトでは無関係**。

【改修方法】

- 対象コードがないため改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### 誤って変換されたクエリは変換されなくなりました
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#incorrectly-translated-queries-are-no-longer-translated

【前提知識】

- 特になし（LINQのメソッドチェーンの並び順の話）。

【説明】

以前、`.Take(5).Where(...)`のようにLINQ上で`Take`を先に書いても、生成されるCosmos DB SQLでは`WHERE`句が`OFFSET/LIMIT`より前に来てしまう（SQL的な意味では正しい順序だが、元のLINQの意図した「先に5件取ってから絞り込む」という意味とは異なる）という誤変換が起きることがあり、誤った結果を返す原因になっていた。EF Core 9.0以降は、このような誤変換が起きるパターンを検知した場合、変換自体を行わず例外をスローするようになった。理由は、サイレントに間違った結果を返す（気づかれにくいデータ不整合）よりも、早期に例外で気づかせる方を優先したため。

【放置したときの影響】

Cosmos DBを使っていないこのプロジェクトには影響なし。（Cosmos DB利用時は「ミディアム」相当の影響とされる項目。）

【プロジェクトでの調べ方】

- Cosmos DBプロバイダーへの参照なしを確認済み。**このプロジェクトでは無関係**。

【改修方法】

- 対象コードがないため改修不要。
- 仮にCosmos DBを使っていて該当例外が出た場合は、LINQ演算子の順序を`.Where(...).Take(5)`のように、絞り込みを先に書く順序へ入れ替える。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### 無視される代わりに HasIndex がスローされるようになりました
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#hasindex-now-throws-instead-of-being-ignored

【前提知識】

- **`HasIndex`とは**
  リレーショナルDBで「このカラムに検索用のインデックスを貼る」ことを指定するEF Coreのモデル構成メソッド（`shared/DicomTool.Shared/Data/DicomDbContext.cs`でもPostgreSQL向けに多数使われている、通常のリレーショナルDBでは頻出の機能）。

【説明】

以前、Cosmos DBプロバイダーに対して`HasIndex`を呼んでも、Cosmos DBには適用できない設定のため単に無視されていた（エラーにはならないが何も起きない）。EF Core 9.0以降は、Cosmos DBプロバイダーに対して`HasIndex`を指定すると例外がスローされるようになった。理由は、Cosmos DBは全プロパティに既定でインデックスが付く設計であり、`HasIndex`という「何もしない」呼び出しを黙って許可する意味がなかったため。

【放置したときの影響】

Cosmos DBを使っていないこのプロジェクトには影響なし。（Cosmos DB利用時は「低」相当の影響とされる項目。）このプロジェクトの`HasIndex`呼び出し（`shared/DicomTool.Shared/Data/DicomDbContext.cs`内、`UserStudy`/`UserSeries`/`UserSop`/`AppUser`向けの各種インデックス定義）は、すべてPostgreSQL（リレーショナルDB）向けであり、正常に機能する。

【プロジェクトでの調べ方】

- Cosmos DBプロバイダーへの参照なしを確認済み。**このプロジェクトでは無関係**（`HasIndex`自体は多用しているが、PostgreSQL向けであり本項目の対象外）。

【改修方法】

- 対象コードがないため改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### 9.0.0-rc.2 の後、IncludeRootDiscriminatorInJsonId は HasRootDiscriminatorInJsonId に名前が変更されました
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#after-900-rc2-includerootdiscriminatorinjsonid-was-renamed-to-hasrootdiscriminatorinjsonid

【前提知識】

- 特になし（Cosmos DBプロバイダー固有のAPI名の話）。

【説明】

Cosmos DBプロバイダーのプレビュー版（9.0.0-rc.2）で使われていた`IncludeRootDiscriminatorInJsonId`というメソッド名が、正式リリースまでの間に`HasRootDiscriminatorInJsonId`に変更された。単純なAPI名の変更であり、機能自体の変更はない。

【放置したときの影響】

Cosmos DBを使っていないこのプロジェクトには影響なし。

【プロジェクトでの調べ方】

- Cosmos DBプロバイダーへの参照なしを確認済み。**このプロジェクトでは無関係**。

【改修方法】

- 対象コードがないため改修不要。
- 仮にプレビュー版から`IncludeRootDiscriminatorInJsonId`を使っているコードがあれば、`HasRootDiscriminatorInJsonId`に置き換える。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### 参照先の Newtonsoft.Json バージョンが 10.0.2 から 13.0.1 に更新されました
リンク：https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes#the-referenced-newtonsoftjson-version-was-updated-from-1002-to-1301

【前提知識】

- **Newtonsoft.Json（Json.NET）とは**
  .NETで古くから使われている代表的なJSON処理ライブラリ。Cosmos DB SDK（Cosmos DBプロバイダーが内部的に使うクライアントライブラリ）が依存している。

【説明】

Cosmos DBプロバイダーが参照する`Newtonsoft.Json`のバージョンが、セキュリティ修正を取り込むため`10.0.2`から`13.0.1`に更新された。通常はアプリ側で意識する必要はないが、アプリ自身が`Newtonsoft.Json`の特定の古いバージョンに強く依存している場合は、バージョン競合が起きないか確認が必要になることがある。

【放置したときの影響】

Cosmos DBを使っていないこのプロジェクトには影響なし。

【プロジェクトでの調べ方】

- Cosmos DBプロバイダーへの参照なしを確認済み。また全csprojを`Newtonsoft.Json`でgrepしても直接の参照は見当たらなかった。**このプロジェクトでは無関係**。

【改修方法】

- 対象コードがないため改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）
