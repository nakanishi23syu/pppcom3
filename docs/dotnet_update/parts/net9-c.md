# .NET 9 での破壊的変更（SDKとMSBuild / Windows フォーム / WPF）

## SDK と MSBuild

### `dotnet sln add` では無効なファイル名が許可されない
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/9.0/dotnet-sln

【前提知識】

- **ソリューションファイル（.sln / .slnx）とは**
  複数のプロジェクト（.csprojファイル）をまとめて1つの単位として扱うための「目次ファイル」。Visual Studioで「ソリューションを開く」と言ったときに開いているのはこのファイルで、中に「どのプロジェクトが含まれるか」の一覧が書いてある。`dotnet sln add フォルダ/プロジェクト.csproj`というCLIコマンドで、この一覧にプロジェクトを追加できる。
- **.slnxとは**
  従来の`.sln`ファイルは独自のテキスト形式で、人間には少々読みにくく、差分（Gitでの変更履歴）も見づらいという弱点があった。`.slnx`はそれをXML形式で書き直した新しいソリューションファイル形式で、.NET 9のSDKから正式にサポートされた。中身は`<Solution><Project Path="..."/></Solution>`のようなシンプルなXMLになる。
- **DOS予約語とは**
  Windowsの前身であるMS-DOS時代から、`CON`（コンソール）、`NUL`（何もしないデバイス）、`PRN`（プリンター）、`COM1`〜`COM9`（シリアルポート）、`LPT1`〜`LPT9`（パラレルポート）といった名前は、ファイル名ではなく「特別な装置名」として予約されている。これらの名前ではファイルやフォルダを正しく作成できないため、そもそもファイル名として使うべきではない。

【説明】

.NET 9 SDK（バージョン9.0.2xx以降）から、`dotnet sln add`コマンドの内部実装が、新しく作られた`vs-solutionpersistence`というライブラリに切り替わった。これは`.slnx`形式を読み書きできるようにするための土台であり、これに伴って副次的な動作変更が2つ発生した。

- 以前：プロジェクト名やソリューションフォルダ名に、`CON`のようなDOS予約語や、`:`や`*`のようなWindowsのファイル名として本来使えない文字が含まれていても、`dotnet sln add`はエラーを出さずに通してしまっていた（実際にファイルシステム上でおかしなことになる可能性があった）。
- 新しい動作：これらの無効な名前は`dotnet sln add`の時点で弾かれ、エラーになる。
- また、以前は「同名の入れ子プロジェクト（例：`folder/project.csproj`と`parent/child/project.csproj`）」を追加しようとすると失敗していたが、新しい動作ではこれが成功するようになった（入れ子でないプロジェクトの挙動に合わせただけの改善）。

つまり「今まで通っていた（本来は不正な）名前が弾かれるようになった」というエラー面と、「今まで失敗していた正当なケースが成功するようになった」という改善面が両方ある変更。

【放置したときの影響】

影響は小さい。実際に問題になるのは、プロジェクトフォルダ名やプロジェクト名がそもそもDOS予約語や無効文字を含んでいるという、通常はまず起こらない特殊なケースだけ。既存の正常なプロジェクト名であれば、`dotnet sln add`の挙動が変わったことに気づく必要すらない。

もし該当する場合は、次のようなエラーで`dotnet sln add`自体が失敗する（ビルドが失敗するというより、CIやセットアップスクリプトでソリューションにプロジェクトを追加する手順が止まる）。

```
$ dotnet sln add "CON/CON.csproj"
（vs-solutionpersistenceからのエラーメッセージが表示され、追加に失敗する）
```

【プロジェクトでの調べ方】

dicom-tool-3ではすでに`DicomTool.slnx`という**新しい.slnx形式**のソリューションファイルが採用されている（`.sln`ファイルは存在しない）ことを確認した。中身は次のようにXMLでフォルダとプロジェクトが列挙されているだけのシンプルな構成。

```xml
<Solution>
  <Folder Name="/backend/">
    <Project Path="backend/DicomTool.Api/DicomTool.Api.csproj" />
    ...
```

各プロジェクトのフォルダ名・ファイル名（`DicomTool.Api`、`DicomTool.TrayApp`など）はいずれも通常の英数字とピリオドのみで、DOS予約語や無効文字を含む名前は存在しない。よって、この変更が実際に問題を起こすことはない。念のため確認するなら、`DicomTool.slnx`内の`Project Path`一覧と、`Glob **/*.csproj`で取得したフォルダ構成を照らし合わせ、`CON`・`NUL`・`AUX`・`PRN`・`COM?`・`LPT?`のような名前や、`:`・`*`・`?`のような記号がフォルダ名/ファイル名に含まれていないかを見ればよい（今回確認した限り該当なし）。

【改修方法】

現時点では対応不要。今後新しいプロジェクトを追加する際は、フォルダ名・プロジェクト名にDOS予約語や無効文字を使わないよう気をつければよい（そもそも一般的な命名規則に従っていれば問題にならない）。

```
# NG（DOS予約語をフォルダ名にしている）
dotnet sln DicomTool.slnx add CON/CON.csproj

# OK（通常の英数字名）
dotnet sln DicomTool.slnx add services/DicomTool.NewService/DicomTool.NewService.csproj
```

【参考記事】
- （特になし）

---

### `dotnet watch` 古いフレームワークのホット リロードと互換性がありません
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/9.0/dotnet-watch

【前提知識】

- **`dotnet watch`とは**
  `dotnet run`の代わりに`dotnet watch run`と実行すると、ソースコードのファイルを監視してくれて、保存するたびに自動でアプリを再起動（または後述のホットリロードで即座に反映）してくれる開発支援コマンド。Web開発でいう「ファイル保存したらブラウザが自動更新される」のに近い体験を.NETアプリでも実現するもの。
- **ホットリロード（Hot Reload）とは**
  アプリを実行したままソースコードを書き換えて保存すると、アプリを再起動せずにコードの変更をその場で反映してくれる機能。例えばWebAPIを起動したままメソッドの中身を1行直して保存すると、次のリクエストからもう新しいロジックで動く、といった具合。デバッグのたびにいちいちアプリを再起動する手間が省ける。
- **`TargetFramework`とは**
  `.csproj`ファイルに書く`<TargetFramework>net10.0</TargetFramework>`のような設定で、「このプロジェクトはどのバージョンの.NETに向けてビルドするか」を指定するもの。`net6.0`、`net7.0`のように書く。

【説明】

以前の`dotnet watch`は、`net5.0`以前（.NET 5、.NET Core 3.1など、かなり古いバージョン）を対象とするプロジェクトに対しては、ホットリロード機能を「黙って自動的に無効化」して起動していた。つまり、古いフレームワークではホットリロードが使えないという制約があったが、それによってエラーになることはなく、単に「ホットリロードなしの、ただの自動再起動ツール」として動いていた。

.NET 9からは、`dotnet watch`ツールの内部実装が大きく作り直され、この「古いフレームワークだと自動でホットリロードを諦める」という互換性維持のための分岐が削除された。その結果、.NET 5以前を対象とするプロジェクトで`dotnet watch`を（`--no-hot-reload`オプションを付けずに）実行すると、動作を諦めるのではなく、次のような**エラーで起動そのものが失敗する**ようになった。

```
ホット リロード ベースの監視は、.NET 6.0 以降のアプリでのみサポートされます。
```

理由は、サポート対象外の古い.NETバージョン向けの後方互換コードを`dotnet watch`の内部に残し続けることが、新しい実装の保守を複雑にするだけの価値がないと判断されたため。

【放置したときの影響】

**影響が出るのは`TargetFramework`が`net5.0`以前（`netcoreapp3.1`や`net5.0`など）のプロジェクトに限られる。** `net6.0`以降を対象とするプロジェクトでは、この変更の前後で挙動は変わらない。

対象プロジェクトが`net5.0`以前のままだった場合、`dotnet watch`コマンドの実行そのものがエラーで止まる（＝開発時の便利コマンドが使えなくなるだけで、`dotnet build`や`dotnet run`など通常のビルド・実行自体には影響しない）。

【プロジェクトでの調べ方】

先に行った`TargetFramework`のGrep結果より、dicom-tool-3の全プロジェクト（`DicomTool.Api`、`DicomTool.Worker`、`DicomTool.TrayApp`、`DicomTool.DicomScp`、`DicomTool.StorageGuard`、`DicomTool.Timeline`、`DicomTool.Shared`、`DicomTool.Api.Tests`）はすべて`net10.0`または`net10.0-windows`をターゲットにしていることを確認済み。`net5.0`以前を対象とするプロジェクトは1つも存在しない。したがって、**この変更はdicom-tool-3には影響しない。**

念のため、今後の確認方法としては`csproj`ファイル内の`<TargetFramework>`または`<TargetFrameworks>`の値を見て、`net5.0`・`netcoreapp3.1`・`netcoreapp2.x`のような古い値になっていないかを確認すればよい。

【改修方法】

現時点では対応不要。仮に将来、何らかの理由で古いフレームワークを対象にするプロジェクトを追加することになった場合は、以下のいずれかで対応する。

```
# 対応1: ホットリロードなしでdotnet watchを起動する
dotnet watch --no-hot-reload run

# 対応2: プロジェクトファイルのTargetFrameworkをnet6.0以降に上げる
<TargetFramework>net6.0</TargetFramework>
```

【参考記事】
- （特になし）

---

### `dotnet workload` コマンド出力の変更
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/9.0/dotnet-workload-output

【前提知識】

- **ワークロード（workload）とは**
  .NET SDKは標準ではWebアプリやコンソールアプリなどの開発に必要な最小限の機能しか持たないが、iOS/AndroidアプリやMAUIアプリ、ブラウザで動くWebAssemblyアプリなどを開発するには追加の大きなコンポーネント一式が必要になる。この「あとから追加でインストールする開発機能の単位」が「ワークロード」で、`dotnet workload install maui`のようにインストールする。
- **`--machine-readable`のようなオプションとは**
  通常、CLIコマンドの出力は人間が読みやすいように整形されるが、それをスクリプトやツールが自動解析（パース）しやすい形式（今回の場合はJSON）で出してほしい、というときに付けるオプション。プログラムが他のプログラムの出力を読み取って処理する、という自動化の文脈でよく使われる。
- **JSON（JavaScript Object Notation）とは**
  `{"key": "value"}`のような形式でデータを表現するテキスト形式。プログラム同士がデータをやり取りするときの共通言語としてよく使われ、多くのプログラミング言語に「JSON文字列を読み取ってプログラム内のデータ構造に変換する（パースする）」ための標準機能が用意されている。

【説明】

`dotnet workload list --machine-readable`のような、機械可読な出力を意図した一部の`dotnet workload`系コマンドは、以前はJSON本体の前後に、通常のログメッセージ（例えば「マニフェストの更新に失敗しました」といった警告文）や、JSON本体の開始・終了を示す目印（`==workloadListJsonOutputStart==` 〜 `==workloadListJsonOutputEnd==`）を一緒に出力していた。そのため、これらのコマンド出力を自動処理するツールを作る場合、単純にJSONパーサーへ丸ごと渡すことができず、「まず目印の行を探してその間だけを切り出す」という一手間（カスタム解析）が必要だった。

.NET 9からは、これらのコマンドはJSON本体だけを出力するようになった。目印や余計なログ行は出力されなくなり、コマンドの標準出力をそのままJSONパーサーに渡せるようになった。

【放置したときの影響】

**dotnet workload系コマンドの出力を自動解析するスクリプトやツールを自作している場合にのみ影響する。** 単に手元で`dotnet workload list`を実行して人間が目で見る分には、何も困らない（むしろ出力がスッキリして見やすくなる）。

もし過去に「`==workloadListJsonOutputStart==`という行を探してから、その次の行をJSONとしてパースする」ような自作スクリプトを書いていた場合、.NET 9環境ではその目印の行自体が出力されなくなるため、目印を待ち続けてパース処理が永遠に発生しない、あるいは例外になる、といった形で**動かなくなる**可能性がある。

```csharp
// 旧: 目印行を探してからJSONを取り出すような処理は、.NET 9以降では対象行が来ないため機能しなくなる
string? jsonLine = null;
bool insideJson = false;
foreach (var line in output.Split('\n'))
{
    if (line.Contains("==workloadListJsonOutputStart==")) { insideJson = true; continue; }
    if (line.Contains("==workloadListJsonOutputEnd==")) break;
    if (insideJson) jsonLine = line;
}
```

【プロジェクトでの調べ方】

`dotnet workload`というキーワードでリポジトリ全体をGrepしたが、ソースコードにもスクリプト類にも該当箇所は見つからなかった。dicom-tool-3はASP.NET Core Web API・Temporalワーカー・WinFormsトレイアプリ・DICOM通信サービスという構成で、iOS/Android/MAUI/WebAssemblyのような追加ワークロードを必要とする開発は行っていないため、`dotnet workload`コマンド自体を使う場面がそもそも存在しない。**この変更はdicom-tool-3には影響しない。**

【改修方法】

現時点では対応不要。将来、CIパイプラインなどで`dotnet workload list --machine-readable`などの出力を解析するスクリプトを書く場合は、目印行を探す処理を書かず、標準出力をそのままJSONパーサーに渡せばよい。

【参考記事】
- （特になし）

---

### `installer` リポジトリ バージョンはドキュメントに記載されなくなりました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/9.0/productcommits-versions

【前提知識】

- **.NETのリポジトリ構成とは**
  .NET自体はMicrosoftがGitHub上でオープンソースとして開発しており、`dotnet/runtime`（実行エンジン本体）、`dotnet/sdk`（開発者が使うCLIツール群）、`dotnet/aspnetcore`（Webフレームワーク）、`dotnet/installer`（これらを1つのインストーラーにまとめる役割）のように、機能ごとに複数のリポジトリ（Gitのソースコード置き場）に分かれて開発されている。
- **`productcommits`ファイルとは**
  ある.NETのビルド（例えば「.NET 9.0.100」というバージョン）が、上記の各リポジトリのどの時点のコミット（変更履歴上の1点）を組み合わせて作られたかを記録した、テキストまたはJSON形式のファイル。特定のURLにアクセスすると取得できる、いわば「このバージョンの部品構成表」のようなもの。社内ツールなどでこのファイルを解析し、各コンポーネントの更新状況を追跡するために使われることがある。

【説明】

.NET 9で、`dotnet/installer`リポジトリが`dotnet/sdk`リポジトリに統合（マージ）された。これは.NETチーム内部の開発効率化（ビルドの複雑さの軽減、コードフローの高速化）が目的の変更。

この統合に伴い、`productcommits`ファイルの中身から、これまで含まれていた「`installer`リポジトリのバージョン・コミット情報」の行が削除された。以前はこのファイルを見れば`runtime`・`aspnetcore`・`windowsdesktop`・`sdk`に加えて`installer`のバージョンも分かったが、.NET 9以降は`installer`が独立したリポジトリではなくなったため、その情報自体が存在しなくなった。

【放置したときの影響】

**通常の.NET開発者にはまったく影響しない。** 影響を受けるのは、.NETのビルドプロセスを追跡するために`productcommits`ファイルを直接解析するような、非常に特殊な社内ツールを開発・運用している一部の組織のみ。

もしそのようなツールがあり`installer`の行を前提にパースしていた場合、該当する行が見つからずエラーになる、または「installerのバージョンが取得できない」という形で情報が欠落する可能性がある。

【プロジェクトでの調べ方】

`productcommit`・`installer`リポジトリというキーワードでリポジトリ内を確認したが、dicom-tool-3は.NETランタイム自体のビルドパイプラインを追跡するツールではなく、通常の業務アプリケーション（DICOM関連ツール群）であるため、そもそも`productcommits`ファイルを参照する仕組みは存在しない。**この変更はdicom-tool-3には一切関係しない。**

【改修方法】

対応不要。

【参考記事】
- （特になし）

---

### MSBuild カスタム カルチャ リソースの処理
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/msbuild-custom-culture

【前提知識】

- **カルチャ（Culture）とローカライズとは**
  .NETでいう「カルチャ」とは、`en-US`（アメリカ英語）や`ja-JP`（日本語/日本）のように、言語と地域を表すコードのこと。アプリを多言語対応（ローカライズ）する際、`Resources.ja-JP.resx`のように、リソースファイル名にカルチャコードを埋め込んでおくと、実行時のOSの言語設定に応じて自動的に対応する言語のリソースファイルが選ばれる、という仕組みがある。
- **サテライトアセンブリとは**
  上記のようなカルチャ別リソースは、ビルド時に「サテライトアセンブリ」という、そのカルチャ専用の小さなDLL（例：`ja-JP/MyApp.resources.dll`）としてまとめられる。MSBuildは、プロジェクト内のフォルダ名がカルチャコードのように見えるものを見つけると、自動的に「これはローカライズ用のフォルダだ」と判断してこの処理を行っていた。
- **MSBuildとは**
  `.csproj`ファイルを読み込んで実際のビルド作業（コンパイル、リソース埋め込み、パッケージのダウンロードなど）を行う、.NET/Visual Studioのビルドエンジン。

【説明】

MSBuildは以前、プロジェクト内に`en-US`や`fr-FR`のような「カルチャコードに似た名前」のフォルダを見つけると、それを自動的に「ローカライズ用のカルチャ固有リソースフォルダ」だとみなして処理していた（透過的なカスタムカルチャサポート）。

しかしこれには問題があった。例えばハッシュ値をフォルダ名にしていた場合や、たまたま技術的な理由でカルチャコードと似た名前（2文字-2文字のようなパターン）のフォルダを作っていた場合に、MSBuildが「これはローカライズ用フォルダだ」と誤認識してしまい、意図しないリソースアセンブリが作られてしまう、という予期しない挙動が発生していた。

そこでMSBuild 17.14（.NET SDK 9.0.300および.NET 10 Preview 1以降に対応）からは、この自動検出を既定で無効化し、必要な場合だけ`EnableCustomCulture`プロパティを`true`に明示的に設定する「オプトイン」方式に変更された。

【放置したときの影響】

**ほとんどのプロジェクトには影響しない。** カスタムカルチャの自動検出は、そもそも「意図せず誤検出されて困る」というケースへの対策として無効化されたものなので、素直な多言語リソース構成（Visual Studioの標準的な`.resx`のカルチャサフィックス命名規則、例：`Strings.ja-JP.resx`）を使っている場合は影響を受けない。

影響が出るとすれば、独自に「カルチャコードのような名前のフォルダを作って、それをMSBuildに自動でリソースとして拾わせる」という、やや特殊な運用をしていたプロジェクトのみ。この場合、.NET 10 Preview 1や.NET SDK 9.0.300以降でビルドすると、該当リソースがサテライトアセンブリに含まれなくなり、実行時に該当言語のリソースが見つからず、既定のカルチャ（フォールバック）の文言が表示されてしまう、といった形で気づきにくい形で動作が変わる可能性がある。

【プロジェクトでの調べ方】

dicom-tool-3内を`.resx`ファイルでGlob検索したところ、**リポジトリ全体に`.resx`ファイルは1つも存在しない**ことを確認した。つまりこのプロジェクトはそもそも.NET標準のリソースファイルによる多言語対応（ローカライズ）を行っていない（画面文言は日本語のC#コード内に直接書かれている作り）。したがって、カルチャコードに似たフォルダ名を作ってしまっているリスクもなく、**この変更はdicom-tool-3には影響しない。**

【改修方法】

対応不要。将来、多言語対応のために`.resx`ベースのローカライズを導入する場合は、Visual Studioの標準的な命名規則（`ファイル名.カルチャコード.resx`）に従えば通常は問題にならない。もし独自のカルチャフォルダ運用を行いたい場合のみ、明示的に以下を設定する。

```xml
<PropertyGroup>
  <EnableCustomCulture>true</EnableCustomCulture>
  <!-- カルチャとして誤認識させたくないフォルダがあれば除外する -->
  <CustomCultureExcludeDirectories>SomeHashLikeFolder;abc-def</CustomCultureExcludeDirectories>
</PropertyGroup>
```

【参考記事】
- （特になし）

---

### .NET Framework をターゲットにする際に使用される新しい既定の RID
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/9.0/default-rid

【前提知識】

- **RID（ランタイム識別子, Runtime Identifier）とは**
  `win-x64`、`linux-x64`、`osx-arm64`のように、「どのOS・どのCPUアーキテクチャ向けにビルド/実行するか」を表す識別子。特にネイティブな依存関係（OS固有のDLLなど）を含むアプリを配布可能な形（自己完結型など）にビルドするときに使われる。
- **.NET Framework と .NET（Core）の違い**
  「.NET Framework」はWindows専用の古い実行環境（バージョン4.x系で開発が事実上停止している）で、現在のクロスプラットフォームな「.NET」（.NET 5以降、いわゆる.NET Core系）とは別物。この記事の変更は、.NET Framework（`net48`など）を対象にするプロジェクトに関するもので、dicom-tool-3が使っている`net10.0`のような現代の.NETには直接関係しない話題である点に注意。
- **マルチターゲティングとは**
  1つのプロジェクトファイル（`.csproj`）で`<TargetFrameworks>net48;net10.0</TargetFrameworks>`のように複数のフレームワークを同時にターゲットにすること。同じライブラリを古い.NET Frameworkのアプリと新しい.NETのアプリの両方から使えるようにしたい場合などに使われる。

【説明】

.NET 8で、`net8.0`以降をターゲットにするプロジェクトの復元（NuGetパッケージの依存関係解決）を高速化するため、より小さい「ポータブルRIDグラフ」という仕組みに切り替える変更が行われた。ところがこれが、「同じプロジェクトで.NET Frameworkとモダンな.NETの両方をマルチターゲットしている」環境で問題を引き起こした。復元処理は1回しか行われないのに、.NET Framework側は昔ながらの`win7-x86`/`win7-x64`という既定RIDを使おうとし、.NET（Core）側は新しい縮小されたRIDグラフを使おうとするため、噛み合わずに復元エラー（`NETSDK1047`）が発生することがあった。

この問題を解消するため、.NET 9では「RIDが明示的に指定されていない、.NET Frameworkをターゲットにするプロジェクト」の既定RIDが、`win7-x86`/`win7-x64`から`win-x86`/`win-x64`（新しいポータブルRIDグラフと互換性のある名前）に変更された。

【放置したときの影響】

**dicom-tool-3のような、.NET Frameworkを一切ターゲットにしていないプロジェクトには全く影響しない。** この変更は「.NET Frameworkをターゲットにするプロジェクトの既定RID」の話であり、`net10.0`のみを対象とするプロジェクトの挙動には関係がない。

仮に.NET Frameworkプロジェクトが関係する環境で、`win7-x64`のような古いRID名にコード内やスクリプト内で直接依存していた場合は、ビルド成果物のパス（`bin/win7-x64/...`など）が`bin/win-x64/...`に変わることで、そのパスを決め打ちしているスクリプトが動かなくなる可能性がある（ただし影響範囲は限定的）。

【プロジェクトでの調べ方】

`TargetFramework`のGrep結果より、dicom-tool-3の全プロジェクトは`net10.0`または`net10.0-windows`のみをターゲットにしており、`net48`や`net472`のような.NET Framework向けのターゲットは1つも存在しない。また`RuntimeIdentifier`というキーワードでもGrepしたが、リポジトリ内に明示的なRID指定は見つからなかった。**この変更はdicom-tool-3には無関係。**

【改修方法】

対応不要。

【参考記事】
- （特になし）

---

### ターミナル ロガーは既定です
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/9.0/terminal-logger

【前提知識】

- **MSBuildロガー（ログ出力形式）とは**
  `dotnet build`を実行したときにターミナルに表示される、ビルドの進捗・警告・エラーといった情報を出力する仕組み。従来からある「コンソールロガー」は、ビルドで発生したメッセージを単純に上から下へ流していくだけのシンプルな表示だった。
- **ターミナルロガー（Terminal Logger）とは**
  .NET 8あたりから追加された、より視覚的でリッチなビルド進捗表示。今どのプロジェクトをビルド中か、進捗バーのような表示、色分け、といった機能を持ち、対応するターミナル（Windows TerminalやVS Codeの統合ターミナルなど）ではより見やすい。

【説明】

以前は`dotnet build`のようなコマンドを実行すると、既定で「最小限（minimal）の詳細度」のコンソールロガーが使われ、必要な情報がテキストとして淡々と流れるだけの見た目だった。

.NET 9からは、対話的なターミナルセッション（人間が手元のターミナルで直接コマンドを打っている状況）で`dotnet build`等を実行すると、ターミナルがレイアウトや色付け機能をサポートしている限り、既定で新しい「ターミナルロガー」による表示に切り替わる。一方、シェルスクリプトの一部として実行されていたり、入出力がリダイレクトされていたり、ターミナル側が拡張機能に対応していない場合は、これまで通りの通常のコンソールロガーが使われる（＝CI環境などでは基本的に自動でこれまで通りの動作になる）。

この変更は、開発時のビルド進捗をより分かりやすくする目的で行われた。

【放置したときの影響】

**動作（ビルドの成否）自体には影響しない、見た目だけの変更。** ビルドが失敗する・成功するといった本質的な結果は変わらず、単にターミナルへの出力形式（進捗表示のスタイル）が変わるだけ。

強いて影響があるとすれば、ビルドのコンソール出力をそのままテキストとして解析している自作スクリプトがある場合、ターミナルロガーの出力形式（進捗バーなどの制御文字を含む表示）がそれまでの単純なテキストログの前提と食い違い、解析がうまくいかなくなる可能性がある程度。ただし前述の通り、リダイレクトされている場合は自動的に旧来のロガーが使われるため、CIパイプラインのようにログをファイルにリダイレクトする運用であれば通常は問題にならない。

【プロジェクトでの調べ方】

dicom-tool-3にはCIビルドスクリプトや、`dotnet build`のコンソール出力をパースするような仕組みは見当たらなかった（`.github/workflows`のようなCI定義ファイルも本リポジトリには存在しないことをGlobで確認済み）。開発は各自のターミナルで`dotnet run`等を直接実行する運用であるため、この変更は**単に開発者から見たビルド時の表示が少し変わる**程度の影響にとどまる。

【改修方法】

対応不要（見た目の好みの問題）。もし以前のシンプルな表示に戻したい場合は、以下のいずれかで無効化できる。

```
# 特定のコマンド実行時だけ無効化する
dotnet build --tl:off

# 環境変数ですべてのコマンドについて無効化する
setx MSBUILDTERMINALLOGGER off
```

【参考記事】
- （特になし）

---

### .NET 9 SDK のバージョン要件
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/9.0/version-requirements

【前提知識】

- **.NET SDKとVisual Studioのバージョンの関係**
  .NET SDK（`dotnet`コマンドやコンパイラを含む開発ツール一式）と、それを使うIDEであるVisual Studioは、別々にバージョン管理されているが、互いに対応関係がある。新しいメジャーバージョンの.NET（例：.NET 9）を正式にサポートするには、Visual Studio側も一定以上のバージョンである必要がある、というルールがMicrosoftから公式に定められている。
- **なぜバージョンの組み合わせに制約があるのか**
  新しい.NETのターゲットフレームワーク（`net9.0`など）を正しく認識してIntelliSenseやビルドを行うための情報は、Visual Studio自体のアップデートで提供される。古いVisual Studioは「`net9.0`という新しいターゲットフレームワークが存在すること」自体を知らないため、正しく扱えない。

【説明】

Microsoftの公開サポートルールに従い、.NETの新しいメジャーバージョンがリリースされるたびに、それをフルサポートするために必要なVisual StudioとMSBuildの最小バージョンが、1四半期遅れくらいのペースで更新される。.NET 9のリリースにあたっては、次のようになった。

- .NET SDK 9.0.100自体を読み込む（インストールして認識させる）には、Visual Studio 17.11以降が必要（`net8.0`以前をターゲットにする場合）。
- `net9.0`をターゲットにする場合は、Visual Studio 17.12以降が必要。

以前は、これより古いVisual Studioでも.NET 9.0.100の読み込みができてしまったり、17.11で`net9.0`をターゲットにしても警告なく（実際には正しく動かない可能性があるにもかかわらず）通ってしまっていた。.NET 9以降は、17.10以前では.NET 9.0.100自体が読み込めなくなり、17.11で`net9.0`を使おうとすると`NETSDK1223`という警告が明示的に出るようになった。

【放置したときの影響】

**Visual Studioを使わず、Visual Studio Codeやコマンドラインの`dotnet`コマンドだけで開発している場合は、直接の影響はない。** この変更はあくまで「Visual StudioというIDE側の対応バージョン」についての話であり、`dotnet build`や`dotnet run`自体が動かなくなるわけではない。

Visual Studioを使っている場合、古いバージョン（17.10以前）のままだと.NET 9 SDKを正しく認識できず、プロジェクトの読み込みエラーやIntelliSenseの不具合が起こる可能性がある。

【プロジェクトでの調べ方】

この変更はプロジェクトのソースコードや設定ファイルを調べて分かる類のものではなく、**開発者が使っているVisual Studioのバージョン**に依存する話。dicom-tool-3のリポジトリ内には特定のIDEバージョンを強制する設定（`.vsconfig`など）は見当たらなかった。Visual Studioを使って開発する場合は、「ヘルプ」→「Microsoft Visual Studio について」からバージョンを確認し、17.12以降になっているかを確認するとよい。

【改修方法】

Visual Studioのバージョンが古い場合は、Visual Studio Installerからアップデートする。

```
# コマンドラインでVisual Studio Installerの最新化を促す場合の例（環境による）
winget upgrade Microsoft.VisualStudio.2022.Community
```

Visual Studio Codeや`dotnet` CLIのみで開発している場合は対応不要。

【参考記事】
- （特になし）

---

### .NET Standard 1.x ターゲットに対する警告の発生
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/9.0/netstandard-warning

【前提知識】

- **.NET Standardとは**
  .NET Framework・.NET Core・Xamarinなど、当時バラバラに存在していた複数の.NET系実行環境すべてで動くライブラリを作れるようにするために定められた「共通API仕様」のバージョン規格。`netstandard1.0`〜`netstandard2.1`のように番号が振られており、番号が小さいほど「対応範囲は広いが使えるAPIが少ない（古い）」、番号が大きいほど「使えるAPIは多いが対応範囲は狭まる」という関係にある。
  ライブラリプロジェクトで`<TargetFramework>netstandard2.0</TargetFramework>`のように指定すると、そのライブラリは.NET Standard 2.0に対応するあらゆる実行環境（.NET Framework 4.6.1以降、.NET Core、Xamarin等）から参照できるようになる。
- **`netstandard1.x`が非推奨とされる理由**
  `netstandard1.0`〜`1.6`は、実質的に10年以上前の.NET Framework 4.5相当のごく限られたAPIしか使えない、かなり制約の強い規格。さらにNuGetパッケージとしての配布形態上、依存関係のパッケージ数が膨れ上がりやすく、ビルド時間やダウンロード量が余計に増えるという実務上のデメリットもある。

【説明】

以前は、`netstandard1.0`〜`netstandard1.6`を対象とするプロジェクトを.NET 9 SDKでビルドしても、特に警告は出なかった。

.NET 9からは、これらの古い.NET Standardバージョンを対象とするプロジェクトをビルドすると、次のようなビルド警告が新たに出るようになった。

```
警告 NETSDK1215: 2.0 より前の .NET Standard をターゲットにすることは推奨されなくなりました。
```

これは、より新しく制約の少ない`netstandard2.0`／`netstandard2.1`、あるいは`net6.0`以降への移行を促すために追加された、注意喚起のための警告。

【放置したときの影響】

**警告が出るだけで、ビルドは成功し、アプリの動作も変わらない。** 実害としての影響はほぼゼロで、ビルドログに黄色い警告メッセージが増える程度。ただし、「警告ゼロ」をCIの合格条件にしている場合（`TreatWarningsAsErrors`が有効など）は、この警告によってビルドが失敗するようになる可能性がある。

【プロジェクトでの調べ方】

`TargetFramework`のGrep結果より、dicom-tool-3の全プロジェクトは`net10.0`/`net10.0-windows`のみを対象としており、`netstandard1.x`を対象とするプロジェクトは存在しない。**この変更はdicom-tool-3には影響しない。**

【改修方法】

対応不要。仮に将来`netstandard1.x`を使う理由がある場合（極めて古い環境との互換性が必須、など）は、次のように警告を明示的に抑制できる。

```xml
<PropertyGroup>
  <!-- ターゲットフレームワークのバージョンチェック自体をスキップする -->
  <CheckNotRecommendedTargetFramework>false</CheckNotRecommendedTargetFramework>
  <!-- または警告だけを抑制する -->
  <NoWarn>$(NoWarn);NETSDK1215</NoWarn>
</PropertyGroup>
```

推奨される本来の対応は、`netstandard2.0`以降または`net6.0`以降へターゲットフレームワークを引き上げること。

【参考記事】
- （特になし）

---

### .NET 7 ターゲットに対して出力される警告
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/9.0/net70-warning

【前提知識】

- **.NETのサポートポリシー（LTS / STS）とは**
  .NETには「LTS（長期サポート、Long Term Support）」と「STS（標準サポート、Standard Term Support、旧称Current）」の2種類のリリースがある。LTS（偶数バージョン：6, 8, 10…）は3年間サポートされるのに対し、STS（奇数バージョン：7, 9…）は18ヶ月ほどで短くサポートが終了する。.NET 7はSTSリリースであり、既にサポート終了（EOL: End Of Life）となっている。
- **サポート終了（EOL）とは**
  Microsoftがそのバージョンに対してセキュリティ更新プログラムの提供を停止すること。EOLになったバージョンを使い続けると、新たに見つかった脆弱性が修正されないまま使うことになり、セキュリティリスクが高まる。

【説明】

.NET 7はSTS（短期サポート）リリースであり、サポートが終了している。.NETのバージョンがサポート終了になると、Microsoftはまず翌月にVisual Studio上でそのように表示（マーク）し、それでも半年ほど猶予期間を置いてから、SDKレベルでのビルド時警告を追加する、という段階を踏む方針を取っている。

.NET 8および9 SDKの2024年11月リリース（.NET 8.0.111、8.0.307、8.0.404、9.0.100）から、`net7.0`をターゲットにしているプロジェクトをビルドすると、次の警告が出るようになった。

```
警告 NETSDK1138: ターゲット フレームワーク 'net7.0' がサポート対象外です
```

以前はこの警告は出ず、サポート終了のフレームワークを対象にしていることに気づきにくかった。

【放置したときの影響】

**警告が出るだけで、ビルドや実行自体が失敗するわけではない。** ただし、これは「セキュリティ更新が提供されなくなったフレームワークを使い続けている」という、放置してよい類の警告ではない点に注意が必要（zlibの項目のような「単なる内部実装の変更」とは性質が異なり、実際にサポートが切れているという重要な情報を伝える警告）。

CIで警告をエラー扱いにしている設定（`TreatWarningsAsErrors=true`）の場合は、ビルドが失敗するようになる。

【プロジェクトでの調べ方】

`TargetFramework`のGrep結果より、dicom-tool-3の全プロジェクトは`net10.0`/`net10.0-windows`をターゲットにしており、`net7.0`を対象とするプロジェクトは存在しない。**この変更はdicom-tool-3には影響しない。** それどころか、既に最新の.NET 10を使っているため、むしろこの種の警告とは無縁の状態にある。

【改修方法】

対応不要。仮に何らかの理由で`net7.0`のようなサポート終了フレームワークを使い続ける必要がある場合（本来推奨されない）は、次のように警告を抑制できる。

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <CheckEolTargetFramework>false</CheckEolTargetFramework>
  </PropertyGroup>
</Project>
```

ただし本来の対応は、サポート中のバージョン（.NET 8/9/10などのLTSまたは最新STS）へターゲットフレームワークを引き上げること。

【参考記事】
- （特になし）

## Windows フォーム

### BindingSource.SortDescriptions が null を返さない
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/9.0/sortdescriptions-return-value

【前提知識】

- **`BindingSource`とは**
  WinFormsで、画面のコントロール（`DataGridView`など）とデータ（リストやDataTableなど）を仲立ちする役割のコンポーネント。「このデータをこのグリッドに表示する」という、データバインディングの中心的な部品。
- **`IBindingListView`とは**
  データのソート（並び替え）やフィルタリングをサポートするデータソースが実装するインターフェイス（「このデータソースはこういう操作ができますよ」という約束事の一覧）。`SortDescriptions`プロパティは「今どのカラムでどう並び替えられているか」の情報を保持する。
- **null許容/null非許容とは**
  C#では、変数や戻り値が「`null`（何もない）」を取りうるかどうかを型で区別する仕組みがある（null許容参照型）。「null非許容」と宣言されたものは、本来`null`が返ってくることを呼び出し側は想定しなくてよい、というAPIの約束になる。この約束が守られていないと、想定外の場所で`NullReferenceException`（nullを参照しようとしたときに起きる例外）が起きやすくなる。

【説明】

`BindingSource.SortDescriptions`プロパティは、内部で紐づいているデータソースが並び替え可能（`IBindingListView`を実装している）な場合にその並び替え情報を返すAPI。しかし実装するインターフェイス`IBindingListView.SortDescriptions`自体は「戻り値はnullを返さない」という約束（null非許容）になっているにもかかわらず、以前の`BindingSource.SortDescriptions`の実装は、データソースが並び替え非対応の場合に`null`を返してしまっていた。これはインターフェイスの約束と矛盾する、いわば実装のバグだった。

.NET 9からは、この矛盾を解消するため、データソースが並び替え非対応の場合でも`null`ではなく「空の`ListSortDescriptionCollection`（要素数0のコレクション）」を返すように修正された。

【放置したときの影響】

**「`BindingSource.SortDescriptions`はnullを返すことがある」という前提で書かれたコードがある場合のみ影響する。** 一般的なWinFormsアプリの多くはこのプロパティを直接扱わないため、影響を受けるコードがある可能性自体が低い。

もし影響がある場合、逆に「今までnullチェックしていたコードが不要になる」方向の変化であり、危険なのはむしろ逆のケース——すなわち、以下のように「返ってくる値は常に空でない何らかの要素を含むはずだ」という誤った前提を置いているコードよりは、単純に`null`チェックをしていたコードがそのまま無害に動き続けるだけであることが多い。実害としては小さい部類の変更。

```csharp
// 旧: nullを想定していたコード（.NET 9以降は空コレクションが返るので、
// if文の中には入らなくなるだけで、動作が壊れるわけではない）
if (bindingSource.SortDescriptions == null)
{
    Console.WriteLine("ソート情報なし");
}
else if (bindingSource.SortDescriptions.Count == 0)
{
    Console.WriteLine("ソート情報なし（空）");
}
```

【プロジェクトでの調べ方】

`BindingSource|DataGridView|PictureBox|StatusStrip|ComponentDesigner|IMsoComponent`というキーワードでリポジトリ全体の`.cs`ファイルをGrepしたが、いずれも該当箇所は見つからなかった。dicom-tool-3のWinFormsアプリである`DicomTool.TrayApp`は、タスクトレイに常駐する`NotifyIcon`と、右クリックメニュー用の`ContextMenuStrip`・`MessageBox`のみで構成されたシンプルな作りであり（`services/DicomTool.TrayApp/TrayApplicationContext.cs`参照）、`DataGridView`や`BindingSource`のようなデータバインディング系コントロールは一切使用していない。**この変更はdicom-tool-3には影響しない。**

【改修方法】

対応不要。

【参考記事】
- （特になし）

---

### null 許容注釈を変更
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/9.0/nullability-changes

【前提知識】

- **null許容参照型（Nullable Reference Types）とは**
  C#8.0で導入された機能で、プロジェクトで`<Nullable>enable</Nullable>`を設定すると、`string`型の変数は「nullを入れてはいけない」ものとして扱われ、`string?`と書いた場合だけ「nullが入りうる」ことを表せるようになる。これにより、コンパイラが「ここはnullかもしれないのに、nullチェックせずに使おうとしていますよ」という警告を、実行前（コンパイル時）に教えてくれるようになる。dicom-tool-3の各プロジェクトも軒並み`<Nullable>enable</Nullable>`を設定していることを確認済み。
- **アノテーション（注釈）とは**
  ここでいう「null許容注釈」とは、.NETのライブラリ側のAPI定義（メソッドの引数や戻り値）に対して、「この引数はnullを受け付ける／受け付けない」という情報をあらかじめ付与しておくこと。呼び出す側のコードは、この注釈をもとにコンパイラから警告をもらえる。
- **`IWindowsFormsEditorService`とは**
  WinFormsのデザイナー（Visual StudioでGUIをドラッグ＆ドロップで作る画面）を拡張する際に使う、やや専門的なインターフェイス。プロパティエディタでドロップダウンを表示するといった、カスタムのプロパティエディタを自作するときに使うAPI。

【説明】

.NET 9では、WinFormsのAPIの一部について、null許容注釈（そのパラメーターがnullを受け付けるかどうかの情報）が変更された。具体的には`IWindowsFormsEditorService.DropDownControl(Control)`メソッドの`control`引数が、以前は「null許容」と注釈されていたが、.NET 9からは「null非許容」に変更された。

以前この引数がnull許容とされていたのは実は誤りで、実際にはこのメソッドに`null`を渡すことは論理的に想定されておらず、また実装する側にも「nullが渡されたときにどう振る舞うべきか」の明確なガイダンスがなかった。そこで、実態に合わせて「null非許容」に注釈を修正したという変更。

【放置したときの影響】

**影響は非常に限定的。** `IWindowsFormsEditorService.DropDownControl(Control)`という、WinFormsのカスタムプロパティエディタ（デザイナー拡張）を自作している場合にのみ関係するAPIであり、一般的なWinFormsアプリの開発では触れる機会がほとんどない。

影響がある場合も、実行時の挙動が変わるわけではなく、**コンパイル時にnull許容の変数を渡そうとすると警告（CS8600番台など）が出るようになる**だけ。ビルド自体は成功するため、`TreatWarningsAsErrors`を設定していなければ実害はない。

```csharp
// nullかもしれない変数をそのまま渡すと、.NET 9以降ではコンパイラ警告が出るようになる
Control? maybeNullControl = GetControlOrNull();
editorService.DropDownControl(maybeNullControl); // 警告: nullかもしれない値をnull非許容の引数に渡している
```

【プロジェクトでの調べ方】

`IWindowsFormsEditorService`というキーワードでリポジトリ全体を検索したが、dicom-tool-3では該当する型・メソッドは一切使用されていない。dicom-tool-3のWinFormsアプリ`DicomTool.TrayApp`はデザイナー拡張（カスタムプロパティエディタ）を自作しておらず、そもそもVisual Studioのデザイナー機構自体をあまり使っていない、コードで直接UIを組み立てるシンプルな構成であることを`services/DicomTool.TrayApp/TrayApplicationContext.cs`で確認済み。**この変更はdicom-tool-3には影響しない。**

【改修方法】

対応不要。

【参考記事】
- （特になし）

---

### ComponentDesigner.Initialize が ArgumentNullException をスローする
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/9.0/componentdesigner-initialize

【前提知識】

- **`ComponentDesigner`とは**
  WinFormsのデザイナー（Visual StudioのGUIエディタ）が、フォームやコンポーネントを画面上で編集可能にするために内部で使うクラス。コントロールを自作してVisual Studioのデザイナー上で扱えるようにしたい場合などに、このクラスを継承したカスタムデザイナーを作ることがある。
- **`ArgumentNullException`と`NullReferenceException`の違い**
  どちらも「あるべき値がnullだった」ことに起因する例外だが、`ArgumentNullException`は「メソッドの引数としてnullを渡すこと自体がそもそも許されていない」ということを、メソッドの入り口で即座にはっきり教えてくれる例外。一方`NullReferenceException`は、nullのまま処理が進んでしまい、ずっと後になってから「あれ、これnullだった」と予期しない場所で発生する、原因を突き止めにくい例外。一般に、前者の方が「どこで問題が起きたか」が分かりやすく、望ましいとされる。

【説明】

以前の`ComponentDesigner.Initialize`メソッドは、`component`引数に`null`を渡しても、その場ではエラーにならずに処理を受け付けてしまっていた。しかし、渡された`component`が実際には様々な後続処理で「nullでないこと」を前提に使われていたため、後になって`NullReferenceException`や、原因の分かりにくい別の例外が発生することがあった。

.NET 9からは、`component`引数が`null`の場合、`Initialize`メソッドの入り口で即座に`ArgumentNullException`をスローするように修正された。これにより、「そもそも`null`を渡すこと自体が間違いだった」ということが、問題発生箇所からずっと離れた場所ではなく、まさにその場で分かるようになった。

【放置したときの影響】

**通常のWinFormsアプリ開発者にはほぼ影響しない。** `ComponentDesigner`はWinFormsのデザイナー拡張（カスタムコントロール用のデザイナーを自作する場合など）でのみ使われる、かなり専門的なAPI。

もし影響がある場合、以前は「`null`を渡してもエラーにならず、後で分かりにくい例外が起きる」という状態だったのに対し、新しい動作では「`null`を渡した瞬間にはっきりした例外が起きる」ようになる。これは**エラーが起きるタイミングが早まる（＝原因を特定しやすくなる）方向の変更**であり、正しく`null`以外を渡していたコードには影響がない。

```csharp
// 以前: 実行時にどこかでNullReferenceExceptionが起きて原因追及に苦労する可能性があった
public override void Initialize(IComponent? component)
{
    base.Initialize(component); // .NET 9以降、componentがnullならここでArgumentNullExceptionが即座に発生する
}
```

【プロジェクトでの調べ方】

`ComponentDesigner`というキーワードでリポジトリ全体を検索したが、dicom-tool-3では該当するクラスの継承や使用は一切見当たらなかった。dicom-tool-3のWinFormsアプリ`DicomTool.TrayApp`はカスタムデザイナーを実装しておらず、`ComponentDesigner`を扱う場面自体が存在しない。**この変更はdicom-tool-3には影響しない。**

【改修方法】

対応不要。

【参考記事】
- （特になし）

---

### DataGridViewRowAccessibleObject.Name の開始行インデックス
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/9.0/datagridviewrowaccessibleobject-name-row

【前提知識】

- **`DataGridView`とは**
  WinFormsで表形式のデータ（Excelのような行と列のグリッド）を表示するための代表的なコントロール。
- **アクセシビリティ（Accessibility）とスクリーンリーダーとは**
  視覚障害を持つユーザーなどが、画面の内容を音声で読み上げてもらいながらPCを操作するための支援技術に「スクリーンリーダー」と呼ばれるソフトウェアがある（Windowsの`ナレーター`など）。アプリ側は、各コントロールが「自分は何者で、今どういう状態か」をスクリーンリーダーに伝えるための情報（アクセシブルオブジェクト、`AccessibleObject`）を提供する必要があり、`DataGridViewRowAccessibleObject`はグリッドの「行」に関するこの情報を表すクラス。`Name`プロパティは、その行がどう読み上げられるかに関わる。
- **0始まり(0-based)と1始まり(1-based)の違い**
  プログラミングでは配列やリストのインデックス（何番目かを表す番号）は0から数え始める（0-based）のが一般的だが、人間が日常的に「1行目、2行目」と数えるときの感覚は1から始まる（1-based）。この記事の変更は、まさにこの「プログラム内部の数え方」と「人間が読み上げを聞いたときに自然に感じる数え方」のズレを修正するもの。

【説明】

以前、`DataGridViewRow.DataGridViewRowAccessibleObject.Name`プロパティは、行番号を0始まりでスクリーンリーダーに伝えていた。つまり実際の1行目のデータが「行 0」のように読み上げられてしまい、目でグリッドを見ているユーザーが「1行目」と認識するものと、スクリーンリーダーが読み上げる番号とがズレてしまう、というアクセシビリティ上の問題があった（[GitHub issue #7154](https://github.com/dotnet/winforms/issues/7154)で報告された）。

.NET 9からは、既定でこの行番号が1始まりに変更され、実際の1行目が「行 1」として読み上げられるようになった。これにより、目視の行番号感覚とスクリーンリーダーの読み上げが一致し、より直感的で分かりやすい体験になった。

【放置したときの影響】

**スクリーンリーダーなどの支援技術で行番号の読み上げに依存する自動テストや、行番号を前提にした自動化ツールがある場合にのみ影響する。** 通常のマウス・キーボード操作での見た目や動作には影響がない。

支援技術関連のUIテスト自動化（アクセシビリティツリーの`Name`プロパティを検証しているテストなど）を書いている場合は、期待する行番号がずれて**テストが失敗する**可能性がある。

```csharp
// 例: アクセシビリティ関連のテストで行番号を検証している場合
// 旧: 1行目の名前は "行 0" だった
// 新: 1行目の名前は "行 1" になる（テストの期待値を更新する必要がある）
Assert.Equal("行 0", dataGridView.Rows[0].AccessibilityObject.Name); // .NET 9以降は失敗する
```

【プロジェクトでの調べ方】

`DataGridView`というキーワードでリポジトリ全体の`.cs`ファイルをGrepしたが、dicom-tool-3では`DataGridView`コントロールは一切使用されていない（前述の通り、`DicomTool.TrayApp`は`NotifyIcon`＋`ContextMenuStrip`のみのシンプルなトレイアプリ）。また、アクセシビリティツリーを検証する自動テストの類も見当たらなかった。**この変更はdicom-tool-3には影響しない。**

【改修方法】

対応不要。仮に将来`DataGridView`を導入し、かつ以前の0始まりの挙動を維持したい場合は、プロジェクトのルートに`runtimeconfig.template.json`を作成し、以下のスイッチを設定する。

```json
{
    "configProperties": {
      "System.Windows.Forms.DataGridViewUIAStartRowCountAtZero": true
    }
}
```

【参考記事】
- （特になし）

---

### IMsoComponent のサポートはオプトインです
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/9.0/imsocomponent-support

【前提知識】

- **COM（Component Object Model）とは**
  Windowsに古くからある、異なるプログラム（特に異なるプログラミング言語や異なるプロセス）同士が機能をやり取りするための仕組み。Office製品（Excel、Wordなど）の拡張（アドイン）は、多くがこのCOMベースの仕組みで作られている。
- **`IMsoComponent`/`IMsoComponentManager`とは**
  Microsoft Office関連のアプリケーション拡張（VSTOアドインなど）で使われる、やや特殊なCOMインターフェイス群。WinFormsアプリが「自分はOfficeのメッセージループ（アプリがイベントを処理し続けるための仕組み）と協調して動く1つの部品（コンポーネント）である」と、Office側のコンポーネントマネージャーに登録するために使われる。
- **メッセージループとは**
  Windowsのデスクトップアプリは、キーボード入力・マウス操作・画面の再描画要求などの「イベント（メッセージ）」を延々と受け取っては処理する、というループ構造で動いている。複数のアプリ（例えばWinFormsアプリとOffice）が絡む場合、このループをどう協調させるかが重要になる。

【説明】

以前、WinFormsスレッドが起動すると、既存の`IMsoComponentManager`（Office関連のコンポーネント管理の仕組み）が存在する場合、自動的にそれに登録（統合）しに行く、という挙動が既定で組み込まれていた。しかしこれはCOM呼び出しを伴うためオーバーヘッドが大きく、そもそもOfficeとの連携が不要な大多数のWinFormsアプリにとっては、常にこの登録処理を行うこと自体が無駄なコストだった。

.NET 9からは、この自動登録が既定で行われなくなった。Officeアドイン開発などで本当にこの統合が必要な場合のみ、`Switch.System.Windows.Forms.EnableMsoComponentManager`というスイッチを明示的に`true`に設定することで、以前の動作（オプトイン）に戻せるようになっている。

【放置したときの影響】

**Office関連の拡張機能（VSTOアドインなど）と連携するWinFormsアプリを作っていない限り、実害はない。** それどころか、多くのアプリにとっては単に不要なCOM呼び出しのオーバーヘッドが減る、パフォーマンス上プラスの変更。

もしOfficeアドインなど、実際に`IMsoComponentManager`との統合を前提にしていたアプリがあった場合、.NET 9以降ではこの統合が既定で行われなくなるため、Office側とWinForms側のメッセージループの協調がうまくいかなくなり、キー入力の取りこぼしなど分かりにくい不具合につながる可能性がある。

【プロジェクトでの調べ方】

`IMsoComponent`というキーワードでリポジトリ全体を検索したが、dicom-tool-3では該当する型は一切使用されていない。dicom-tool-3はDICOM（医用画像）関連の業務ツールであり、Microsoft Officeのアドイン開発は行っていない。**この変更はdicom-tool-3には影響しない。**

【改修方法】

対応不要。

【参考記事】
- （特になし）

---

### 新しいセキュリティ アナライザー
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/9.0/security-analyzers

【前提知識】

- **アナライザー（コード分析ツール）とは**
  ビルド時（コンパイル時）にソースコードを自動的に検査し、「このコードはこういう問題を持っている可能性がありますよ」と警告やエラーを出してくれる仕組み。.NETには「Roslynアナライザー」という仕組みがあり、ビルドと同時にこのようなチェックが走る。
  今回追加されたのは`WFO1000`という警告ID(診断ID)を持つ、Windows フォーム専用の新しいアナライザー。
- **デザイナーによるシリアル化とは**
  Visual StudioのGUIデザイナーで、フォームにボタンを配置してプロパティを設定すると、その設定内容が裏側で自動的にコード（`InitializeComponent`メソッド内など）やリソースファイル(`.resx`)として書き出される。この「プロパティの値を自動的にコード/ファイルとして保存する」処理のことを（デザイナーによる）シリアル化と呼ぶ。
- **`DesignerSerializationVisibilityAttribute`/`DefaultValueAttribute`とは**
  自作のコントロールのプロパティに対して、「このプロパティはデザイナーで自動保存してよいか」「初期値は何か」といったことを明示的に指定するための属性（アノテーション）。何も指定しないと、デザイナーは「保存すべきかどうか分からないプロパティ」を、状況次第で誤って（あるいは不必要に）保存してしまうことがある。

【説明】

以前は、自作のWinFormsコントロールや`UserControl`のプロパティに、シリアル化に関する設定（`DesignerSerializationVisibilityAttribute`・`DefaultValueAttribute`・`ShouldSerializeXxx`メソッドなど）を何も付けていなくても、デザイナーはそのプロパティを暗黙のうちに保存してしまうことがあった。これが問題になるのは特に、ユーザーの個人情報や内部設定情報のような「本来は生成されるコードやリソースファイルに書き出されてほしくない機密情報」をプロパティとして持つ独自の業務用コントロール（LOBアプリケーション向けの`UserControl`）を作っていた場合で、意図せずそうした機密データがソースコードやリソースファイルに直接埋め込まれて漏洩する、というセキュリティリスクがあった。

.NET 9からは、コントロールや`UserControl`のプロパティに明示的なシリアル化設定がない場合、`WFO1000`という新しいアナライザーが警告（既定ではエラー）を出すようになった。

```
WFO1000: プロパティ "property" は、プロパティ コンテンツのコード シリアル化を構成しません。
```

これにより、意図せず機密情報がシリアル化されてしまうリスクを、開発の早い段階（コンパイル時）で気づけるようにすることが狙い。

【放置したときの影響】

**自作のカスタムコントロール／`UserControl`を作っており、かつそのプロパティにシリアル化設定を明示していない場合、影響が大きい。** 既定ではこのアナライザーは「エラー」として扱われるため、**該当するプロパティがあると、そのままではビルド自体が失敗する。** これは警告に留まらず、ビルドが止まるという意味で影響度が大きい変更。

```csharp
// NG例: シリアル化に関する設定が何もないpublicプロパティを持つUserControl
public partial class MyUserControl : UserControl
{
    // .NET 9以降、これだけではWFO1000エラーでビルド失敗する
    public string SecretApiKey { get; set; } = string.Empty;
}
```

一方、自作の`UserControl`／カスタムコントロールを作っていない（既存のコントロールをそのまま使うだけ）場合は、このアナライザーの対象にならないため影響はない。

【プロジェクトでの調べ方】

`UserControl`を継承している自作クラス、および`System.Windows.Forms.Control`を継承している自作クラスがないか、リポジトリ内の`.cs`ファイルを検索した。`services/DicomTool.TrayApp`配下には`UserControl`や独自のカスタムコントロールを継承したクラスは存在せず（前述の通り`NotifyIcon`＋`ContextMenuStrip`のみで構成されている）、Visual Studioデザイナー生成のコード（`InitializeComponent`）自体も本プロジェクトには存在しない。**この変更はdicom-tool-3には影響しない。**

念のため、今後の確認方法としては、`class ... : UserControl`や`class ... : Control`のようなパターンでGrepし、該当するクラスがあればそのpublicプロパティに`[DesignerSerializationVisibility]`や`[DefaultValue]`が付いているかを確認するとよい。

【改修方法】

現時点では対応不要。将来カスタムコントロールを作る場合は、機密性を意識しつつ、各publicプロパティに明示的なシリアル化設定を付ける。

```csharp
// 良い例: 明示的にシリアル化しないことを宣言する
[System.ComponentModel.DesignerSerializationVisibility(
    System.ComponentModel.DesignerSerializationVisibility.Hidden)]
public string SecretApiKey { get; set; } = string.Empty;

// または、通常のプロパティとして保存してよい場合は既定値を明示する
[System.ComponentModel.DefaultValue("")]
public string DisplayLabel { get; set; } = string.Empty;
```

やむを得ずアナライザーを抑制する場合（非推奨）は、`.editorconfig`に以下を追加する。

```ini
[*.cs]
dotnet_diagnostic.WFO1000.severity = silent
```

【参考記事】
- （特になし）

---

### DataGridView が null の場合は例外なし
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/9.0/datagridviewheadercell-nre

【前提知識】

- **`DataGridViewHeaderCell`とは**
  `DataGridView`（表形式コントロール）の列ヘッダーや行ヘッダー（左端の灰色のセル）を表すクラス。マウスがヘッダー上に乗った・離れた・クリックされた、といったイベントの内部処理に関するメソッドがいくつか用意されている。
- **`.DataGridView`プロパティが`null`になりうる状況**
  `DataGridViewHeaderCell`はもともと何らかの`DataGridView`に所属して初めて意味を持つオブジェクトだが、まだどの`DataGridView`にも追加されていない、あるいは既に取り外された、といったタイミングでは、この`DataGridView`プロパティが`null`になることがある。

【説明】

`DataGridViewHeaderCell.MouseDownUnsharesRow`、`MouseEnterUnsharesRow`、`MouseLeaveUnsharesRow`、`MouseUpUnsharesRow`という4つの内部的なメソッドは、以前、自身が所属する`DataGridView`プロパティが`null`のタイミングで呼び出されると、`NullReferenceException`（想定外の`null`参照によるクラッシュ的な例外）を発生させてしまっていた。これは「たまたま`null`のタイミングで呼ばれると突然例外で落ちる」という、直感に反する不具合だった。

.NET 9からは、この4つのメソッドは`DataGridView`プロパティが`null`の場合、例外を投げる代わりに単に`false`を返すように修正された。

【放置したときの影響】

**通常のWinFormsアプリ開発では、これらのメソッドを直接呼び出すことはほぼない**（`DataGridView`の内部実装で使われる、いわば裏方のメソッド）。そのため、多くのアプリにはまったく影響しない。

もし何らかの理由でこれらのメソッドを直接呼び出し、かつ「`null`のときに例外が飛んでくること」を前提に`try-catch`で処理を分岐させていたような特殊なコードがある場合は、.NET 9以降は例外が飛ばずに`false`が返るだけになるため、`catch`ブロックの処理が実行されなくなる（＝分岐が変わる）可能性がある。この変更は基本的にバグ修正であり、危険が増える方向の変更ではない。

【プロジェクトでの調べ方】

`DataGridView`というキーワードでのGrep結果より、dicom-tool-3では`DataGridView`関連のクラスは一切使用されていないことを確認済み。**この変更はdicom-tool-3には影響しない。**

【改修方法】

対応不要。

【参考記事】
- （特になし）

---

### PictureBox は HttpClient 例外を発生させます
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/9.0/httpclient-exceptions

【前提知識】

- **`PictureBox`とは**
  WinFormsで画像を表示するための基本的なコントロール。`ImageLocation`プロパティにURLやファイルパスを設定すると、その画像を読み込んで表示してくれる。URLを指定した場合は、内部でネットワーク越しに画像データをダウンロードしてくる。
- **`WebClient`/`WebException`と`HttpClient`/`HttpRequestException`の違い**
  `WebClient`は.NETの初期からあるHTTP通信用の古いAPIで、通信エラー時には`WebException`という例外を投げる。一方`HttpClient`は現在推奨されているモダンなHTTP通信用APIで、通信エラー時には`HttpRequestException`（接続失敗など）や`TaskCanceledException`（タイムアウトなど）を投げる。両者は別の型の例外であるため、`catch`する側は捕まえたい例外の型を正しく合わせる必要がある。dicom-tool-3自体も、DICOM画像のような大きなデータのHTTP通信では基本的に現代的な`HttpClient`ベースのAPI（`HttpClientFactory`など）を使うのが一般的。

【説明】

`PictureBox`がURLから画像を読み込む際、以前は内部の実装として古い`WebClient`を使っていたため、ネットワークエラー（サーバーに繋がらない、タイムアウトするなど）が起きると`WebException`が発生していた。

.NET 9からは、`PictureBox`の内部実装が`WebClient`から`HttpClient`に置き換えられた。これにより、同じネットワークエラーが起きた場合でも、投げられる例外の型が`WebException`ではなく`HttpRequestException`または`TaskCanceledException`に変わった。

【放置したときの影響】

**`PictureBox`のURL画像読み込みでネットワークエラーを`try-catch`している場合に、影響が比較的大きい。** 以前`catch (WebException)`と書いていたコードは、.NET 9以降は`HttpRequestException`や`TaskCanceledException`が飛んでくるようになるため、**その`catch`ブロックが実行されなくなり、例外が握りつぶされずにそのままアプリ全体に伝播してクラッシュする**、という動作の変化が起こりうる。

```csharp
// 旧: WebExceptionだけを想定してキャッチしているコード
try
{
    pictureBox.ImageLocation = "https://example.com/might-not-exist.png";
    pictureBox.Load();
}
catch (WebException ex)
{
    // .NET 9以降、ネットワークエラー時にここには来ない
    // （HttpRequestExceptionやTaskCanceledExceptionが未処理のまま上に伝播する）
    ShowErrorMessage(ex.Message);
}
```

【プロジェクトでの調べ方】

`PictureBox`というキーワードでリポジトリ全体を検索したが、dicom-tool-3では`PictureBox`コントロールは一切使用されていない（`DicomTool.TrayApp`は`NotifyIcon`のみで画像表示コントロールを持たない）。DICOM画像自体の表示は、WinFormsではなくブラウザベースの`frontend/timeline`・`frontend/viewer`（別スタック）側で行っている構成であることも踏まえると、**この変更はdicom-tool-3には影響しない。**

【改修方法】

対応不要。仮に将来`PictureBox`でURLからの画像読み込みを行う場合は、次のように例外の型を更新しておく。

```csharp
try
{
    pictureBox.ImageLocation = imageUrl;
    pictureBox.Load();
}
catch (HttpRequestException ex)
{
    // 接続失敗など
    ShowErrorMessage(ex.Message);
}
catch (TaskCanceledException ex)
{
    // タイムアウトなど
    ShowErrorMessage(ex.Message);
}
```

【参考記事】
- （特になし）

---

### StatusStrip では、別の既定のレンダラーが使用されます
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/9.0/statusstrip-renderer

【前提知識】

- **`StatusStrip`とは**
  WinFormsアプリの画面下部によくある、ステータスバー（「準備完了」「進捗50%」のような表示が並ぶ帯状の領域）を表すコントロール。
- **レンダラー（Renderer）とは**
  ボタンやバーの「見た目の描画方法」を切り替える仕組み。`ToolStripRenderMode.System`は、OSのテーマ（Windowsのシステム標準の見た目）に合わせて描画するモード。それに対する既定のレンダラー（`ToolStripProfessionalRenderer`）は、.NET側で用意された独自の見た目で描画するモードで、こちらの方がフォーカスされている項目の縁取りなどのコントラストが強く、視認性・アクセシビリティに優れる。

【説明】

以前、`StatusStrip.RenderMode`プロパティは既定で`ToolStripRenderMode.System`（Windowsのシステムテーマに準拠した見た目）に設定されていた。しかしこの見た目には、`ToolStripSplitButton`（ステータスバー上の分割ボタン）にフォーカスが当たったときの枠線表示が、コントラスト不足で見えにくいという、アクセシビリティ基準を満たさない問題があった。

.NET 9からは、`StatusStrip`の既定のレンダラーが変更され、`StatusStrip`の見た目が若干変わった（アクセシビリティにより配慮したコントラストの高い見た目になった）。

なお、注記として、この新しい見た目は.NET 9のサービスリリース（マイナーアップデート）や.NET 10 Preview 1で、いったん以前の挙動に戻されている（後日再度見直しが行われた模様）。dicom-tool-3が使っている`net10.0`環境での最終的な既定値は、実際にアプリを動かして確認するのが確実。

【放置したときの影響】

**動作（クリックできる／できないなどの機能面）には影響せず、見た目（色や枠線の描画）が若干変わるだけ。** ステータスバーのピクセル単位の見た目を厳密にテストしているような特殊なケース（スクリーンショット比較の自動テストなど）でなければ、実害はほぼない。

【プロジェクトでの調べ方】

`StatusStrip`というキーワードでリポジトリ全体を検索したが、dicom-tool-3では`StatusStrip`コントロールは一切使用されていない（`DicomTool.TrayApp`のコンテキストメニューは`ContextMenuStrip`であり、画面下部のステータスバーである`StatusStrip`とは別物）。**この変更はdicom-tool-3には影響しない。**

【改修方法】

対応不要。仮に将来`StatusStrip`を使い、以前の見た目に固定したい場合は、明示的に以下を設定する。

```csharp
statusStrip1.RenderMode = ToolStripRenderMode.System;
```

【参考記事】
- （特になし）

## WPF

### `GetXmlNamespaceMaps` 型の変更
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/wpf/9.0/xml-namespace-maps

【前提知識】

- **WPF（Windows Presentation Foundation）とは**
  WinFormsと同様、Windowsデスクトップアプリを作るためのUIフレームワークの1つ。WinFormsより新しく、XAML（XMLベースのマークアップ言語）で画面レイアウトを定義するのが特徴。なお、dicom-tool-3のWindowsデスクトップアプリ（`DicomTool.TrayApp`）はWinFormsで実装されており、**WPFは本リポジトリでは使用されていない。**
- **添付プロパティ（Attached Property）とは**
  WPFにおいて、あるクラスが定義したプロパティを、別のクラスのオブジェクトに対しても設定できるようにする仕組み。`XmlAttributeProperties.XmlNamespaceMaps`は、XAML上でXMLの名前空間マッピング情報をオブジェクトに付与するための、やや専門的な添付プロパティ。
- **`Hashtable`とは**
  キーと値のペアを保持する、`Dictionary`の元祖にあたる古いコレクション型。ここでは「文字列1つ」ではなく「複数のキーと値のペアを持つ構造化されたデータ」を保持するための型として使われている。
- **型キャスト（Type Cast）と`InvalidCastException`とは**
  ある型のデータを別の型として無理やり扱おうとすることを型キャストと呼ぶ。実際のデータの型と、キャストしようとしている型が一致しない場合、`InvalidCastException`という例外が発生する。

【説明】

`XmlAttributeProperties.XmlNamespaceMaps`という添付プロパティの「裏側で実際にデータを保持している型（バッキングプロパティの型）」が、以前は`String`型として宣言されていた。しかし実際には、内部で`dependencyObject.GetValue(XmlNamespaceMapsProperty)`を呼ぶと`Hashtable`型の値が返ってきており、これを取得する`GetXmlNamespaceMaps(DependencyObject)`メソッドの実装は、それを（誤って）`String`型にキャストしようとしていた。型が食い違っているため、このメソッドを呼ぶと必ず`InvalidCastException`が発生してしまうという、実質的に「呼ぶと確実に落ちる」バグだった。

.NET 9からは、このバッキングプロパティの型が実態に合わせて正式に`Hashtable`に変更され、`GetXmlNamespaceMaps(DependencyObject)`はもう`InvalidCastException`を投げなくなった。また対になる`SetXmlNamespaceMaps`メソッドも、引数の型が`String`から`Hashtable`を受け取る形に変わった。

【放置したときの影響】

**この変更は「以前は呼ぶと必ず例外で落ちていたAPIが、正しく動くようになった」というバグ修正であり、基本的に修正前のコードが動いていたはずがない類のAPI。** よって、これまで`GetXmlNamespaceMaps`/`SetXmlNamespaceMaps`をコンパイルは通るが実際には使っていなかった（動かせなかった）コードがあれば、それが初めて正しく機能するようになる、という改善の方向の変更。

一方、もし`String`を渡す前提で`SetXmlNamespaceMaps(DependencyObject, String)`を呼び出しているコードがあれば、そのオーバーロード自体が`Hashtable`引数を取るものに変わっているため、コンパイルエラーになる可能性がある。

【プロジェクトでの調べ方】

`XmlAttributeProperties`・`XmlNamespaceMaps`というキーワードに加え、そもそも`*.xaml`ファイルや`PresentationFramework`への参照がリポジトリ内に存在するかを確認した。dicom-tool-3のプロジェクト一覧（`backend/DicomTool.Api`、`services/DicomTool.Worker`、`services/DicomTool.TrayApp`、`services/DicomTool.DicomScp`、`services/DicomTool.StorageGuard`、`frontend/timeline/DicomTool.Timeline`、`shared/DicomTool.Shared`）のうち、Windowsデスクトップアプリは`DicomTool.TrayApp`のみであり、そのプロジェクトファイル（`services/DicomTool.TrayApp/DicomTool.TrayApp.csproj`）を確認したところ`<UseWindowsForms>true</UseWindowsForms>`のみが設定されており、`<UseWPF>true</UseWPF>`は設定されていない。すなわち**dicom-tool-3にはWPFプロジェクトが1つも存在せず、この変更はまったく関係がない。**

【改修方法】

対応不要（WPFを使っていないため）。

【参考記事】
- （特になし）
