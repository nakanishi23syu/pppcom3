# .NET 8→9への移行
## パッケージ参照の更新より後の手順

### UseStaticFiles を MapStaticAssets に置き換える
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/migration/80-to-90?view=aspnetcore-10.0&tabs=visual-studio

【前提知識】

- **静的ファイル配信（Static Files）とは**
  ASP.NET Coreのアプリが、CSS・JavaScript・画像などの「サーバー側で処理せず、そのままバイト列を返すだけのファイル」をHTTP経由で配信する機能。従来は`app.UseStaticFiles()`という1行をミドルウェアパイプラインに追加するだけで、既定では`wwwroot`フォルダの中身がそのままURLとして公開される。
- **フィンガープリント（fingerprinting）とは**
  ファイル名に内容のハッシュ値を埋め込むこと（例：`site.css` → `site.a1b2c3d4.css`）。ファイルの中身が変わればファイル名も変わるので、ブラウザに「これは新しいファイルだ」と確実に伝えられ、`Cache-Control: max-age=31536000, immutable`のような強いキャッシュ設定を安全に使える（中身が変わらない限りファイル名も変わらないので、ブラウザは何年でもキャッシュしてよい）。
- **`wwwroot`とビルド時に既知の静的アセットとは**
  `MapStaticAssets`は、プロジェクトの`wwwroot`フォルダのように「ビルド時点でファイルの一覧が確定している」静的ファイル群を対象にした仕組み。ビルド・発行のタイミングでそれらのファイルを解析し、フィンガープリント付与や事前圧縮（gzip/brotli）をあらかじめ済ませておく。これに対し、実行時に初めて存在が分かるファイル（アップロードされた画像など）は対象にできない。

【説明】

.NET 9で新しく追加された`MapStaticAssets`は、`UseStaticFiles`の後継として位置づけられる最適化されたAPI。両者の大きな違いは次の通り。

- `UseStaticFiles`：ミドルウェアとしてリクエストのたびに`wwwroot`（や指定したフォルダ）からファイルを探して返す。ファイルの一覧は実行時に動的に解決される。
- `MapStaticAssets`：**ビルド・発行時**に静的ファイルを解析し、フィンガープリント付きファイル名の生成・gzip/brotli形式への事前圧縮・適切なキャッシュヘッダーの決定までを済ませてしまい、実行時はエンドポイントルーティングの一部として配信するだけにする。結果として実行時のオーバーヘッドが減り、キャッシュも効きやすくなる。

MVC・Razor Pagesアプリでは`app.MapStaticAssets()`を呼んだ上で、`.MapRazorPages()`や`.MapControllerRoute()`の戻り値に`.WithStaticAssets()`を連結する必要がある。また、フィンガープリント済みのファイル名をHTML側で解決するために、スクリプト・リンクタグヘルパーの自動解決や、JavaScriptのモジュールインポート用に`importmap`の追加が必要になる場合がある（Blazorなら`<ImportMap />`、MVC/Razor Pagesなら`<script type="importmap"></script>`）。

なお、`UseStaticFiles`自体が.NET 9で廃止されたわけではなく、引き続き動作する。`MapStaticAssets`はあくまで「`wwwroot`のようなビルド時に固定された静的ファイル群を配信する場合」の新しい最適化手段であり、実行時に決まる任意のフォルダを配信するような用途（後述）はそもそも対象外。

【放置したときの影響】

本プロジェクト（`backend/DicomTool.Api`）で`UseStaticFiles`の利用箇所をGrepで確認したところ、`Program.cs`に1箇所だけ以下のような呼び出しがあった。

```csharp
var dicomStorageFullPath = StoragePaths.ResolveStoragePath(builder.Environment.ContentRootPath);
Directory.CreateDirectory(dicomStorageFullPath);
var dicomContentTypeProvider = new FileExtensionContentTypeProvider();
dicomContentTypeProvider.Mappings[".dcm"] = "application/dicom";
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(dicomStorageFullPath),
    RequestPath = "/dicom-files",
    ContentTypeProvider = dicomContentTypeProvider,
});
```

これは`wwwroot`配下のCSS/JSのようなビルド時に確定した静的アセットではなく、**実行時に`ContentRootPath`から計算されるDICOMファイル保存先フォルダ**を、実行時に決まる`PhysicalFileProvider`で配信している（DICOMファイルは検査データが保存されるたびに増えていく、実行時にしか存在が分からないファイル群）。加えて`.dcm`拡張子用の独自`ContentTypeProvider`も指定している。

`MapStaticAssets`は「ビルド・発行時に静的ファイルの一覧を解析して最適化する」仕組みのため、このような実行時にのみ存在が分かる動的なフォルダの配信には対応していない。したがって、**この箇所は`MapStaticAssets`への置き換え対象ではなく、そのまま`UseStaticFiles`を使い続けるのが正しい**。また、本プロジェクトには`wwwroot`フォルダやMVC/Razor Pagesの仕組み自体が存在しない（GraphQL API中心の構成）ため、`MapStaticAssets`が想定する「典型的なユースケース」自体が今のところ存在しない。

以上より、この項目を放置しても実害はない。誤って機械的に`UseStaticFiles`を`MapStaticAssets`に置き換えてしまうと、DICOMファイルの配信自体が壊れる（`PhysicalFileProvider`や`RequestPath`、独自`ContentTypeProvider`を指定する手段が`MapStaticAssets`には無い）ため、むしろ「置き換えない」ことが正しい判断になる。

【プロジェクトでの調べ方】

1. `UseStaticFiles`という文字列でリポジトリ全体をGrepする。
   - 本プロジェクトでは`backend/DicomTool.Api/Program.cs`の1箇所のみヒットした。
2. ヒットした箇所が「`wwwroot`のような固定の静的アセットフォルダ」を配信しているのか、「実行時に決まる動的なフォルダ」を配信しているのかを確認する。
   - 今回は`StoragePaths.ResolveStoragePath(...)`という関数呼び出しでフォルダパスを実行時に計算しており、後者に該当すると判断した。
3. `wwwroot`フォルダがプロジェクトに存在するか、`AddRazorPages`/`AddControllersWithViews`/`AddMvc`のような呼び出しがあるかを確認する。
   - 本プロジェクトはいずれも存在せず、GraphQL API（`MapGraphQL()`）中心の構成だった。

【改修方法】

本プロジェクトでは改修不要。`app.UseStaticFiles(new StaticFileOptions { ... })`はそのまま残す。

（もし将来、`wwwroot`配下に管理画面用のCSS/JSなどビルド時に固定される静的アセットを追加するようなことがあれば、その部分に限って`MapStaticAssets`の採用を検討するとよい。その場合の書き方は次の通り。）

```diff
- app.UseStaticFiles();
+ app.MapStaticAssets();
```

MVC/Razor Pagesアプリの場合はさらに次のように連結する。

```csharp
app.MapRazorPages()
   .WithStaticAssets();
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

---

### Blazor Web Appに簡単な認証状態のシリアル化を採用する
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/migration/80-to-90?view=aspnetcore-10.0&tabs=visual-studio

【前提知識】

- **Blazorとは**
  C#でブラウザのUIを組み立てられるASP.NET Coreのフレームワーク。サーバー側で動く「Blazor Server」と、ブラウザ内のWebAssemblyで動く「Blazor WebAssembly」があり、両方を組み合わせた構成が「Blazor Web App」と呼ばれる。
- **認証状態のシリアル化とは**
  サーバー側で「誰がログインしているか」を判定した結果を、クライアント側（ブラウザで動くWebAssembly部分）にも引き継ぐための仕組み。サーバーとクライアントは別々のプロセスとして動くため、何もしなければクライアント側は「誰がログインしているか」を知らない。

【説明】

このリポジトリはBlazorを使っていない（Reactベースのフロントエンドと、ASP.NET Core GraphQL APIという構成）ため、直接の影響はない。参考として内容を要約すると、.NET 9では、サーバー側で認証状態をクライアント側に渡すための独自コード（`PersistingAuthenticationStateProvider.cs`）を自分で書かなくても、`AddRazorComponents().AddAuthenticationStateSerialization()`という1行を呼ぶだけで済むようになった。同様にクライアント側でも、独自の`PersistentAuthenticationStateProvider.cs`を`AddAuthenticationStateDeserialization()`という1行の呼び出しに置き換えられる。

【放置したときの影響】

このリポジトリではBlazor自体を使っていないため、影響はない。

【プロジェクトでの調べ方】

`*.razor`ファイルや`Microsoft.AspNetCore.Components`への参照、`AddRazorComponents`の呼び出しがプロジェクト内に存在するかを確認すればよい。本プロジェクトの構成（`backend/DicomTool.Api`＝GraphQL API、フロントエンドはReact、TemporalワーカーやWinFormsトレイアプリ、DICOM通信サービス）を踏まえると、いずれも該当しないことは明らか。

【改修方法】

対応不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

---

### ストリーミング レンダリング属性に true パラメーターが不要になった
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/migration/80-to-90?view=aspnetcore-10.0&tabs=visual-studio

【前提知識】

- **ストリーミングレンダリングとは**
  Blazorのサーバーサイドレンダリングにおいて、ページ全体のデータが揃うのを待たずに、先に画面の一部（読み込み中の表示など）を送り、データが揃い次第残りを追加で送る仕組み。ユーザーは真っ白な画面で長く待たされずに済む。
- **`@attribute`とは**
  `.razor`ファイルの先頭に書く、そのコンポーネントに対する属性（メタ情報）の指定。`[StreamRendering]`はコンポーネントに対して「このコンポーネントはストリーミングレンダリングを使う」と指定するための属性。

【説明】

このリポジトリはBlazorを使っていないため、直接の影響はない。参考として内容を要約すると、.NET 8では`@attribute [StreamRendering(true)]`のように明示的に`true`を渡す必要があったが、.NET 9以降は`true`が既定値になったため`@attribute [StreamRendering]`と書くだけでよくなった、という単なる記法の簡略化。

【放置したときの影響】

このリポジトリではBlazor自体を使っていないため、影響はない。

【プロジェクトでの調べ方】

`.razor`ファイルの中で`[StreamRendering(true)]`という記述を検索すればよいが、本プロジェクトには`.razor`ファイル自体が存在しない。

【改修方法】

対応不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

# .NET 9→10への移行
## パッケージ参照の更新より後の手順

### Blazor WebAssembly MSBuildプロパティを使用してWasmApplicationEnvironmentName環境を設定する
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/migration/90-to-100?view=aspnetcore-10.0&tabs=visual-studio

【前提知識】

- **スタンドアロンBlazor WebAssemblyとは**
  サーバー側のASP.NET Coreを介さず、静的ファイルとしてブラウザに配信され、ブラウザ内のWebAssemblyだけで完結して動くBlazorアプリの形態。
- **環境（Environment）とは**
  ASP.NET Coreにおける「Development」「Staging」「Production」といった実行環境の区分。環境ごとに設定ファイル（`appsettings.Development.json`など）を切り替えたり、デバッグ用UIの表示有無を制御したりするために使う。通常のASP.NET Coreサーバーでは環境変数`ASPNETCORE_ENVIRONMENT`で指定するが、スタンドアロンBlazor WebAssemblyはサーバープロセスを持たないため、この仕組みがそのままは使えない。

【説明】

このリポジトリはBlazorを使っていないため、直接の影響はない。参考として内容を要約すると、スタンドアロンBlazor WebAssemblyアプリにおいて、これまでHTTPリクエストの`Blazor-Environment`ヘッダーや`launchSettings.json`内の`ASPNETCORE_ENVIRONMENT`で環境名を指定していたのに代わり、.NET 10からは`.csproj`ファイルに`<WasmApplicationEnvironmentName>`というMSBuildプロパティを書いて環境名を指定できるようになった。指定しない場合、ビルド時は既定で`Development`、発行（publish）時は既定で`Production`として扱われる。

【放置したときの影響】

このリポジトリではBlazor自体を使っていないため、影響はない。

【プロジェクトでの調べ方】

`.csproj`ファイルの`<Sdk>`が`Microsoft.NET.Sdk.BlazorWebAssembly`になっているプロジェクトが無いかを確認すればよいが、本プロジェクトの`.csproj`一覧（`backend/DicomTool.Api`、Temporalワーカー、WinFormsトレイアプリ、DICOM通信サービスなど）にBlazor WebAssemblyのSDKを使っているものはない。

【改修方法】

対応不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

---

### インライン化されたブート構成ファイル
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/migration/90-to-100?view=aspnetcore-10.0&tabs=visual-studio

【前提知識】

- **`blazor.boot.json`とは**
  Blazor WebAssemblyアプリが起動する際に最初に読み込む、「どの.dllファイルをダウンロードすべきか」「それぞれのファイルの整合性チェック用ハッシュ値は何か」といった情報をまとめたJSONファイル。従来はブラウザから直接取得できる独立したファイルとして配信されていた。
- **`dotnet.js`とは**
  Blazor WebAssembly（および.NETのWebAssembly全般）のランタイムを起動するためのJavaScriptファイル。ブラウザは最初にこのファイルを読み込み、そこから.NETランタイム本体やアプリのDLLを順に読み込んでいく。

【説明】

このリポジトリはBlazorを使っていないため、直接の影響はない。参考として内容を要約すると、.NET 10では、従来独立したファイルとして配信されていた`blazor.boot.json`の内容が、`dotnet.js`スクリプトの中にインラインで埋め込まれる形に変わった。これにより、通常のアプリ開発者には影響がないが、`blazor.boot.json`を直接パースしたりダウンロードしたりして「DLLの整合性チェックの失敗をデバッグする」「DLLファイルの拡張子を変更するカスタマイズをする」といった、内部構造に踏み込んだ独自処理をしていた開発者には影響が出る。

【放置したときの影響】

このリポジトリではBlazor自体を使っていないため、影響はない。

【プロジェクトでの調べ方】

`blazor.boot.json`という文字列でリポジトリ全体を検索し、直接参照・パースしているコードが無いかを確認すればよいが、本プロジェクトはBlazorを使っておらず該当なし。

【改修方法】

対応不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

---

### コンポーネントとサービスからの状態を保持するための宣言型モデル
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/migration/90-to-100?view=aspnetcore-10.0&tabs=visual-studio

【前提知識】

- **プリレンダリング（Prerendering）とは**
  Blazor Web Appにおいて、ブラウザ側でJavaScript/WebAssemblyが起動する前に、サーバー側で一度HTMLを生成してユーザーに先に見せる仕組み。表示は速くなるが、その後クライアント側で改めて同じコンポーネントが初期化されるため、「サーバー側で取得した値」をクライアント側にも引き継がないと、同じデータ取得処理が二重に走ってしまう。
- **`PersistentComponentState`とは**
  上記の「サーバー側で取得した値をクライアント側に引き継ぐ」ために.NET 8から存在するサービス。ただし、状態の登録・取得を手続き的に自分でコードを書く必要があり、記述量が多くなりがちだった。
- **属性（Attribute）による宣言型の指定とは**
  「このプロパティは永続化してほしい」という意図を、手続き的なコード（`RegisterOnPersisting`の呼び出しなど）ではなく、プロパティの上に`[PersistentState]`と書くだけで宣言的に表現できるようにする方式。

【説明】

このリポジトリはBlazorを使っていないため、直接の影響はない。参考として内容を要約すると、.NET 10からは、コンポーネントやサービスが持つ状態をプリレンダリングをまたいで保持したい場合、対象のプロパティに`[PersistentState]`属性を付けるだけで、Blazorのフレームワーク側が自動的に永続化・復元をしてくれるようになった。これにより、従来`PersistentComponentState`を使って自分で書いていた手続き的なコードが大幅に削減できる。

【放置したときの影響】

このリポジトリではBlazor自体を使っていないため、影響はない。

【プロジェクトでの調べ方】

`PersistentComponentState`という文字列でリポジトリ全体を検索し、既存の手続き的な状態保持コードが無いかを確認すればよいが、本プロジェクトはBlazorを使っておらず該当なし。

【改修方法】

対応不要（新機能のため、そもそも移行時に「壊れて直す」類の対応は発生しない）。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

---

### カスタム Blazor キャッシュと MSBuild プロパティ BlazorCacheBootResources 削除
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/migration/90-to-100?view=aspnetcore-10.0&tabs=visual-studio

【前提知識】

- **`BlazorCacheBootResources`とは**
  Blazor WebAssemblyアプリの`.csproj`に設定できたMSBuildプロパティの1つ。Blazorがブラウザにダウンロードしたリソース（DLLファイルなど）を独自のキャッシュ機構でキャッシュするかどうかを制御していた。
- **フィンガープリント化とブラウザキャッシュとは**
  （前述の「UseStaticFiles を MapStaticAssets に置き換える」の項目を参照。）ファイル名にハッシュ値を含めることで、ブラウザの標準的なキャッシュ機構だけで安全に長期キャッシュできるようにする仕組み。

【説明】

このリポジトリはBlazorを使っていないため、直接の影響はない。参考として内容を要約すると、.NET 10ではBlazorクライアント側の全ファイルがフィンガープリント化され、ブラウザの標準キャッシュだけで十分に効率よくキャッシュできるようになったため、従来Blazor独自に持っていたカスタムキャッシュ機構と、それを制御する`BlazorCacheBootResources`というMSBuildプロパティ自体が廃止（削除）された。もし`.csproj`にこのプロパティの設定が残っていた場合、それは削除する必要がある（未知のプロパティとして無視されるだけで、ビルドエラーにはならないと考えられるが、意味のない設定として残ってしまう）。

【放置したときの影響】

このリポジトリではBlazor自体を使っていないため、影響はない。

【プロジェクトでの調べ方】

`BlazorCacheBootResources`という文字列で全`.csproj`ファイルを検索すればよいが、本プロジェクトはBlazorを使っておらず該当なし。

【改修方法】

対応不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

---

### 既存のパスキー ユーザー認証を採用する Blazor Web App
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/migration/90-to-100?view=aspnetcore-10.0&tabs=visual-studio

【前提知識】

- **パスキー（Passkey）とは**
  パスワードの代わりに、指紋認証や顔認証、セキュリティキーなどを使ってログインする、より安全とされる認証方式。FIDO2/WebAuthnという業界標準の技術に基づく。

【説明】

このリポジトリはBlazorを使っていないため、直接の影響はない。この項目は厳密には「破壊的変更」ではなく、既存のBlazor Web App（Individual Accountsテンプレートなど）にパスキー認証を追加導入したい場合のガイダンスへの案内。具体的な移行手順というより新機能紹介に近い位置づけ。

【放置したときの影響】

このリポジトリではBlazor自体を使っていないため、影響はない。

【プロジェクトでの調べ方】

該当なし（本プロジェクトはBlazorのIndividual Accounts認証テンプレートを使っていない）。

【改修方法】

対応不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

---

### 個別のアカウントを使用している Blazor Web App でナビゲーションエラーが無効になっている場合
リンク：https://learn.microsoft.com/ja-jp/aspnet/core/migration/90-to-100?view=aspnetcore-10.0&tabs=visual-studio

【前提知識】

- **Individual Accounts（個別のアカウント）テンプレートとは**
  Visual StudioやCLIでBlazor Web Appプロジェクトを新規作成する際に選べる、ASP.NET Core Identityを使ったユーザー登録・ログイン機能一式をあらかじめ用意してくれるテンプレートの1つ。
- **`BlazorDisableThrowNavigationException`とは**
  Blazorのナビゲーション（ページ遷移）処理中に例外を投げる挙動を無効化するための設定フラグ。
- **`[DoesNotReturn]`属性とは**
  C#コンパイラに対して「このメソッドは（例外を投げるなどして）絶対に処理を戻さない」と伝えるための属性。静的解析（null許容性解析など）の精度を上げるために使われる。

【説明】

このリポジトリはBlazorを使っていないため、直接の影響はない。参考として内容を要約すると、`.csproj`や設定で`<BlazorDisableThrowNavigationException>`を`true`に設定していたBlazor Web App（Individual Accounts認証）が対象で、この設定をしていると`IdentityRedirectManager.cs`というテンプレート生成コードの中で、ナビゲーション時に`InvalidOperationException`を投げる処理と、それに付随する`[DoesNotReturn]`属性（テンプレート内5箇所）が、.NET 10のテンプレート更新後のコードと整合しなくなる。該当する場合は、`InvalidOperationException`のスロー処理と`[DoesNotReturn]`属性を手動で削除する対応が必要。

【放置したときの影響】

このリポジトリではBlazor自体を使っていないため、影響はない。

【プロジェクトでの調べ方】

`BlazorDisableThrowNavigationException`および`IdentityRedirectManager`という文字列でリポジトリ全体を検索すればよいが、本プロジェクトはBlazor Web App・ASP.NET Core Identityのテンプレートを使っておらず該当なし。

【改修方法】

対応不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）
