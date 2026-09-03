## Extensions

### BackgroundService は、すべての ExecuteAsync をタスクとして実行します
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task

【前提知識】

- **`IHostedService`／`BackgroundService`とは**
  ASP.NET Core（や、Generic Hostを使うワーカーアプリ）は、Webサーバーとして待ち受ける役割とは別に、「アプリ起動中ずっと裏で動き続ける処理（バッチ的な監視、キューのポーリングなど）」を同じプロセス・同じDIコンテナの中で動かせる仕組みを持っている。この「裏で動き続ける処理」の共通インターフェースが`IHostedService`で、`StartAsync`/`StopAsync`という2つのメソッドを持つ。`BackgroundService`は、この`IHostedService`を実装した抽象クラスで、利用者は`StartAsync`/`StopAsync`を自分で書かなくても、`ExecuteAsync`という1つの非同期メソッドをオーバーライドするだけで済むようにしてくれる、いわば「お手軽版」。
- **ホストの起動シーケンスとは**
  アプリを起動すると、ASP.NET Core（Generic Host）はDIコンテナに登録されているすべての`IHostedService`（`BackgroundService`を含む）に対して、順番に`StartAsync`を呼び出していく。すべての`StartAsync`が完了する（正確には完了したとみなされる）まで、アプリは「起動完了」とはみなされない。
- **`async`メソッドにおける「同期部分」と「非同期部分」の違い**
  C#の`async`メソッドは、呼び出された瞬間に丸ごと別スレッドに切り替わるわけではない。**最初に`await`に到達するまでのコードは、呼び出し元と同じスレッド上で、同期的に（＝呼び出し元をブロックしながら）実行される**。最初の`await`に到達して初めて、そこから先が非同期的に進むようになる（＝呼び出し元に制御が戻る）。この性質は`ExecuteAsync`にもそのまま当てはまる。

【説明】

`BackgroundService.ExecuteAsync`は、名前だけ見ると「呼び出したら即座にバックグラウンドスレッドに処理が移る」ように思えるが、実際には上記の「同期部分／非同期部分」の性質がそのまま現れていた。

- **以前の動作（.NET 9以前）**：`BackgroundService`の内部実装が`ExecuteAsync`をホストの起動シーケンス中（＝メインスレッド）で直接呼び出す作りになっていたため、`ExecuteAsync`の中の「最初の`await`より前の同期的なコード」が、**他のすべての`IHostedService`の起動をブロックしながら**メインスレッドで実行されていた。最初の`await`に達して初めて、そこから先がバックグラウンドスレッドに切り替わっていた。
- **新しい動作（.NET 10以降）**：`BackgroundService`の内部実装が変わり、`ExecuteAsync`メソッド全体（同期部分も含めて）が最初からバックグラウンドスレッドで実行されるようになった。つまり`ExecuteAsync`のどこにコードを書いても、他のサービスの起動をブロックしなくなった。
- **変更理由**：以前の「最初の`await`まではメインスレッドで同期実行され、他のサービスをブロックする」という挙動は、ほとんどの開発者が知らずに書いてしまう「落とし穴」だった。「`ExecuteAsync`にちょっとした初期化コードを`await`の前に書いただけで、意図せず他のサービスの起動が固まる（あるいは逆に、その順序性に無自覚に依存してしまう）」という混乱を避けるため、常に「バックグラウンドで動く」という直感通りの挙動に統一された。

【放置したときの影響】

このリポジトリで唯一`BackgroundService`を継承している`services/DicomTool.DicomScp/Services/DicomScpHostedService.cs`は、次のようになっている。

```csharp
public sealed class DicomScpHostedService : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // DICOM SCP(DIMSE)リスナーの起動処理はここに書かれている
        _server = _dicomServerFactory.Create<DicomScpService>(ServicePorts.DicomScpDimse);
        return base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    // ...
}
```

もし仮に、このような「起動時に必ず先に済ませておきたい同期的な処理」を`StartAsync`ではなく`ExecuteAsync`の`await`より前に書いてしまっていた場合、.NET 8/9では意図せず「他のサービスの起動をブロックする」という副作用に頼った設計になりがちだった（ブロックされることを前提に、後続のサービスがこのサービスの初期化完了を暗黙に期待してしまう、など）。.NET 10ではその暗黙の順序保証が失われるため、次のような不具合が顕在化しうる。

```csharp
// 「落とし穴」の典型例（本プロジェクトには存在しないが、注意喚起用の例）
public class RiskyService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // .NET 9以前は、これが完了するまで他サービスの起動がブロックされていた
        // （＝暗黙のうちに「このコードが終わってから次に進む」ことに依存できていた）
        InitializeSharedResourceSynchronously();

        await Task.Delay(Timeout.Infinite, stoppingToken); // ここが最初のawait
    }
}
```

.NET 10ではこの`InitializeSharedResourceSynchronously()`が他サービスと並行して走るようになるため、「共有リソースの初期化が終わる前に、他のサービスがそのリソースを使い始めてしまう」といった競合状態（レースコンディション）が発生しうる。

【プロジェクトでの調べ方】

`BackgroundService`（および`IHostedService`/`IHostedLifecycleService`）を直接継承しているクラスをリポジトリ全体から検索した。

```
grep -rn ": BackgroundService\|: IHostedService\|IHostedLifecycleService" --include=*.cs
```

ヒットしたのは`services/DicomTool.DicomScp/Services/DicomScpHostedService.cs`の1件のみだった。中身を確認したところ、上記の通り**起動時に必要な処理（DICOMサーバーのリスニング開始）はすでに`StartAsync`のオーバーライド内に書かれており、`ExecuteAsync`は`Task.CompletedTask`を返すだけ**になっていた。つまりこのクラスは、まさに本ページが「推奨されるアクション」として挙げているパターン（同期的に他をブロックしたい処理は`StartAsync`に書く）を、変更前から偶然採用済みだった。したがって**この変更によるdicom-tool-3への実害はない**。

なお`services/DicomTool.Worker`（Temporalワーカー）はTemporal SDK（Temporalio）が提供する独自のホスティング機構を使っており、自前で`BackgroundService`を継承したクラスは存在しなかった。

【改修方法】

対応不要（現状、影響を受けるコードが存在しないため）。

参考として、今後もし「起動時に他のサービスをブロックしてでも先に終わらせたい同期的な処理」を`BackgroundService`に書く必要が出てきた場合の書き方を示す。

改修前（`ExecuteAsync`の`await`前に書いてしまい、.NET 10ではブロックされなくなるコード）：
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    DoSyncSetup(); // .NET10ではもう他サービスをブロックしない
    await RunLoopAsync(stoppingToken);
}
```

改修後（`DicomScpHostedService`と同じパターン。`StartAsync`に移す）：
```csharp
public override Task StartAsync(CancellationToken cancellationToken)
{
    DoSyncSetup(); // ここに書けば、.NET10でも他サービスの起動をブロックする
    return base.StartAsync(cancellationToken);
}

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    await RunLoopAsync(stoppingToken);
}
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### AnyKey を使用した GetKeyedService() と GetKeyedServices() の問題を修正する
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/extensions/10.0/getkeyedservice-anykey

【前提知識】

- **DI（依存性注入）とは**
  「あるインターフェースを使いたいクラスが、その実装クラスを自分で`new`せず、外部（DIコンテナ）から渡してもらう」設計パターン。ASP.NET Coreでは`builder.Services.AddSingleton<IFoo, Foo>()`のように登録しておくと、コンストラクターに`IFoo foo`という引数を書くだけで実装が自動的に渡ってくる。
- **キー付きサービス（Keyed Services）とは**（.NET 8で追加された機能）
  通常のDI登録は「1つのインターフェースに対して1つの実装」が基本だが、「同じインターフェースに対して、用途ごとに複数の実装を登録し、呼び出し側が“キー”（文字列など）で選んで取り出したい」というケースがある（例：通知方法が複数あり、"email"キーならメール通知の実装、"sms"キーならSMS通知の実装を取り出したい）。これを実現するのが`AddKeyedSingleton<IFoo, FooA>("A")`のような登録と、`GetKeyedService<IFoo>("A")`のような取得。
- **`KeyedService.AnyKey`とは**
  キー付きサービスを登録する際に、特定のキーではなくこの`KeyedService.AnyKey`という特殊な値をキーとして指定できる。これは「どんなキーで問い合わせられても一致する、いわばワイルドカード登録（キャッチオール）」を意図したもので、「特定のキーに対応する実装がまだ用意されていないときの共通のフォールバック実装」を登録するような用途を想定している。

【説明】

- **以前の動作**：`GetKeyedService(typeof(IFoo), KeyedService.AnyKey)`のように、**取得側**で`AnyKey`を「1つのサービスを取り出すためのキー」として指定すると、`AnyKey`で登録されたサービスがそのまま返ってきてしまっていた。また`GetKeyedServices(typeof(IFoo), KeyedService.AnyKey)`（複数形。すべて列挙するAPI）に`AnyKey`を渡すと、`AnyKey`で登録された項目まで列挙結果に含まれてしまっていた。
- **新しい動作（.NET 10）**：`GetKeyedService()`に`AnyKey`を渡すと、`InvalidOperationException`（「AnyKeyを使って単一のサービスを解決することはできません」）が**例外として**投げられるようになった。`GetKeyedServices()`（複数形）に`AnyKey`を渡した場合は、例外にはならないが、`AnyKey`で登録された項目は結果から除外され、**特定のキーで登録されたものだけ**が返るようになった。
- **変更理由**：`AnyKey`は本来「特定のキーが問い合わせられたときに一致させるための“特殊な登録”」であって、「取得側が自分で指定して1個取り出すためのキー」でも「一覧取得のためのキー」でもない。以前の動作はこの本来の意図と矛盾しており、キー付きサービスまわりの挙動が直感的でなくなっていたため、意図通りのセマンティクスに修正された。

【放置したときの影響】

もしこのプロジェクトが`KeyedService.AnyKey`を使ったコードを持っていた場合、次のような影響が出る。

```csharp
// 取得側で AnyKey を直接指定していたコード（.NET10では例外で落ちる）
var service = serviceProvider.GetKeyedService<IMyService>(KeyedService.AnyKey);
// → InvalidOperationException: "Cannot resolve a single service using AnyKey."
```

```csharp
// AnyKey登録も含めて全件取得するつもりだったコード（.NET10では件数が減る＝ロジックがサイレントに変わる）
var all = serviceProvider.GetKeyedServices<IMyService>(KeyedService.AnyKey);
// .NET9以前: AnyKey登録も含めて返っていた
// .NET10以降: AnyKey登録は除外され、特定キー登録分だけが返る
```

前者は起動直後または該当コードのパスを通った瞬間に例外で気づけるため、まだ影響としては軽い（見つけやすい）。後者は例外が出ずに件数だけが変わるため、テストでカバーされていないと気づきにくい、より厄介な影響になりうる。

【プロジェクトでの調べ方】

キー付きサービス関連のAPI（`GetKeyedService`／`GetKeyedServices`／`AnyKey`／`AddKeyedSingleton`／`AddKeyedScoped`／`AddKeyedTransient`／`FromKeyedServices`）をリポジトリ全体から検索した。

```
grep -rn "GetKeyedService\|GetKeyedServices\|AnyKey\|AddKeyedSingleton\|AddKeyedTransient\|AddKeyedScoped\|FromKeyedServices"
```

**1件もヒットしなかった。** dicom-tool-3では、DI登録はすべて通常の（キーなしの）`AddSingleton`/`AddScoped`/`AddTransient`のみで行われており、キー付きサービスの仕組み自体を使っていない。したがって**この変更による影響は一切ない**。

【改修方法】

対応不要（未使用のため）。参考として、将来キー付きサービスを導入する場合に注意すべき書き方の違いを示す。

改修前（.NET9以前の、意図と矛盾した使い方）：
```csharp
services.AddKeyedSingleton<INotifier, EmailNotifier>(KeyedService.AnyKey);

// AnyKeyを「取り出すためのキー」として使ってしまっている
var notifier = provider.GetKeyedService<INotifier>(KeyedService.AnyKey);
```

改修後（特定のキーを使って解決する）：
```csharp
services.AddKeyedSingleton<INotifier, EmailNotifier>(KeyedService.AnyKey);

// 特定のキーで問い合わせる（登録側でAnyKeyにしていれば、これでフォールバックとしてヒットする）
var notifier = provider.GetKeyedService<INotifier>("email");
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### 構成で保持される NULL 値
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/extensions/10.0/configuration-null-values-preserved

【前提知識】

- **構成（Configuration）システムとは**
  ASP.NET Core／.NETアプリでは、`appsettings.json`や環境変数、コマンドライン引数などから設定値を読み込む統一的な仕組みが用意されている（`IConfiguration`）。このプロジェクトでも`backend/DicomTool.Api/appsettings.json`のJwt設定やCORS許可オリジンなどがこれに当たる。
- **「バインド（Bind）」とは**
  読み込んだ設定値（文字列ベース）を、C#で定義したクラスのプロパティに自動的に割り当てる処理。たとえば`appsettings.json`の`"Jwt": { "ExpiryMinutes": 480 }`という値を、`JwtOptions.ExpiryMinutes`という`int`プロパティに自動でセットしてくれる。`configuration.Get<T>()`や`services.Configure<T>(section)`がこれを行う代表的なAPI。
- **null許容参照型・null許容値型の基礎**
  C#では`string`のような参照型は元々`null`を入れられるが、`int`や`bool`、列挙型（`enum`）などの値型は、そのままでは`null`を入れられない（`int? `のように`?`を付けて初めて`null`を許容する「null許容値型」になる）。JSONの`null`をこれらの型にバインドしようとしたときにどう扱われるかが、今回の変更の中心。

【説明】

以前の構成バインダーには、地味だが混乱の元になる2つの癖があった。

1. **JSON構成プロバイダーが`null`を空文字列に変換してしまう**：`appsettings.json`に`"SigningKey": null`と書いても、内部的には`""`（空文字列）として扱われていた。
2. **バインダーが`null`値と「その項目が存在しない」を区別しない**：値が`null`（＝内部的には上記の理由でほぼ`""`）だと、バインダーは「その項目自体が構成に存在しない」ものとして扱い、**バインドをスキップ**していた（＝C#側のプロパティは初期値のまま変わらない…はずが、実際には上記1の変換のせいで空文字列で上書きされてしまうなど、動作が入り組んでいた）。

.NET 10では、この2点が是正された。

- JSON構成プロバイダーは`null`を`null`のまま正しく報告するようになった（空文字列への変換をしない）。
- バインダーは`null`を「欠損値」ではなく「明示的に指定された値」として扱い、対応するプロパティに実際に`null`をバインドするようになった（`string`のようなnull許容参照型なら、実際に`null`が入る）。
- `int`や`enum`のような**null許容ではない値型**のプロパティに`null`をバインドしようとした場合、.NET 9以前は`InvalidOperationException`（型変換失敗の例外）が発生していたが、.NET 10では例外を投げずに、その型の既定値（`int`なら`0`、`enum`なら`0`に対応する値）が設定されるようになった。
- 空の配列（`[]`）も、以前は無視されていたが、.NET 10からは正しく「要素数0の配列」としてバインドされるようになった。

【放置したときの影響】

このプロジェクトの`backend/DicomTool.Api/Configuration/JwtOptions.cs`と、それを使う`Program.cs`／`AuthService.cs`を例に、具体的にどう影響しうるかを説明する。

```csharp
// JwtOptions.cs
public sealed class JwtOptions
{
    public string SigningKey { get; set; } = "";   // 既定値は空文字列
    public int ExpiryMinutes { get; set; } = 60 * 8; // 既定値は480分
}
```

```csharp
// Program.cs / AuthService.cs
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
```

現在の`appsettings.json`では`"SigningKey": ""`（空文字列）が明示的に書かれており、`null`は使われていない。しかし、もし将来誰かが「未設定を表すつもりで」`"SigningKey": null`や`"ExpiryMinutes": null`と書いてしまった場合、挙動が次のように変わる。

- **`SigningKey`（`string`、null許容参照型ではないが参照型なので元々`null`を受け付けられる）**：.NET 9以前は`null`が空文字列`""`に変換されてバインドされていた（＝`Encoding.UTF8.GetBytes("")`は例外にならず、空バイト配列を使った危険な鍵ができあがっていた＝サイレントに壊れる）。.NET 10以降は`SigningKey`に本当の`null`が入るようになり、`Encoding.UTF8.GetBytes(null)`は`ArgumentNullException`を投げてアプリが**起動時に即座にクラッシュする**（気づきやすくなる、という意味では改善だが、挙動は変わる）。
- **`ExpiryMinutes`（`int`、null許容ではない値型）**：.NET 9以前は、JSON側の`null`が空文字列に変換された上で`int`への変換に失敗し、`InvalidOperationException`（「構成値をInt32型に変換できませんでした」）で**起動時に即座に落ちていた**。.NET 10以降は例外にならず、`ExpiryMinutes`は既定値`0`に設定されて**そのまま起動してしまう**（JWTの有効期限が実質0分になり、発行直後にトークンが無効になる、といった気づきにくい不具合につながりうる）。

つまりこの変更は、「間違って`null`を書いてしまった」という設定ミスに対して、.NET 8では（種類によっては）例外で早期に気づけていたものが、.NET 10では気づきにくい形でサイレントに既定値へフォールバックするケースが増える、という方向の変化を含む点に注意が必要。

【プロジェクトでの調べ方】

1. まず、構成バインド系のAPI（`ConfigurationBinder.Get<T>()`/`GetValue<T>()`/`services.Configure<T>()`/`BindConfiguration`など）の使用箇所を洗い出した。

   ```
   grep -rn "\.Configure<\|Configuration\.Get\|GetValue<\|Bind("
   ```

   主なヒットは以下の3箇所。
   - `backend/DicomTool.Api/Program.cs:69` … `builder.Services.Configure<FormOptions>(...)`（コード内で値を直接設定しており、JSON側の値は使っていないため無関係）
   - `backend/DicomTool.Api/Program.cs:125` … `builder.Services.Configure<JwtOptions>(jwtSection)`（`appsettings.json`の`Jwt`セクションをバインド。上記で説明した`SigningKey`/`ExpiryMinutes`が該当）
   - `backend/DicomTool.Api/Program.cs:175` … `builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? []`（空配列バインドが関係する箇所）
   - `services/DicomTool.StorageGuard/Program.cs:47` … `builder.Configuration.GetValue<double?>("StorageGuard:MinFreePercentDefault") ?? 10.0`（`double?`という**null許容値型**を使っており、かつ`??`でのフォールバックも既にあるため、この変更の影響を受けにくい書き方になっている）

2. 次に、現在の`appsettings.json`／`appsettings.Development.json`の中身を実際に確認し、`null`が明示的に書かれている項目がないか調べた。結果、**現時点では`Jwt`セクション・`Cors:AllowedOrigins`セクションのいずれにも`null`は使われていない**（`SigningKey`は空文字列`""`、`AllowedOrigins`は空配列`[]`または実際のURLの配列）。
3. `Cors:AllowedOrigins`の`Get<string[]>() ?? []`については、`appsettings.json`が`"AllowedOrigins": []`（空配列）のケースを実際に検証した。.NET 9以前は空配列のバインドが無視されて`Get<string[]>()`が`null`を返し、`?? []`で空配列にフォールバックしていた。.NET 10以降は`Get<string[]>()`自体が正しく空配列を返すため`?? []`は発火しないが、**結果はどちらも同じ「要素数0の配列」になる**ため、実害はない。

以上より、**現時点のdicom-tool-3はこの変更による実害を受けていない**が、`Jwt:SigningKey`や`Jwt:ExpiryMinutes`に将来誤って`null`を設定してしまった場合の挙動が変わる点は、チームへの申し送り事項として認識しておく価値がある。

【改修方法】

現状は改修不要。ただし、設定ミス（`null`の誤設定）を早期に検知したい場合は、バインド後に明示的な検証を追加しておくとよい。

改修前（バインドした値をそのまま信頼して使う）：
```csharp
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.Configure<JwtOptions>(jwtSection);
```

改修後（起動時に必須項目が空でないことを検証し、.NET10のサイレントな既定値フォールバックに頼らないようにする）：
```csharp
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.Configure<JwtOptions>(jwtSection);
builder.Services.AddOptions<JwtOptions>()
    .Validate(o => !string.IsNullOrEmpty(o.SigningKey), "Jwt:SigningKey が未設定です。")
    .Validate(o => o.ExpiryMinutes > 0, "Jwt:ExpiryMinutes は1以上である必要があります。")
    .ValidateOnStart(); // 起動時に検証を強制実行する
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### コンソールログ出力でメッセージが重複しなくなりました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/extensions/10.0/console-json-logging-duplicate-messages

【前提知識】

- **`ILogger`とログの「フォーマッタ」とは**
  .NETの標準的なログ出力の仕組みでは、`ILogger<T>.LogInformation(...)`のようなコードでログを出力すると、その内容がコンソールなどの出力先に書き込まれる。このとき「どういう見た目でログを書き出すか」を決めるのが**フォーマッタ**で、代表的なものに人間が読みやすい`Simple`フォーマッタ（既定）と、ログ集約基盤（ELK, Loki等）に取り込みやすい構造化された`Json`フォーマッタがある。`builder.Logging.AddSimpleConsole()`や`builder.Logging.AddJsonConsole()`のように明示的に指定して切り替える。
- **構造化ログの`State`と`{OriginalFormat}`とは**
  `LogInformation("Hello {Name}", name)`のような呼び出しでは、実は「レンダリング済みのメッセージ文字列（`Message`）」だけでなく、「`Name`という名前の値」や「元の書式指定文字列そのもの（`{OriginalFormat}`）」も内部的に保持されている（`State`と呼ばれるオブジェクトに入っている）。これにより、ログ集約基盤側で「`Name`というフィールドだけで検索する」といった高度な絞り込みができるようになっている。

【説明】

- **以前の動作**：JSONフォーマッタ（`AddJsonConsole`等）を使ってコンソールにログを出すと、同じメッセージ文字列が出力の中に**最大3回**現れていた。最上位の`Message`フィールドに1回、`State`オブジェクトの中の`Message`にもう1回、さらに`State`の中の`{OriginalFormat}`（書式指定文字列そのもの）にもう1回、という具合。

  ```json
  {
    "Message": "This is an information message.",
    "State": {
      "Message": "This is an information message.",
      "{OriginalFormat}": "This is an information message."
    }
  }
  ```

- **新しい動作（.NET 10）**：`Message`は最上位レベルにのみ出力され、`State`オブジェクトの中からは（通常は）取り除かれるようになった。

  ```json
  {
    "Message": "This is an information message.",
    "State": {
      "{OriginalFormat}": "This is an information message."
    }
  }
  ```

- **変更理由**：同じ文字列を何度も出力することによる無駄な冗長さ（ログのサイズ増加、パフォーマンスの無駄な書式設定処理）を減らすため。

【放置したときの影響】

ログの出力サイズが減り、内容がすっきりする方向の変更なので、基本的には歓迎すべき変更である。影響が出うるのは、**過去のJSON形式のログ出力を前提に、`State.Message`という深い階層から値を取り出すようなログ解析処理（パーサーやダッシュボードのクエリなど）を自前で書いていた場合**で、そのフィールドが（原則として）出力されなくなることで解析が壊れる可能性がある。

【プロジェクトでの調べ方】

コンソールログのフォーマッタ設定に関わるAPI・設定を検索した。

```
grep -rn "AddJsonConsole\|AddSimpleConsole\|AddConsole(\|AddSystemdConsole"
grep -rn "FormatterName" --include=appsettings*.json
```

いずれも**1件もヒットしなかった**。各サービス（`DicomTool.Api`、`DicomTool.Worker`、`DicomTool.DicomScp`、`DicomTool.StorageGuard`、`DicomTool.TrayApp`）の`Program.cs`はいずれもログ出力周りをカスタマイズしておらず、ASP.NET Core／Generic Hostの既定のコンソールロガー（`Simple`フォーマッタ。JSON形式ではない）がそのまま使われている。`appsettings.json`側にも`Logging:Console:FormatterName`のような設定は存在しない。

したがって**dicom-tool-3は現時点でJSON形式のコンソールログを使っておらず、この変更の影響を受けない**。

【改修方法】

対応不要。ただし、将来VM上での運用でログ集約基盤（例：Loki、ELK Stack等）と連携させるために`AddJsonConsole`へ切り替えるような場合は、この変更後の出力フォーマット（`State.Message`が原則出力されなくなる）を前提にログクエリ・パーサーを設計するとよい。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### ProviderAliasAttribute が Microsoft.Extensions.Logging.Abstractions アセンブリに移動されました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/extensions/10.0/provideraliasattribute-moved-assembly

【前提知識】

- **アセンブリ（Assembly）とは**
  C#でビルドすると出来上がる`.dll`（または`.exe`）ファイルの単位。NuGetパッケージ1つが必ずしも1アセンブリとは限らないが、`Microsoft.Extensions.Logging`パッケージは`Microsoft.Extensions.Logging.dll`という実体を持ち、より基盤的な型だけを集めた`Microsoft.Extensions.Logging.Abstractions`パッケージは`Microsoft.Extensions.Logging.Abstractions.dll`という別の実体を持つ、というように分かれている。ライブラリを作る側は、「実際にログを書き出す具体的な仕組み」までは要らず、「`ILogger`インターフェースのような抽象的な型」だけ使いたい場合、より軽量な`.Abstractions`パッケージだけを参照することで、余計な依存を増やさずに済む。
- **`[ProviderAlias]`属性とは**
  自分でカスタムのログプロバイダー（`ILoggerProvider`の実装）を作るときに、そのクラスへ`[ProviderAlias("MyProvider")]`という属性を付けておくと、`appsettings.json`の`Logging`セクションで、プロバイダーのクラス名の代わりに`"MyProvider"`という短いエイリアス名でログレベルを設定できるようになる、という補助的な機能。
- **型転送（Type Forwarding）とは**
  「ある型を別のアセンブリに移動したが、古いアセンブリを参照しているコードもそのまま動かしたい」というときに使われるテクニック。移動元のアセンブリに「この型は実は別のアセンブリに引っ越しました」という転送用の情報だけを残しておくことで、既存のコードは再コンパイルなしでそのまま動く。

【説明】

- **以前の動作**：`ProviderAliasAttribute`は`Microsoft.Extensions.Logging`アセンブリ（フル機能のロギングパッケージ）の中に定義されていた。そのため、この属性だけを使いたいライブラリ作者も、フルの`Microsoft.Extensions.Logging`パッケージ全体への依存を持たざるを得なかった。
- **新しい動作（.NET 10）**：`ProviderAliasAttribute`はより軽量な`Microsoft.Extensions.Logging.Abstractions`アセンブリの方へ定義し直された。互換性のため、元の`Microsoft.Extensions.Logging`アセンブリ側には「型転送」の情報が残されており、既存のコードは見た目上何も変えなくてもそのまま動く。
- **変更理由**：`ProviderAliasAttribute`のような「型に付けるだけの軽い注釈」のために、フルの`Microsoft.Extensions.Logging`パッケージ全体（実際のロギング実装まで含む、より重い依存）を引き込まなくて済むようにするため。`Microsoft.Extensions.Logging.Abstractions`だけに依存する軽量なライブラリでも、この属性を使えるようになる。

【放置したときの影響】

公式ドキュメントも「ほとんどのシナリオでアクション不要」としている。型転送によって既存コードはそのまま動く。唯一問題になりうるのは、**同じプロジェクトの中で「.NET 10版の`Microsoft.Extensions.Logging`」と「.NET 10より古いバージョンの`Microsoft.Extensions.Logging.Abstractions`」を、パッケージバージョンの不整合により両方参照してしまっている**ケースで、この場合は「同じ属性が2つのアセンブリの両方で定義されている」ことになり、コンパイルエラーになる可能性がある。

【プロジェクトでの調べ方】

1. `ProviderAlias`という文字列自体をリポジトリ全体から検索した。

   ```
   grep -rn "ProviderAlias"
   ```

   **1件もヒットしなかった。** dicom-tool-3では自前のカスタム`ILoggerProvider`実装を作っておらず、`[ProviderAlias]`属性を使っている箇所自体が存在しない。

2. 念のため、各`.csproj`が`Microsoft.Extensions.Logging`または`Microsoft.Extensions.Logging.Abstractions`を**直接**（`PackageReference`として）参照していないか確認した。

   ```
   grep -rn "PackageReference Include=\"Microsoft.Extensions.Logging" --include=*.csproj
   ```

   直接参照は見つからなかった（`Microsoft.Extensions.Logging`系はASP.NET CoreやGeneric Hostのフレームワーク参照から間接的に付いてくる形になっている）。また、全プロジェクトの`TargetFramework`を確認したところ、`DicomTool.Api`／`DicomTool.Worker`／`DicomTool.DicomScp`／`DicomTool.StorageGuard`／`DicomTool.Shared`／`DicomTool.Timeline`はすべて`net10.0`、`DicomTool.TrayApp`のみ`net10.0-windows`と、全プロジェクトが揃って.NET 10を対象にしており、新旧バージョンが混在するリスクも見当たらない。

以上より、**この変更はdicom-tool-3に一切影響しない**。

【改修方法】

対応不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### トリミング時の安全性が保証されない Microsoft.Extensions.Configuration コードから DynamicallyAccessedMembers 注釈が削除されました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/extensions/10.0/dynamically-accessed-members-configuration

【前提知識】

- **トリミング（Trimming）とは**
  `dotnet publish`時のオプションの1つで、「実際には使われていないコード（クラス・メソッドなど）をアセンブリから削り落とし、配布物のサイズを小さくする」機能。特に自己完結型（self-contained）デプロイやシングルファイル発行と組み合わせてよく使われる。dicom-tool-3では、`.csproj`を確認した限り`PublishTrimmed`や`TrimMode`といったトリミング関連の設定は使われておらず、通常の`dotnet run`／通常の`dotnet publish`で運用されている。
  参考：トリミングと対になる技術としてNative AOT（事前コンパイル）があるが、今回の変更は主にトリミングに関するもの。
- **リフレクション（Reflection）とトリミングの相性の悪さ**
  `Microsoft.Extensions.Configuration`の`ConfigurationBinder.Get<T>()`のようなAPIは、実行時に「Tという型にはどんなプロパティがあるか」をリフレクションで調べながら値を埋めていく作りになっている。トリミングは「どのコードが実際に使われるか」を静的（コンパイル時）に解析して削るかどうかを決めるが、リフレクションで「実行時に初めてわかる型のプロパティ」まではトリマーが正確に予測できないことがある。その結果、本当は実行時に必要なメンバーがうっかり削り落とされてしまい、`publish`は成功するのに実行するとエラーになる、という事故が起こりうる。
- **`[RequiresUnreferencedCode]`と`[DynamicallyAccessedMembers]`属性とは**
  この問題に対処するため、.NETは「このAPIはトリミングと相性が悪いので使うと警告を出す」という`[RequiresUnreferencedCode]`属性と、「このメンバー（型引数など）のこの種類の情報（コンストラクター、パブリックプロパティ等）はトリマーに消さないでほしい」と伝える`[DynamicallyAccessedMembers]`属性を用意している。
- **Configuration Binding Source Generator とは**
  リフレクションを一切使わず、コンパイル時に「この設定クラスをバインドするための専用コード」を自動生成してくれる仕組み。トリミングやNative AOTと安全に共存できる、`ConfigurationBinder.Get<T>()`等の代替手段として推奨されている。

【説明】

`ConfigurationBinder.Get<T>()`や`services.Configure<T>()`のようなリフレクションベースのバインドAPIには、すでに「トリミングすると壊れる可能性がある」ことを示す`[RequiresUnreferencedCode]`警告が付いていた。それに加えて.NET 9以前では、これらのAPIには`[DynamicallyAccessedMembers]`という「せめて一部のメンバーだけはトリマーに残しておいてもらう」ための注釈も付けられており、トリミングされた環境でも**限られたケースでは動くことがある**、という中途半端な状態になっていた。

.NET 10では、この「一部だけ残す」ための注釈が完全に削除された。これにより、トリミングされた環境でこれらのAPIを使った場合に**動く可能性のあるケースがさらに限定的になった**（＝以前は動いていた一部のケースも、動かなくなりうる）。

- **変更理由**：この注釈（特に`DynamicallyAccessedMemberTypes.All`という「型のあらゆるメンバーを残す」という強い指定）は、トリマーによる削減効果を大きく妨げてしまう。「一部だけ動く」という中途半端な保証をするより、「このAPIはトリミングでは正式にサポートしない、安全に使いたいならソースジェネレーターに移行してほしい」と、方針をはっきりさせる方向に倒された、という位置づけの変更。

【放置したときの影響】

dicom-tool-3が現状の運用（`dotnet run`／通常の`dotnet publish`、トリミングなし）を続ける限り、**実行時の挙動には一切影響しない**。この変更はあくまで「`-p:PublishTrimmed=true`のようなトリミングを有効にしてpublishしたとき」にのみ意味を持つ。

ただし、影響を受けるAPIの一覧には`ConfigurationBinder.Get<T>()`／`GetValue<T>()`、`OptionsBuilderConfigurationExtensions.Bind`/`BindConfiguration`、`OptionsConfigurationServiceCollectionExtensions.Configure`が含まれており、これらはこのプロジェクトの`backend/DicomTool.Api/Program.cs`（`Configure<JwtOptions>`、`GetSection(...).Get<string[]>()`）や`services/DicomTool.StorageGuard/Program.cs`（`GetValue<double?>`）で実際に使われている。したがって、**もし将来WinFormsの`DicomTool.TrayApp`をVM配布用にシングルファイル・トリミング publishしたい、といった要望が出てきた場合**には、これらの設定バインドコードがトリミングによって実行時エラー（`MissingMemberException`等）を起こす可能性がある、という点は覚えておく必要がある。

【プロジェクトでの調べ方】

1. まず、そもそもトリミングを有効にした発行を行っているかどうかを確認した。

   ```
   grep -rn "PublishTrimmed\|TrimMode\|IsTrimmable" --include=*.csproj
   grep -rn "PublishTrimmed\|self-contained\|SelfContained\|PublishSingleFile"
   ```

   いずれも**1件もヒットしなかった**。全`.csproj`にトリミング関連の設定はなく、デプロイ用のスクリプト類にも自己完結型／トリミング／シングルファイル発行を行っている形跡は見当たらなかった。各サービスは`dotnet run`で直接起動する運用（`CLAUDE.md`にも記載の通り、Dockerfileすら存在しない）であることも確認済み。

2. 次に、影響を受けるAPI（`ConfigurationBinder.Get<T>`/`GetValue<T>`/`Configure<T>`/`BindConfiguration`等）の使用箇所を洗い出した（前項「構成で保持されるNULL値」の調査結果と同じ箇所）。`backend/DicomTool.Api/Program.cs`と`services/DicomTool.StorageGuard/Program.cs`で使われていることを確認した。

以上より、**現時点のdicom-tool-3の運用方法（トリミングなし）では、この変更による実害はない**。ただし、将来トリミングやNative AOT発行を検討する際には、上記のバインドAPIをConfiguration Binding Source Generatorへ置き換える対応が必要になる、という点をチームへの申し送り事項として記録しておく。

【改修方法】

現状は改修不要。将来トリミングを有効化する場合の移行例を示す。

改修前（リフレクションベースのバインドAPIをそのまま使用）：
```csharp
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.Configure<JwtOptions>(jwtSection);
```

改修後（Configuration Binding Source Generatorを有効化する。`.csproj`に1行追加するだけで、`Configure<T>`等の呼び出しはソースコードを変更せずにコンパイル時生成コードへ自動的に切り替わる）：
```xml
<!-- .csproj -->
<PropertyGroup>
  <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
</PropertyGroup>
```
```csharp
// C#コード側は変更不要。ビルド時にソースジェネレーターが
// JwtOptions専用の、リフレクションを使わないバインドコードを自動生成してくれる。
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.Configure<JwtOptions>(jwtSection);
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）
