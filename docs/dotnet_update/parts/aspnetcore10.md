## ASP.NET Core 10

### 既知の API エンドポイントで無効になっている Cookie ログイン リダイレクト
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/breaking-changes/10/cookie-authentication-api-endpoints?view=aspnetcore-10.0

【前提知識】

- **認証（Authentication）と認可（Authorization）の違い**
  「認証」は「あなたは誰ですか」を確認すること（ログインしているか、トークンは本物か）。「認可」は「あなたにこの操作をする権限がありますか」を確認すること。ASP.NET Coreでは`app.UseAuthentication()` → `app.UseAuthorization()`の順でミドルウェア（後述）が実行され、認証が先に行われて「誰か」が確定してから、認可で「権限があるか」が判定される。
- **Cookie認証スキームとは（この項目の主役）**
  ASP.NET Coreには複数の「認証方式（スキーム）」があり、それぞれ別の仕組みで動く。
  - `AddCookie(...)` … 昔ながらのWebアプリ（Razor Pages/MVCでサーバーがHTMLページを返す形式）向け。ログインに成功すると、サーバーがユーザー情報を暗号化してCookieに詰めて返す。以降のリクエストはブラウザが自動でそのCookieを送ってくるので、サーバー側はCookieの中身を復号してユーザーを特定する。**この認証方式には昔から「未ログインで保護されたページにアクセスされたら、ログインページへHTTP 302リダイレクトする」という機能が組み込まれている**（`LoginPath`というオプションで飛び先を指定する)。ブラウザの画面遷移が前提の、人間がWebページを見るシナリオ向けの機能。
  - `AddJwtBearer(...)` … API向け。クライアント（フロントエンドのJavaScriptやモバイルアプリ）が`Authorization: Bearer <トークン>`ヘッダー（またはこのプロジェクトのようにCookie等）でJWT（JSON Web Token、後述）を送ってきて、それをサーバーが検証する。JWT Bearer認証には元々「ログインページへリダイレクトする」機能はなく、未認証なら401、権限不足なら403というステータスコードだけを返すのが標準の挙動。
  この項目は前者（Cookie認証スキーム、`AddCookie`）だけに関係する変更である点に注意。
- **JWT（JSON Web Token）とは**
  「このユーザーはログイン済みで、有効期限はいつまで」といった情報を、改ざんを検知できる形で1つの文字列に詰め込んだトークン。サーバーは秘密鍵で署名しており、受け取った側は同じ鍵で署名を検証することで「本物かどうか」を確認できる（本文自体は暗号化されておらず誰でも中身を読めるが、書き換えると署名が一致しなくなるので改ざんは検知できる）。
- **既知のAPIエンドポイント（`IApiEndpointMetadata`）とは**
  今回新設された「このエンドポイントはHTML画面ではなくAPIである」という目印（メタデータ）。`[ApiController]`が付いたMVCコントローラーや、JSONの読み書きをするMinimal API（`app.MapGet(...)`のような書き方）、`TypedResults`を返すエンドポイント、SignalRのエンドポイントには自動でこの目印が付く。

【説明】

以前は、`AddCookie`によるCookie認証を使っている場合、未認証・権限不足のリクエストは（XMLHttpRequestによる場合を除き）常にログインページ／アクセス拒否ページへの302リダイレクトになっていた。これは、そのリクエストがブラウザで人間が見ているHTMLページ宛てなのか、JavaScriptが叩いているJSON APIエンドポイント宛てなのかを区別せずに一律で適用されていた挙動である。

ところが、API宛てのリクエストに対して302リダイレクトを返すと都合が悪い。JSONを期待しているクライアント（`fetch`や`axios`など）からすると、302で返ってきたレスポンス先（ログインページのHTML）を誤ってパースしようとしてエラーになったり、そもそも「未認証だった」という事実がステータスコードとして素直に伝わらなかったりする。

.NET 10からは、上記の「既知のAPIエンドポイント」に対する未認証・権限不足のリクエストは、リダイレクトではなく単純に401（Unauthorized）・403（Forbidden）のステータスコードを返すように変更された。XMLHttpRequestは元々リダイレクトされない扱いだったので、そちらは変わらず401/403のまま。変更理由は公式ドキュメントにも「非常に要望が多かった」とある通り、「APIエンドポイントをログインページにリダイレクトするのは通常は意味がない」という自然な要望に応えたもの。

【放置したときの影響】

この変更が影響するのは「`AddCookie`でCookie認証を使っていて、かつ`[ApiController]`やMinimal APIなどの『既知のAPIエンドポイント』を保護している」場合のみ。該当する場合、今まで302リダイレクトを前提にコーディングしていたクライアント側のコード（例えば「302が返ってきたらログインページへ遷移する」という自前ロジック）が動かなくなり、代わりに401/403が返るようになる。

一方、`AddJwtBearer`（JWT Bearer認証）だけを使っている場合、この変更が扱っているCookie認証の「ログインリダイレクト機能」自体がそもそも存在しないため、この項目は無関係。

```csharp
// 影響を受けるパターン（AddCookieを使い、[ApiController]を保護している場合）
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => options.LoginPath = "/Account/Login");
// → .NET 9まで: 未ログインで[ApiController]のAPIを叩くと302 → /Account/Login
// → .NET 10から: 302ではなく401が返る
```

【プロジェクトでの調べ方】

`backend/DicomTool.Api`で使われている認証方式そのものを確認した。

1. `backend/DicomTool.Api/Program.cs`（129〜162行目）を確認すると、`builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)`となっており、認証方式は**JWT Bearer認証**である。`AddCookie`の呼び出しはリポジトリ全体を検索しても見つからなかった（`Grep`で`AddCookie`・`CookieAuthenticationEvents`・`RedirectToLogin`を検索し、ヒット0件）。
2. たしかにJWTトークンの受け渡し場所には`httpOnly` Cookieを使っている（同ファイル151〜160行目の`OnMessageReceived`で`AppConstants.AuthCookieName`という名前のCookieからトークン文字列を取り出している）。しかしこれは「JWT Bearer認証が、トークンの置き場所としてたまたまCookieを使っている」だけであり、この破壊的変更が対象とする「Cookie認証スキーム（`AddCookie`）」とは別物。JWT Bearer認証はもともとリダイレクトせず401/403を返す実装なので、今回の変更前後で挙動は変わらない。
3. GraphQLのMutation/Query側（`backend/DicomTool.Api/GraphQL/Mutation.cs`・`Query.cs`）で認可判定に失敗した場合も、HotChocolate（GraphQLサーバー）が独自にGraphQLのエラー形式でレスポンスを返す仕組みであり、ASP.NET Core標準のCookie認証リダイレクトの経路には乗らない。

以上より、**この破壊的変更は現時点の`backend/DicomTool.Api`には影響しない**（Cookie認証スキーム自体を使っていないため）。

【改修方法】

対応不要。ただし、今後もし管理画面などをRazor PagesやMVCのViewで作り、`AddCookie`を追加することがあれば、その際にこの変更を思い出す必要がある。もし「常にログインページへリダイレクトしたい」という昔ながらの挙動を維持したいAPIを新設する場合は、以下のように`OnRedirectToLogin`/`OnRedirectToAccessDenied`を明示的に上書きすればよい。

```csharp
// 将来Cookie認証を追加する場合、以前の「常にリダイレクト」の挙動を維持したいとき
builder.Services.AddAuthentication()
    .AddCookie(options =>
    {
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### WithOpenApi 拡張メソッドの廃止
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/breaking-changes/10/withopenapi-deprecated?view=aspnetcore-10.0

【前提知識】

- **OpenAPI（旧Swagger）とは**
  「このAPIにはどんなエンドポイントがあり、それぞれどんなリクエスト・レスポンスの形なのか」を、人間にもツールにも読める形式（JSON/YAML）で記述した仕様書。これがあれば、Swagger UIのような画面でAPIの一覧を見たり、そこからクライアントコードを自動生成したりできる。ASP.NET Coreでは`Microsoft.AspNetCore.OpenApi`パッケージの`AddOpenApi()`/`MapOpenApi()`で、このOpenAPI仕様書をアプリ自身に自動生成させることができる。
- **Minimal APIとは**
  `app.MapGet("/path", () => ...)`のように、コントローラークラスを作らずラムダ式1つでエンドポイントを定義する書き方（ASP.NET Core 6.0以降）。`WithOpenApi()`はこのMinimal APIのエンドポイント定義に「メソッドチェーン」でつなげて、そのエンドポイント固有のOpenAPIドキュメントの内容（概要・説明文など）を調整するための拡張メソッドだった。
- **このプロジェクトのAPI形式（GraphQL）との違い**
  `backend/DicomTool.Api`はMinimal APIやMVCコントローラーでエンドポイントを個別に定義するREST形式ではなく、**GraphQL**という別の設計のAPIを採用している。GraphQLは`/graphql`という単一のURLだけを公開し（`Program.cs`の`app.MapGraphQL()`）、どんなデータを取得・更新するかはリクエストの中身（クエリ文字列）で指定する方式。スキーマ（型定義）はHotChocolateというライブラリが管理しており、そもそもOpenAPI／Swaggerという「REST API向けの仕様記述形式」の対象にならない（GraphQLには`.graphql`スキーマファイルという別の仕組みがある）。

【説明】

以前は、Minimal APIのエンドポイントに`.WithOpenApi()`を呼び出すことで、そのエンドポイント個別のOpenAPIドキュメント（概要・説明・パラメータの追加情報など）をその場でカスタマイズできた。警告は何も出なかった。

.NET 10からは、この`WithOpenApi()`拡張メソッドを呼び出すとコンパイル時警告（`ASPDEPR002`）が出るようになった（呼び出し自体は引き続きコンパイル・実行できる＝いきなり壊れるわけではない）。理由は、同じことをする「組み込みのOpenAPIドキュメント生成パイプライン」（`AddOpenApiOperationTransformer`という、より柔軟な後継の仕組み）が用意されており、機能が重複しているため。APIを整理し、将来的な削除に向けて非推奨化した。

【放置したときの影響】

`backend/DicomTool.Api`はMinimal APIのエンドポイント自体を定義していない（後述の調査の通り）ため、コンパイル時にこの警告が出ることはなく、実害はない。

もし将来、Minimal APIエンドポイントを追加してOpenAPIドキュメントを整備する際に、古い情報（ネット上の.NET 8時代の記事等）を参考にして`WithOpenApi()`を使うコードを書いてしまうと、ビルド時に非推奨警告が出るようになる（コンパイルは通るので即座に壊れはしないが、放置するとビルドログが警告だらけになり、本当に重要な警告を見落とすリスクが増える）。

```csharp
// 今後Minimal APIを追加した場合、古い書き方をすると警告が出る例
app.MapGet("/weather", () => ...)
   .WithOpenApi();   // 警告 ASPDEPR002: WithOpenApi は非推奨です
```

【プロジェクトでの調べ方】

1. `backend/DicomTool.Api`全体を`WithOpenApi`・`AddOpenApi`・`MapOpenApi`で検索したが、いずれも0件（GraphQLの`AddGraphQLServer()`・`MapGraphQL()`のみが存在し、Minimal APIの`MapGet`/`MapPost`自体もこのプロジェクトには存在しない。`Grep`で確認済み）。
2. `backend/DicomTool.Api/DicomTool.Api.csproj`にも`Microsoft.AspNetCore.OpenApi`パッケージの参照はない。
3. 参考までに、リポジトリ内で`Microsoft.AspNetCore.OpenApi`や`Swashbuckle.AspNetCore`を参照しているのは`services/DicomTool.TrayApp`（トレイアプリ、REST的な別プロセス）のみで、`backend/DicomTool.Api`とは無関係。

以上より、**この破壊的変更は`backend/DicomTool.Api`には影響しない**（Minimal APIのOpenAPI関連機能を一切使っていないため）。

【改修方法】

対応不要。もし将来Minimal APIエンドポイントとそのOpenAPIドキュメントカスタマイズが必要になった場合は、最初から新しい書き方（`AddOpenApiOperationTransformer`）を使うようにする。

```csharp
// 改修前（非推奨警告が出る書き方。今後は使わない）
app.MapGet("/weather", () => ...)
   .WithOpenApi(operation =>
   {
       operation.Summary = "現在の天気予報を取得します。";
       return operation;
   });

// 改修後（推奨される書き方）
app.MapGet("/weather", () => ...)
   .AddOpenApiOperationTransformer((operation, context, ct) =>
   {
       operation.Summary = "現在の天気予報を取得します。";
       return Task.CompletedTask;
   });
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### TryHandleAsync が true を返すと、例外診断が抑制される
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/breaking-changes/10/exception-handler-diagnostics-suppressed?view=aspnetcore-10.0

【前提知識】

- **ミドルウェアと例外処理ミドルウェアとは**
  ASP.NET Coreのリクエスト処理は、`app.Use...()`で登録した「ミドルウェア」が数珠つなぎになったパイプラインを順番に通っていくイメージ。`app.UseExceptionHandler(...)`は、パイプラインの中で後続の処理（コントローラーの実行など）が例外を投げたときに、それをキャッチしてエラーレスポンス（500エラーページやJSONエラーなど）に変換してくれる、いわば「パイプライン全体の受け皿」となるミドルウェア。
- **`IExceptionHandler`とは**
  .NET 8で追加された、「特定の種類の例外はこう処理する」というロジックをクラスとして書ける仕組み。`app.UseExceptionHandler()`に対して複数の`IExceptionHandler`実装を登録しておくと、例外が起きるたびに順番に「これは自分が処理できる例外か」を聞かれ（`TryHandleAsync`メソッド）、`true`を返した実装が見つかった時点で「その例外は処理済み」として扱われる。
- **診断（ログ・メトリクス）とは**
  例外が起きたことをアプリの運用者が把握するための記録。具体的には、（1）`ILogger`へのログ出力、（2）`EventSource`という仕組みへのイベント書き込み、（3）`http.server.request.duration`という応答時間の指標（メトリクス）に「エラーだった」というタグを付ける、の3種類。これらは通常、Application Insightsやログ基盤（Grafana/Datadog等）で「エラー率」を監視する際の元データになる。

【説明】

以前は、`IExceptionHandler.TryHandleAsync`が`true`を返して「この例外は自分（アプリ側）が処理した」と申告しても、ASP.NET Coreの例外処理ミドルウェアは変わらず上記の診断（ログ出力・イベント発行・メトリクスへのタグ付け）を記録していた。つまり「アプリ側で処理済みのはずなのに、裏側では『未処理のエラーが起きた』という扱いのログやメトリクスが残ってしまう」という状態だった。

.NET 10からは、`TryHandleAsync`が`true`を返した場合、既定では診断が記録されなくなった。理由は、「アプリ側が意図的に処理した例外」まで「予期しないエラー」としてテレメトリに記録されると、実際の異常検知（アラート）のノイズになる、というフィードバックが多かったため。「自分で処理を宣言したなら、それはもうエラーとして記録しない」という、直感に合った挙動に変更された。

【放置したときの影響】

`backend/DicomTool.Api`は`UseExceptionHandler`も`IExceptionHandler`実装も一切使っていない（後述の調査の通り）ため、この変更の対象コードがそもそも存在せず、直接の影響はない。

ただし一般論として、もし将来`IExceptionHandler`を導入して「エラーはこのクラスでまとめてハンドリングし、独自の形式でログも出す」という設計にした場合、今まで（.NET 9まで）はASP.NET Core側が自動でログ・メトリクスを二重に記録してくれていたが、.NET 10からはそれが起きなくなる。「エラー監視ダッシュボードで急にエラー件数が0件になった（実際にはエラーは起きているが、`IExceptionHandler`が処理済みとして記録を抑制している）」という事象に気づかず放置すると、実運用で障害に気づけなくなるリスクがある。

```csharp
// 例：IExceptionHandlerを実装した場合
public class MyExceptionHandler : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        // 何らかの処理をしてエラーレスポンスを返す
        context.Response.StatusCode = 500;
        return ValueTask.FromResult(true); // .NET 10からはこれ以降、自動ログ等が出なくなる
    }
}
```

【プロジェクトでの調べ方】

`backend/DicomTool.Api`を`IExceptionHandler`・`UseExceptionHandler`・`ExceptionHandlerOptions`で検索したが、いずれも0件だった。`Program.cs`を確認しても、例外処理ミドルウェア（`app.UseExceptionHandler(...)`）自体が登録されていない。

代わりにこのプロジェクトでは、GraphQL（HotChocolate）が独自のエラーハンドリングを持っており、Query/Mutation内で投げた例外はGraphQLレスポンスの`errors`フィールドに変換される（ASP.NET Core標準の例外処理ミドルウェアの経路を通らない）。したがって**この破壊的変更は`backend/DicomTool.Api`には影響しない**。

【改修方法】

対応不要。ただし、もし将来（GraphQL層とは別に）静的ファイル配信やヘルスチェックなど、GraphQL以外のエンドポイント向けに`UseExceptionHandler`＋`IExceptionHandler`を導入することがあれば、テレメトリ（ログ監視）に処理済み例外も記録し続けたいかどうかを意識して、必要なら以下のように明示的にオプトインする。

```csharp
// 処理済みの例外でも引き続きログ・メトリクスを記録したい場合
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    SuppressDiagnosticsCallback = context => false,
});
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### IActionContextAccessor と ActionContextAccessor は廃止されました
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/breaking-changes/10/iactioncontextaccessor-obsolete?view=aspnetcore-10.0

【前提知識】

- **MVCの`ActionContext`とは**
  ASP.NET Core MVC（コントローラー・アクションメソッドで作るAPI）が、今処理中のアクションメソッドに関する情報（どのコントローラー・どのアクションが呼ばれたか、そのメタデータ等）をまとめて持っているオブジェクト。通常はコントローラーの中でしか直接触れないが、`IActionContextAccessor`というサービスをDI（依存性の注入。後述）で受け取れば、コントローラー以外の任意のサービスクラスからも「今処理中のアクションの情報」にアクセスできた。
- **`HttpContext`と`IHttpContextAccessor`とは**
  `ActionContext`よりもさらに一段低いレイヤーの、「今処理中のHTTPリクエスト・レスポンスそのもの」を表すオブジェクトが`HttpContext`。MVCに限らずASP.NET Core全体（Minimal API、GraphQL含む）で共通して使える。これもコントローラーや専用の場所以外から使いたい場合は`IHttpContextAccessor`というサービスをDIで受け取る。
- **DI（依存性の注入）とは**
  「このクラスが動くのに必要な部品（サービス）を、自分でnewして作るのではなく、コンストラクターの引数として外から渡してもらう」という設計パターン。ASP.NET Coreでは`builder.Services.AddXxx()`で「この型が必要になったらこう作る」というレシピをあらかじめ登録しておき（`backend/DicomTool.Api/Program.cs`166行目の`builder.Services.AddHttpContextAccessor()`はまさにこれ）、あとはコンストラクターにその型を書くだけで自動的にインスタンスが渡ってくる仕組み。
- **エンドポイントルーティングとは**
  ASP.NET Core 3.0以降の標準的なルーティングの仕組み。「どのURLがどの処理につながるか」という情報（メタデータ含む）を、リクエスト処理の早い段階で`HttpContext.GetEndpoint()`から直接取得できるようになった。これにより、`IActionContextAccessor`のような「MVC専用の別の仕組み」を用意しなくても、`HttpContext`経由で必要な情報にアクセスできるようになった。

【説明】

以前は、コントローラー以外のサービスクラスからアクションの情報（アクション記述子など）を取得したい場合、`IActionContextAccessor`をコンストラクターインジェクションで受け取り、`_actionContextAccessor.ActionContext?.ActionDescriptor`のようにアクセスするのが定番のやり方だった。

.NET 10からは、`IActionContextAccessor`／`ActionContextAccessor`を使うとコンパイル時警告（`ASPDEPR006`）が出るようになった。理由は、エンドポイントルーティングの導入によって、より汎用的な`IHttpContextAccessor`と`HttpContext.GetEndpoint()`の組み合わせで同じ情報が取得できるようになり、MVC専用の`IActionContextAccessor`という仕組みが不要になったため。

【放置したときの影響】

`backend/DicomTool.Api`はそもそもMVCコントローラー（`[ApiController]`付きクラス）を1つも持っていない（GraphQLのみ）ため、`IActionContextAccessor`が意味を持つ場面自体が存在しない。実際にコード中での使用も0件であり、影響はない。

一般論として、もし`[ApiController]`ベースのAPIで`IActionContextAccessor`を使っているコードを放置すると、ビルド時に警告が出続けるだけで.NET 10の間は動作は変わらない（将来のバージョンで完全に削除される可能性がある点には注意）。

【プロジェクトでの調べ方】

`backend/DicomTool.Api`を`IActionContextAccessor`・`ActionContextAccessor`で検索したが0件。そもそも`[ApiController]`属性を使ったMVCコントローラークラス自体がこのプロジェクトに存在しない（`Grep`で`ApiController`を検索してもヒットなし）。

一方、`Program.cs`の166行目を見ると、このプロジェクトはすでに推奨後継である`IHttpContextAccessor`を`builder.Services.AddHttpContextAccessor()`で登録済みで、GraphQLの`Mutation.cs`側でHttpContext（Cookieの読み書きなど）にアクセスするために実際に使われている。つまり、**このプロジェクトは元々MVC形式のAPIを採用していないため今回の変更対象コードが存在せず、しかも代替手段である`IHttpContextAccessor`を既に使っている**という状態であり、対応不要。

【改修方法】

対応不要。参考までに、もし他のプロジェクトで`IActionContextAccessor`を使ったコードに遭遇した場合の書き換え例は以下の通り。

```csharp
// 改修前
public class MyService
{
    private readonly IActionContextAccessor _actionContextAccessor;
    public MyService(IActionContextAccessor actionContextAccessor)
        => _actionContextAccessor = actionContextAccessor;

    public void DoSomething()
    {
        var actionDescriptor = _actionContextAccessor.ActionContext?.ActionDescriptor;
    }
}

// 改修後
public class MyService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    public MyService(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public void DoSomething()
    {
        var endpoint = _httpContextAccessor.HttpContext?.GetEndpoint();
        var actionDescriptor = endpoint?.Metadata.GetMetadata<ActionDescriptor>();
    }
}
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### IncludeOpenAPIAnalyzers プロパティと MVC API アナライザーは非推奨です
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/breaking-changes/10/openapi-analyzers-deprecated?view=aspnetcore-10.0

【前提知識】

- **アナライザー（Analyzer）とは**
  コードを実行せずにソースコードを静的に解析し、問題があればビルド時に警告・エラーを出してくれる仕組み（Visual Studioの波線もこれ）。「MVC APIアナライザー」は、コントローラーのアクションメソッドに付けた`[ProducesResponseType(200)]`のような「このアクションはこのステータスコードとこの型を返します」という宣言と、実際にメソッドが`return Ok(...)`や`return NotFound()`で返している中身が食い違っていないかをチェックしてくれるアナライザー。
- **`IncludeOpenAPIAnalyzers`プロパティとは**
  `.csproj`ファイルに`<IncludeOpenAPIAnalyzers>true</IncludeOpenAPIAnalyzers>`と書くことで、上記のMVC APIアナライザーを有効化するスイッチ。
- **`TypedResults`とは**
  Minimal API（およびASP.NET Core 10からはコントローラーベースのAPIでも）で使える、戻り値の型そのもので「起こりうるすべてのレスポンスパターン」を表現する仕組み。例えば`Task<Results<Ok<Product>, NotFound>>`という戻り値の型を書けば、「200番でProduct型を返すか、404番を返すかのどちらか」ということがコンパイラに伝わり、コンパイラ自身がチェックしてくれる。これにより「宣言（属性）と実装（returnの中身）がズレる」という事故自体が構造的に起きなくなるため、専用のアナライザーが不要になった。

【説明】

以前は、Web SDKプロジェクトの`.csproj`に`<IncludeOpenAPIAnalyzers>true</IncludeOpenAPIAnalyzers>`と書くと、警告なしでMVC APIアナライザーを有効化できた。

.NET 10からは、このプロパティを`true`に設定するとビルド時に非推奨警告（`ASPDEPR007`）が出るようになった（アナライザー自体は引き続き動作する）。理由は、Minimal APIや`TypedResults`パターンの普及により、そもそも「戻り値の型自体がドキュメントを兼ねる」型安全な書き方が主流になり、専用アナライザーによる後付けチェックが冗長になったため。

【放置したときの影響】

`backend/DicomTool.Api`の`.csproj`には`IncludeOpenAPIAnalyzers`の指定自体がなく、そもそも`[ApiController]`ベースのコントローラーも存在しない（GraphQLのみ）ため、このプロパティを有効化する動機自体がない。したがって影響はない。

一般論として、MVCベースのAPIプロジェクトでこのプロパティを有効にしたまま放置すると、ビルドのたびに非推奨警告が出続ける（実害はないが、警告が積み重なるとビルドログが見づらくなる）。

【プロジェクトでの調べ方】

`backend/DicomTool.Api/DicomTool.Api.csproj`をはじめリポジトリ全体を`IncludeOpenAPIAnalyzers`で検索したが0件だった。前述の通り、このプロジェクトはMVCコントローラーではなくGraphQL（HotChocolate）を採用しており、`[ProducesResponseType]`のようなMVC系の属性自体を1つも使っていない（`Grep`で`ApiController`・`TypedResults`・`ProducesResponseType`を検索してもヒットなし）。**この破壊的変更は`backend/DicomTool.Api`には無関係**。

【改修方法】

対応不要。もし他プロジェクトで`IncludeOpenAPIAnalyzers`を使っている場合は、`.csproj`から該当行を削除し、可能であれば`TypedResults`パターンへの移行を検討する。

```xml
<!-- 改修前 -->
<PropertyGroup>
  <IncludeOpenAPIAnalyzers>true</IncludeOpenAPIAnalyzers>
</PropertyGroup>
```

```xml
<!-- 改修後：単に削除するだけでよい -->
<PropertyGroup>
</PropertyGroup>
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### IPNetwork と ForwardedHeadersOptions.KnownNetworks は廃止されました
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/breaking-changes/10/ipnetwork-knownnetworks-obsolete?view=aspnetcore-10.0

【前提知識】

- **リバースプロキシと転送ヘッダー（Forwarded Headers）とは**
  実際のWebサービス運用では、クライアント（ブラウザ）とASP.NET Coreアプリの間に、Nginxやロードバランサーのような「リバースプロキシ」を1台挟むことが多い。この構成だと、ASP.NET Coreアプリから見た接続元IPアドレスは常に「プロキシ自身のIPアドレス」になってしまい、本来のクライアントのIPアドレスが分からなくなる。そこでプロキシは`X-Forwarded-For`（本来のクライアントIP）や`X-Forwarded-Proto`（本来はHTTPSだったか等）というHTTPヘッダーに元の情報を詰めて転送する。ASP.NET Core側は`app.UseForwardedHeaders(...)`というミドルウェアでこれらのヘッダーを読み取り、`HttpContext.Connection.RemoteIpAddress`などを「本来の値」に復元する。
- **`KnownNetworks`（信頼するネットワーク範囲）とは**
  `X-Forwarded-For`ヘッダーは、リクエストを送る側（クライアント）が自由に書き換えられる、ただのHTTPヘッダーに過ぎない。もし何のチェックもなく信じてしまうと、悪意のある第三者が「自分はこのIPアドレスです」と偽って`X-Forwarded-For`ヘッダーを送りつけ、IPアドレスによるアクセス制限を回避できてしまう（なりすまし）。これを防ぐため、「このIPアドレス範囲（＝信頼できる自社のリバースプロキシが動いているネットワーク）から来たヘッダーだけ信用する」という許可リストを`KnownNetworks`（あるいは`KnownProxies`）に設定する。
- **CIDR表記とIPNetwork型とは**
  「IPアドレスの範囲」を`192.168.1.0/24`のように表す記法をCIDR表記と呼ぶ。`Microsoft.AspNetCore.HttpOverrides.IPNetwork`は、ASP.NET Core独自に用意されていたこのCIDR範囲を表す型。今回、.NET本体（BCL、Base Class Library）側に`System.Net.IPNetwork`という同等の型が標準搭載されたため、ASP.NET Core独自版は不要になった。

【説明】

以前は、転送ヘッダーミドルウェアで信頼するネットワーク範囲を指定する際、ASP.NET Core独自の`Microsoft.AspNetCore.HttpOverrides.IPNetwork`型と、`ForwardedHeadersOptions.KnownNetworks`プロパティを使っていた。

.NET 10からは、これらを使うとコンパイル時警告（`ASPDEPR005`）が出るようになった。代わりに.NET本体に標準搭載された`System.Net.IPNetwork`型と、新しい`KnownIPNetworks`プロパティを使うことが推奨される。理由は単純で、「ASP.NET Core独自に用意していた型」を、後から.NET本体に入った「標準の型」に置き換えて一本化するため（車輪の再発明の解消）。

【放置したときの影響】

`backend/DicomTool.Api`の`Program.cs`を確認したが、`app.UseForwardedHeaders(...)`自体を呼び出していない。つまりリバースプロキシ越しの運用を前提にした転送ヘッダーの処理を、そもそもこのプロジェクトは行っていない（`ConfigureKestrel`でKestrel自体のリクエストサイズ上限は調整しているが、これはリバースプロキシとは無関係）。したがって現時点で影響はない。

ただし、CLAUDE.mdにも記載がある通りこのプロジェクトは複数の関係者間で開発マシン・VM間のDICOM通信を扱っており、将来的に本番運用でNginx等のリバースプロキシを前段に置く構成に変更した場合は、この転送ヘッダーミドルウェアの導入が必要になる可能性がある。そのタイミングで、もし当時のネット記事等（.NET 9以前の情報）を参考にして`Microsoft.AspNetCore.HttpOverrides.IPNetwork`を使ってしまうと、非推奨警告が出るようになる。

```csharp
// 将来もし転送ヘッダーミドルウェアを追加する場合、古い書き方をすると警告が出る例
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    KnownNetworks = { new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Loopback, 8) } // 警告 ASPDEPR005
});
```

【プロジェクトでの調べ方】

リポジトリ全体を`ForwardedHeaders`・`IPNetwork`・`KnownNetworks`・`KnownProxies`で検索したが、いずれも0件だった。`backend/DicomTool.Api/Program.cs`にも`UseForwardedHeaders`の呼び出しはない。**この破壊的変更は現時点の`backend/DicomTool.Api`には影響しない**（転送ヘッダーミドルウェア自体を使っていないため）。

【改修方法】

対応不要。もし今後リバースプロキシ構成に変更し、転送ヘッダーミドルウェアを新規に導入する場合は、最初から新しいAPIを使う。

```csharp
// 今後導入する場合の推奨の書き方
using System.Net;

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownIPNetworks = { IPNetwork.Parse("192.168.1.0/24") }, // System.Net.IPNetwork
});
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### Microsoft.Extensions.ApiDescription.Client パッケージの非推奨
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/breaking-changes/10/apidescription-client-deprecated?view=aspnetcore-10.0

【前提知識】

- **APIクライアントの自動生成とは**
  サーバー側が公開しているOpenAPI仕様書（前述）を元に、「このAPIを呼び出すためのC#クラス（型付きのHTTPクライアント）」をビルド時に自動生成してくれるツール群がある。手書きで`HttpClient`を使ってJSONを組み立てる代わりに、生成されたクラスのメソッドを呼ぶだけでAPIを叩ける、というのが狙い。
- **`Microsoft.Extensions.ApiDescription.Client`パッケージとは**
  上記の自動生成を、`.csproj`に`<OpenApiReference Include="swagger.json" />`のような項目を書くだけでビルドの一部として実行できるようにする、いわば「MSBuildとコード生成ツールをつなぐ橋渡し役」のパッケージ。裏側では実際にはNSwagやKiota等の個別のコード生成ツールを呼び出していた。
- **このプロジェクトのAPI呼び出し方法との違い**
  `backend/DicomTool.Api`はGraphQLを採用しており、フロントエンド側はOpenAPIベースのRESTクライアント自動生成ではなく、GraphQL用の別の仕組み（GraphQLクエリを直接文字列で書く、またはGraphQL専用のコード生成ツールを使う）でAPIを呼び出す。今回のパッケージはあくまで「OpenAPI（REST）ベースのクライアント生成」の話であり、対象範囲が異なる。

【説明】

以前は、`<PackageReference Include="Microsoft.Extensions.ApiDescription.Client" ... />`と`<OpenApiReference Include="swagger.json" />`を`.csproj`に追加する（あるいは`dotnet openapi`コマンドを使う）ことで、ビルド時にOpenAPI仕様書から型付きクライアントコードを自動生成できた。

.NET 10からは、このパッケージ自体が非推奨になった。参照しているプロジェクトはビルド時に警告を受け取るようになる。理由として公式ドキュメントは、(1) パッケージの更新・保守がここ数年最小限にとどまっていたこと、(2) 特定のコード生成ツールに強く結びついた抽象化になっており、他のジェネレーターへの対応がうまくスケールしなかったこと（結局各ジェネレーターが独自のCLI/設定を持っており、MSBuildの中間層がかえって冗長だったこと）を挙げている。

【放置したときの影響】

`backend/DicomTool.Api`はこのパッケージを参照しておらず、そもそもAPI自体がOpenAPI仕様書を公開する形式のREST APIではなくGraphQLであるため、この変更の対象外。

一般論として、REST API側でこのパッケージを使ってクライアントを自動生成しているプロジェクトが放置した場合、当面はビルドできるが警告が出続け、将来のバージョンで完全に廃止された際に急にビルドが壊れるリスクがある。

【プロジェクトでの調べ方】

リポジトリ全体を`ApiDescription.Client`・`OpenApiReference`・`dotnet openapi`・`OpenApiProjectReference`で検索したが、いずれも0件だった。フロントエンド側（`frontend/`配下）の`package.json`も確認したが、GraphQL用のコード生成ツール（graphql-codegen等）やGraphQLクライアントライブラリの明確な痕跡は見当たらなかった（フロントエンドは別のフロントエンド技術スタックの調査範囲になるため、ここでは深追いしていない）。少なくとも`backend/DicomTool.Api`自身が「OpenAPIベースのクライアント自動生成の起点」になっている形跡はない。**この破壊的変更は`backend/DicomTool.Api`には影響しない**。

【改修方法】

対応不要。もし今後REST形式のAPIを別途追加してクライアント自動生成をしたくなった場合は、最初から後継ツール（NSwag、Kiota、OpenAPI Generatorのいずれか）を直接使う構成にする。

```xml
<!-- 改修前（非推奨） -->
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.ApiDescription.Client" Version="8.0.0" />
</ItemGroup>
<ItemGroup>
  <OpenApiReference Include="swagger.json" />
</ItemGroup>
```

```bash
# 改修後の例：Kiotaを個別ツールとしてインストールし、直接コマンドでクライアントを生成する
dotnet tool install -g Microsoft.OpenApi.Kiota
kiota generate --openapi swagger.json --language CSharp --output ./Generated
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### Razor ランタイムコンパイルは廃止されました
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/breaking-changes/10/razor-runtime-compilation-obsolete?view=aspnetcore-10.0

【前提知識】

- **Razor（`.cshtml`ファイル）とは**
  ASP.NET Core MVCやRazor Pagesで、サーバー側がHTML画面を生成するときに使うテンプレート言語・ファイル形式（拡張子`.cshtml`）。HTMLの中にC#のコードを埋め込んで書ける。`backend/DicomTool.Api`のようなAPI専用プロジェクト（画面を持たず、GraphQL/JSONだけを返す）では通常使わない。
- **ビルド時コンパイルとランタイムコンパイルの違い**
  `.cshtml`ファイルは最終的にC#のクラスにコンパイルされてから実行される。通常は「ビルド時（`dotnet build`のタイミング）」にまとめてコンパイルされる。一方「ランタイムコンパイル」を有効にすると、アプリを実行したまま`.cshtml`ファイルを編集した際に、アプリを再起動せずその場で再コンパイルして変更を反映できた（開発中の画面調整を素早く確認したいときに使われていた機能）。
- **ホットリロード（Hot Reload）とは**
  ランタイムコンパイルの後継として推奨されている、より新しい仕組み。`dotnet watch`などと組み合わせて、C#のコード自体を含めてより広い範囲の変更をアプリ実行中にその場で反映できる（Visual Studioのデバッグ実行中にコードを書き換えるとすぐ反映される、あの機能）。

【説明】

以前は、`AddRazorRuntimeCompilation()`をDIコンテナに登録することで、Razorランタイムコンパイル（`.cshtml`をアプリ実行中に再コンパイルする機能）を有効化できた。

.NET 10からは、これに関連するAPI（`AddRazorRuntimeCompilation`など）を使うとコンパイル時警告（`ASPDEPR003`）が出るようになった。理由は、ここ数年、開発中の即時反映手段としてはホットリロードが推奨されており、ランタイムコンパイル自体は本番運用にも推奨されない（本番でファイルをその場で再コンパイルするのはパフォーマンス・セキュリティの両面で望ましくない）ため、新機能の投資対象からも外れていることを明確にする狙い。

【放置したときの影響】

`backend/DicomTool.Api`はAPI専用（GraphQL）プロジェクトであり、`.cshtml`ファイルはリポジトリ全体を検索しても1つも存在しない。したがってRazorランタイムコンパイル自体を使う場面がなく、影響はない。

一般論として、Razor Pages/MVCで画面を作るプロジェクトが本番環境でランタイムコンパイルを有効にしたまま放置すると、非推奨警告が出るだけでなく、そもそも本番でファイルの動的再コンパイルを許してしまうこと自体が推奨されないパフォーマンス・セキュリティ上のリスクを抱え続けることになる。

【プロジェクトでの調べ方】

リポジトリ全体を`AddRazorRuntimeCompilation`・`RazorRuntimeCompilation`で検索したが0件、`.cshtml`ファイルも`Glob`で検索して0件だった。`backend/DicomTool.Api`はビュー（画面）を持たないAPI・GraphQL専用プロジェクトであることを`Program.cs`の内容からも確認済み。**この破壊的変更は`backend/DicomTool.Api`には無関係**。

【改修方法】

対応不要。もし将来、管理画面などをRazor Pages/MVCで追加開発することになった場合は、開発中の即時反映にはランタイムコンパイルではなくホットリロード（`dotnet watch run`）を使う。

```csharp
// 改修前（非推奨）：開発時に.cshtmlの変更を即時反映するためランタイムコンパイルを使っていた
builder.Services.AddControllersWithViews()
    .AddRazorRuntimeCompilation();
```

```bash
# 改修後：ランタイムコンパイルの登録自体を削除し、開発時はdotnet watchを使う
dotnet watch run
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### WebHostBuilder、IWebHost、および WebHost は廃止されています
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/breaking-changes/10/webhostbuilder-deprecated?view=aspnetcore-10.0

【前提知識】

- **ASP.NET Coreの「ホスティングモデル」の歴史**
  「ホスト」とは、アプリを実際に起動し、Kestrel（内蔵Webサーバー）を立ち上げ、DIコンテナやログ基盤などのインフラを組み立てる土台部分のこと。ASP.NET Coreはバージョンを重ねるごとにこの土台の作り方（API）を変えてきた。
  1. **`WebHostBuilder`/`IWebHost`/`WebHost`**（ASP.NET Core 1.0〜） … 最初期の書き方。`Startup`クラスを使う`.UseStartup<Startup>()`スタイルとセットで使われていた、Web専用のホスト構築API。
  2. **`HostBuilder`（汎用ホスト）**（ASP.NET Core 3.0〜） … Web以外（バックグラウンドワーカーサービス等）でも同じ仕組みでホストを組み立てられるよう一般化されたAPI。Webアプリの場合は`ConfigureWebHost(...)`でその中にWeb向けの設定を差し込む。
  3. **`WebApplicationBuilder`/`WebApplication`**（ASP.NET Core 6.0〜、いわゆる最小ホスティングモデル） … `var builder = WebApplication.CreateBuilder(args);`から始まる、現在推奨されている最もシンプルな書き方。`backend/DicomTool.Api/Program.cs`の1行目`var builder = WebApplication.CreateBuilder(args);`はまさにこれ。
  つまり今回廃止予定になったのは（1）の最も古い世代のAPIであり、このプロジェクトが実際に使っているのは（3）の最新世代。
- **`IWebHostBuilder`インターフェースとの違い（紛らわしいので注意）**
  名前がよく似ているが、今回非推奨になったのは**具体的なクラス**である`WebHostBuilder`（および`IWebHost`、`WebHost`という静的ヘルパークラス）。一方、統合テストで使う`WebApplicationFactory<T>.ConfigureWebHost(IWebHostBuilder builder)`メソッドが受け取る`IWebHostBuilder`は**インターフェース**であり、これは今回の非推奨の対象に含まれていない（汎用ホスト・最小ホスティングモデルの両方から今も使われ続けている現役の型）。名前が似ているだけで別物なので注意。

【説明】

以前は、`WebHostBuilder`クラスを`new WebHostBuilder().UseKestrel().UseStartup<Startup>()...`のように使ってWebホストを構築・起動でき、警告は出なかった。

.NET 10からは、`WebHostBuilder`を使うと診断ID`ASPDEPR004`の警告が、`IWebHost`または`WebHost`（静的クラス）を使うと診断ID`ASPDEPR008`の警告が、それぞれコンパイル時に出るようになった。理由は、`WebHostBuilder`はASP.NET Core 3.0の時点で汎用ホスト（`HostBuilder`）に置き換えられ、さらにASP.NET Core 6.0で`WebApplicationBuilder`（最小ホスティングモデル）が登場して以降、今後の新機能investment（投資）はすべてこの新しいホスティングモデル側に向けられており、最も古い世代のAPIをここで整理する、という位置づけ。

【放置したときの影響】

`backend/DicomTool.Api`の`Program.cs`はすでに最新の`WebApplicationBuilder`/`WebApplication`（`WebApplication.CreateBuilder(args)`〜`app.Run()`）で書かれており、廃止対象の`WebHostBuilder`・`IWebHost`・`WebHost`クラスはどこにも使われていない。したがって影響はない。

なお、統合テストプロジェクト`backend/DicomTool.Api.Tests`では`WebApplicationFactory<Program>`を継承し、`ConfigureWebHost(IWebHostBuilder builder)`をオーバーライドしているが、前述の通りこの`IWebHostBuilder`は今回の非推奨対象である`WebHostBuilder`クラスとは別物（インターフェースであり非推奨になっていない）ため、これも問題ない。

【プロジェクトでの調べ方】

リポジトリ全体を`WebHostBuilder`・`IWebHost`・`new WebHost`で検索したところ、2件ヒットしたが、いずれも今回の非推奨対象とは異なるものだった。

- `backend/DicomTool.Api.Tests/Infrastructure/DicomToolWebApplicationFactory.cs`157行目：`protected override void ConfigureWebHost(IWebHostBuilder builder)` — `WebApplicationFactory<T>`が提供する`IWebHostBuilder`**インターフェース**への準拠であり、非推奨の`WebHostBuilder`**クラス**ではない。
- `services/DicomTool.TrayApp/Program.cs`10行目：`using Microsoft.AspNetCore.Hosting; // WebHostBuilderExtensions.UseUrls 拡張メソッド用` — こちらもコメントで拡張メソッドの出どころを説明しているだけで、`WebHostBuilder`クラス自体をnewしているわけではない。

`backend/DicomTool.Api/Program.cs`自体は1行目から`WebApplication.CreateBuilder(args)`で始まっており、最新の最小ホスティングモデルを使用している。**この破壊的変更は`backend/DicomTool.Api`には影響しない**。

【改修方法】

対応不要。すでに推奨されている`WebApplicationBuilder`/`WebApplication`スタイルで書かれている。参考までに、もし仮に古い`WebHostBuilder`スタイルのコードに遭遇した場合の移行例は以下の通り。

```csharp
// 改修前（非推奨）
var hostBuilder = new WebHostBuilder()
    .UseContentRoot(Directory.GetCurrentDirectory())
    .UseStartup<Startup>()
    .UseKestrel();
var testServer = new TestServer(hostBuilder);
```

```csharp
// 改修後（汎用ホストを使う場合）
using var host = new HostBuilder()
    .ConfigureWebHost(webHostBuilder =>
    {
        webHostBuilder
            .UseTestServer()
            .UseContentRoot(Directory.GetCurrentDirectory())
            .UseStartup<Startup>()
            .UseKestrel();
    })
    .Build();
await host.StartAsync();
var testServer = host.GetTestServer();

// 新規アプリの場合は、backend/DicomTool.Api/Program.cs のように
// WebApplication.CreateBuilder(args) から始める最小ホスティングモデルを使うのがさらに推奨される。
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）
