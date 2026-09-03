## SDK と MSBuild

### .NET CLI の `--interactive` はユーザー シナリオの既定で `true` になる
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/dotnet-cli-interactive

【前提知識】

- **`dotnet` CLIとは**
  `dotnet restore`や`dotnet build`のように、ターミナルから`dotnet`コマンドで.NETのビルド・実行・パッケージ管理を行うためのコマンドラインツール。
- **`--interactive`フラグとは**
  NuGetパッケージを取得する際、社内の認証が必要なパッケージソース（プライベートなNuGetサーバーなど）にアクセスする場合、ブラウザを開いてログインを求めることがある。この「ユーザーに対話的に確認を求めてよいか」を制御するのが`--interactive`フラグ。
- **CI/CD環境とは**
  GitHub Actionsなど、人間が張り付いていない自動化されたビルド・テスト・デプロイの実行環境。対話的な確認画面が出ても誰も反応できないため、通常CI/CDでは対話を無効にしておく必要がある。

【説明】

以前は、`dotnet restore`などを実行したとき、`--interactive`を明示的に付けない限り対話機能は常に無効（`false`）だった。認証が必要な状況でも黙って失敗していた。

.NET 10からは、人間が直接ターミナルでコマンドを打っている「ユーザー中心のシナリオ」に限り、`--interactive`が既定で`true`になる。認証が必要な場面で自動的にブラウザ等でログインを促してくれるようになった。一方、CI/CD環境や、出力がリダイレクトされている（＝スクリプトから呼ばれている）場合は、これまで通り既定で`false`のままなので、CI/CDのビルドが対話待ちでハングするような心配はない。

変更理由は、プライベートなNuGetフィードの認証まわりでユーザーがつまずくケースが多かったため、開発者が手元で作業しているときはできるだけ親切に振る舞うようにする、というユーザー体験改善が目的。

【放置したときの影響】

ほとんどの人には影響がない（公式にも「ほとんどのユーザーにアクションは不要」とある）。dicom-tool-3のように認証付きプライベートNuGetフィードを使っていない場合は、挙動が変わったことにすら気づかない。

強いて言えば、「人間が手元のターミナルで`dotnet restore`を打っているのに、パイプやリダイレクトを併用しているため`--interactive`の判定がCI扱いになってしまい、期待した対話が出ない」というような境界ケースで戸惑う可能性がある程度。

【プロジェクトでの調べ方】

- リポジトリ直下の`deploy.bat`・`start-all.bat`・`stop-all.bat`を確認したところ、`dotnet restore`・`dotnet build`・`dotnet publish`等を自動実行している箇所はなかった（`start-all.bat`内に`echo dotnet run`という表示用の文字列があるのみ）。
- GitHub Actions等のCI/CDワークフロー（`.github/workflows/`）もC#プロジェクト用のものは存在しない（`node_modules`配下のサードパーティ製ライブラリのワークフローのみヒット）。
- `nuget.config`もリポジトリ内に存在せず、プライベートな認証付きNuGetフィードは使用していない（NuGet.org等の公開フィードのみ）。
- 以上より、dicom-tool-3では現状この変更の影響はほぼない。将来CIを組む場合は、CI環境では自動的に`false`になる設計なので特別な対応も不要。

【改修方法】

対応不要。明示的に対話を無効化したい場合のみ`dotnet restore --interactive false`のようにフラグを渡す。

【参考記事】

- （公式ドキュメント以外に参考にした記事は特になし）

### `dotnet` CLI コマンドは、コマンドに関連しないデータを stderr に記録します
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/dotnet-cli-stderr-output

【前提知識】

- **標準出力(stdout)と標準エラー出力(stderr)とは**
  コマンドラインプログラムが持つ2種類の出力先。`stdout`は「本来の結果（コマンドが返すべきデータ）」、`stderr`は「エラーメッセージや診断情報など、本来の結果とは別の付随情報」を書き出す場所、という使い分けが伝統的なUnix文化の作法。`command > out.txt`のように`stdout`だけをファイルにリダイレクトしても、`stderr`に出したメッセージは画面に表示され続ける（逆に`2> err.txt`でstderrだけをファイルへ逃がすこともできる）。
- **なぜ使い分けるのか**
  スクリプトが`dotnet`コマンドの出力を後続処理（パース）する場合、`stdout`に余計な前置きメッセージが混ざっていると解析が壊れる。`stdout`を「本当に必要な結果だけ」にしておくことで、パイプやスクリプトからの利用がしやすくなる。

【説明】

以前は、`dotnet`コマンドを実行した際に最初に出る実行メッセージ（起動時の案内メッセージなど）が`stdout`に出力されていた。.NET 10からは、こうしたコマンドの本質的な結果とは言えない付随的なメッセージが`stderr`に出力されるようになった。今後も同様の「本質と関係ないメッセージ」は順次`stderr`に移されていく方針とのこと。

理由は、`stdout`に余計な情報が混ざるとスクリプトでの解析・自動処理がしにくくなるため、`stdout`はできるだけ「コマンドが返すべき本来の結果」だけに保ちたい、というのが背景。

【放置したときの影響】

ほとんどのユーザーには影響しない。唯一注意が必要なのはPowerShellユーザーで、PowerShellは`stderr`への出力があると（実際のプロセス終了コードが0でも）「前のコマンドが失敗した」と判定し`$Error`変数に反映してしまう場合がある。PowerShell 7.2以上を使っていれば問題ないとされている。

【プロジェクトでの調べ方】

- 本プロジェクトの`deploy.bat`・`start-all.bat`・`stop-all.bat`は`.bat`（コマンドプロンプト用）であり、PowerShellの`$Error`のような仕組みには依存していない。
- dicom-tool-3では、`dotnet`コマンドの出力をパースして次の処理に使うようなスクリプト（例：`dotnet build`の出力をgrepして何かを判定する、といった処理）は見当たらなかった。
- よって現時点では影響なし。ただし、開発環境がPowerShellの場合、`dotnet`コマンド実行後に`$?`や`$LASTEXITCODE`を見て成否判定するスクリプトを将来書くなら、PowerShellのバージョンに注意する。

【改修方法】

対応不要。PowerShellを使っていて予期せぬエラー扱いに遭遇した場合は、PowerShellを7.2以上にアップデートする。

【参考記事】

- （特になし）

### .NET ツール パッケージによって RuntimeIdentifier 固有のツール パッケージが作成される
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/dotnet-tool-pack-publish

【前提知識】

- **.NET ツール(`dotnet tool`)とは**
  `dotnet tool install -g dotnet-ef`のように、NuGet経由で配布・インストールできるコマンドラインアプリのこと。csproj側で`<PackAsTool>true</PackAsTool>`を設定してパッケージ化する。
- **`RuntimeIdentifier`(RID)とは**
  `win-x64`や`linux-x64`のように、「どのOS・CPUアーキテクチャ向けにビルドするか」を指定するID。自己完結型（self-contained、.NETランタイムを同梱する）配布や、AOT（事前コンパイル）を行う際に必須の設定。csprojで`<RuntimeIdentifiers>win-x64;linux-x64</RuntimeIdentifiers>`のように複数指定することもある。
- **`dotnet pack`とは**
  プロジェクトをNuGetパッケージ(`.nupkg`)としてビルド・梱包するコマンド。

【説明】

以前は、ツールプロジェクト（`PackAsTool=true`）に`RuntimeIdentifiers`が書いてあっても、`dotnet pack`はそれを無視し、常にランタイムに依存しないプラットフォーム非依存の1種類のツールパッケージだけを作っていた。

.NET 10からは、`RuntimeIdentifiers`が設定されていると、それを見てプラットフォームごとの専用ツールパッケージ（自己完結型・トリミング済み・AOT化されたツールなど）を作成できるようになった。これにより、.NET SDKがインストールされていない環境でも動くツールを配布できる、といったユースケースに対応する。

【放置したときの影響】

このプロジェクトを`dotnet tool`としてパッケージ化・配布する予定がない限り無関係。ただし、もし既存のツールプロジェクトに「たまたま`RuntimeIdentifiers`を書いていた」場合、.NET 10へ更新した途端にパッケージ構成がガラッと変わり（1種類→複数のRID別パッケージ）、意図せずビルド時間や成果物サイズが増える可能性がある。

【プロジェクトでの調べ方】

- `<PackAsTool>`を全csprojに対してgrepしたが、dicom-tool-3内にはヒットなし（`grep -rln "PackAsTool" --include=*.csproj .`が空）。
- `<RuntimeIdentifier>`・`<RuntimeIdentifiers>`も全csprojに対してgrepしたが同様にヒットなし。
- 8つのプロジェクト（`DicomTool.Api`、`DicomTool.Api.Tests`、`DicomTool.Timeline`、`DicomTool.DicomScp`、`DicomTool.StorageGuard`、`DicomTool.TrayApp`、`DicomTool.Worker`、`DicomTool.Shared`）はいずれもASP.NET CoreサービスやBlazor WebAssembly、WinFormsトレイアプリであり、.NETツールとしてNuGet配布するものは1つもない。
- 結論：**dicom-tool-3には現状まったく関係しない**。

【改修方法】

対応不要。

【参考記事】

- （特になし）

### 'loose manifests' から 'workload sets' モードへの既定のワークロード構成
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/default-workload-config

【前提知識】

- **.NETワークロード(workload)とは**
  `dotnet workload install maui`のように、標準の.NET SDKに含まれない追加機能セット（MAUI、Android/iOS開発、Wasmツールなど）を後からインストールする仕組み。
- **ワークロードマニフェスト(manifest)とは**
  「どのワークロードのどのバージョンが利用可能か」を記述した設定ファイル群。
- **loose manifestsモード / workload setsモードとは**
  loose manifestsは、各ワークロードのマニフェストがそれぞれ個別に最新バージョンへ更新されうる（＝互いのバージョンの組み合わせが揃っているとは限らない）モード。一方workload setsモードは、動作確認済みの「バージョンの組み合わせ一式」をセットとして丸ごと固定的に扱うモードで、`dotnet workload update`のような明示的な更新操作をしない限り勝手にバージョンがバラバラに動かない。

【説明】

以前は既定でloose manifestsモードだったため、うっかり`dotnet workload update`を実行すると、互いに組み合わせ動作確認されていないバージョンの新しいマニフェストが個別に入ってしまい、環境によって挙動が不安定になることがあった。

.NET 10からは既定がworkload setsモードになり、SDK自体の更新か明示的な更新コマンドを実行しない限りワークロードのバージョンは変動しなくなった。これにより「昨日は動いたのに今日は動かない」といったワークロード関連の不安定さを防ぎやすくなる。

【放置したときの影響】

このリポジトリで.NETワークロード（MAUI、Android等）を一切使っていなければ影響なし。仮に使っていた場合でも、公式ドキュメント通り「是正措置は不要」であり、問題があれば`dotnet workload config --update-mode manifests`で従来のloose manifestsモードに戻せる。

【プロジェクトでの調べ方】

- dicom-tool-3のプロジェクト構成（ASP.NET Core Web API、Blazor WebAssembly、Temporalワーカー、WinFormsトレイアプリ、DICOM通信サービス）を確認したが、MAUIやAndroid/iOS向けのターゲットフレームワーク（`net10.0-android`等）は存在しない。
- `dotnet workload list`のような追加ワークロードのインストールが必要な要素も見当たらない。
- 結論：**現状このリポジトリには影響しない**。

【改修方法】

対応不要。

【参考記事】

- （特になし）

### `DefineConstants` 評価時に使用できないターゲット フレームワークの場合
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/defineconstants-not-available-at-evaluation

【前提知識】

- **MSBuildの「評価」と「実行(ターゲット実行)」の2段階とは**
  MSBuildがcsprojを処理するとき、大きく分けて「①評価フェーズ：プロパティやItemGroupの値を静的に確定させる」「②実行フェーズ：`Build`や`Restore`などの`Target`（一連の作業手順）を実際に走らせる」という2段階がある。`Condition`属性（例：`<ItemGroup Condition="...">`）は基本的に①の評価フェーズで判定されるが、値によっては②の実行フェーズにならないと確定しないものもある。
- **`DefineConstants`とは**
  C#コンパイラに渡す条件付きコンパイルシンボル（`#if NET10_0_OR_GREATER`のように使う）を溜め込むMSBuildプロパティ。SDKが対象フレームワーク（`net10.0`など）に応じて`NET`・`NET10_0_OR_GREATER`のような値を自動的に追加してくれる。
- **ターゲットフレームワークモニカ(TFM)とは**
  `net10.0`や`net472`のように「どの.NETバージョン/種類向けにビルドするか」を表す文字列。

【説明】

以前は、SDKがターゲットフレームワークに応じて計算する`NET`・`NET9_0_OR_GREATER`・`NETSTANDARD2_0`のような値が、MSBuildの「評価フェーズ」の時点ですでに`DefineConstants`に入っていた。そのため、csproj内で`<ItemGroup Condition="$(DefineConstants.Contains('NET9_0_OR_GREATER'))">`のように、評価フェーズの`Condition`属性で直接チェックするという書き方が(非公式ながら)動いてしまっていた。

.NET 10からは、これらのTFM関連の値の計算処理が評価フェーズではなく「Targetの実行フェーズ」に移された。そのため、評価フェーズで走る`Condition`属性からは、値がまだセットされる前の空の状態しか見えず、上記のような`Condition`チェックが常に偽（false）扱いになってしまう。

変更理由は、`DefineConstants`を直接操作するユーザーコードが誤ってSDKの計算結果を上書きしてしまう事故が起きていたこと、また計算をTargetに移すことでより高度なMSBuildオーケストレーションが可能になることが挙げられている。

【放置したときの影響】

もしプロジェクトファイル内で`Condition="$(DefineConstants.Contains(...))"`のような書き方でTFM判定をしている箇所があると、.NET 10移行後にその`Condition`が常に偽になり、本来含めたかった`PackageReference`やコンパイル対象ファイルが**静かに（エラーも警告も出ずに）取り込まれなくなる**。ビルドは通ってしまうため、実行時になって初めて「あるはずの機能がない」と気づくような、発見しづらいバグにつながる可能性がある。

【プロジェクトでの調べ方】

- 全csprojおよびpropsファイルに対して`DefineConstants`をgrepしたが、dicom-tool-3内にはヒットなし（`grep -rn "DefineConstants" --include=*.csproj --include=*.props --include=*.targets .`が空）。
- 全プロジェクトが単一ターゲットフレームワーク（`net10.0`または`net10.0-windows`）のみを対象としており、マルチターゲット（`net8.0;net10.0`のような複数指定）もしていないため、そもそもTFM別の条件分岐自体が存在しない。
- 結論：**dicom-tool-3には現状影響しない**。

【改修方法】

対応不要。将来マルチターゲット化する場合や、TFMに応じて`PackageReference`を出し分けたくなった場合は、`Condition`で`DefineConstants`を直接チェックせず、以下のように`$([MSBuild]::IsTargetFrameworkCompatible(...))`関数を使う。

```xml
<ItemGroup Condition="$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net9.0'))">
  <PackageReference Include="SomePackage" Version="1.0.0" />
</ItemGroup>
```

【参考記事】

- （特になし）

### コード カバレッジ EnableDynamicNativeInstrumentation の既定値は false です
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/code-coverage-dynamic-native-instrumentation

【前提知識】

- **コードカバレッジとは**
  テストを実行したときに、ソースコードのどの行・分岐が実際に実行されたかを計測する仕組み。`dotnet test --collect:"Code Coverage"`のように実行する。
- **ネイティブ インストルメンテーション（静的/動的）とは**
  C++プロジェクトなど「ネイティブコード」（.NET以外のマシン語コード）に対してカバレッジを取得する仕組み。「静的」はビルド時にあらかじめ計測コードを埋め込む方式、「動的」は実行時にDLLをプロセスへ差し込む(inject)方式。マネージド（C#等の.NETコード）のカバレッジ計測にはこの仕組みは関係ない。

【説明】

以前は、`dotnet test --collect:"Code Coverage"`実行時に「動的ネイティブインストルメンテーション」が既定で有効になっていた。これは、ネイティブコードに対して静的計測が使えない場合の予備手段（フォールバック）として、DLLをプロセスに動的に注入する仕組みだった。

.NET 10ランタイムでセキュリティ強化のための変更が入った結果、この「プロセスへのDLL注入」というやり方自体が標準的でないため失敗するようになり、リンクされたDLLが見つからずプロセスがクラッシュする不具合が発生するようになった（非対話型のセッションではエラー表示なしにクラッシュすることもある）。そのため.NET 10では、この動的ネイティブインストルメンテーションが既定で無効化された。

【放置したときの影響】

**C++等のネイティブコードを含まない、C#のみのソリューションであれば全く影響がない**（マネージドコードのカバレッジ計測方法自体は変わらない）。むしろ計測のオーバーヘッドが減りパフォーマンスが向上する場合がある。

ネイティブコンポーネントを含むソリューションの場合は、動的計測に依存していると、カバレッジ収集時にクラッシュしたりカバレッジが正しく取得できなくなったりする可能性がある。

【プロジェクトでの調べ方】

- dicom-tool-3は`DicomTool.Api.Tests`にxUnitを使ったテストプロジェクトが1つ存在するが、C++やネイティブDLLを含むプロジェクトは存在しない（全てC#の`net10.0`/`net10.0-windows`プロジェクト）。
- `.runsettings`ファイルや、`dotnet test --collect:"Code Coverage"`を呼び出しているCIスクリプト・バッチファイルを検索したが、リポジトリ内にはヒットなし（コードカバレッジ収集自体を現状のプロジェクトでは行っていない）。
- 結論：**現状は無関係**。将来コードカバレッジ収集を導入する場合も、C#のみの構成であれば今回の変更の影響は受けない。

【改修方法】

対応不要。将来ネイティブコンポーネント（例えばfo-dicomのネイティブ依存など、あくまでカバレッジ計測対象としてネイティブコードを含む場合）のカバレッジを取りたくなった場合のみ、以下のいずれかで対応する。

```xml
<!-- .runsettingsやMSBuildプロパティで動的計測を再度有効化する場合 -->
<EnableDynamicNativeInstrumentation>true</EnableDynamicNativeInstrumentation>
```

【参考記事】

- （特になし）

### dnx スクリプトは global.json による SDK の選択を無視する
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/11/dnx-scripts-bypass-global-json

【前提知識】

- **`global.json`とは**
  リポジトリのルート等に置く設定ファイルで、「このフォルダ配下でビルドするときは、この特定バージョンの.NET SDKを使う」とバージョンを固定（ピン留め）するためのもの。複数の.NET SDKが1台のPCに共存しているとき、プロジェクトごとに使うSDKバージョンを揃えるために使う。
- **.NETマルチプレクサー(muxer)とは**
  `dotnet.exe`本体のこと。複数バージョンのSDK/ランタイムがインストールされた環境で、`global.json`の指定などをもとに「実際にどのSDKを使ってコマンドを実行するか」を振り分ける役割を持つ。
- **`dnx`コマンドとは**
  .NETツールをインストールせずにその場で一時的に実行するためのコマンド（`npx`のようなイメージ）。

【説明】

以前は、`dnx`スクリプトは内部で`dotnet dnx`を呼び出しており、`dotnet dnx`はマルチプレクサー経由で動くため、作業ディレクトリの`global.json`が指定するSDKバージョンを尊重していた。しかしこの結果、`global.json`で.NET 10より前のSDKバージョンをピン留めしている環境では、`dnx`コマンド自体が「認識できないコマンド」としてエラーになってしまう不具合があった。

.NET 10 SDK 10.0.302 / 10.0.400以降、および.NET 11 Preview 6以降では、`dnx`・`dnx.cmd`スクリプトは`dotnet --list-sdks`でインストール済みの最新SDKを自力で探し出し、そのSDKを直接呼び出すように変更された。これにより`global.json`によるSDK選択がバイパス（無視）されるようになった。

理由は、`dnx`のようなコマンドは「常に最新のインストール済みSDKで動くべき、バージョン非依存の機能」とみなされているため。古いSDKをピン留めした環境で`dnx`が使えず混乱を招いていた問題を解消する狙い。

【放置したときの影響】

このリポジトリには`global.json`自体が存在しないため直接の影響はない。ただし将来的に「複数の.NET SDKバージョンを使い分けたい」という理由で`global.json`を追加した場合、`dnx`コマンドだけはその固定バージョンを無視して常に最新のインストール済みSDKで動く、という点は覚えておく必要がある。

【プロジェクトでの調べ方】

- リポジトリ直下および各プロジェクトフォルダに`global.json`が存在するか確認したが、`ls global.json`はヒットなし。dicom-tool-3では.NET SDKバージョンの固定は行っていない。
- `dnx`コマンド自体をリポジトリ内のスクリプト（`deploy.bat`等）から使用している箇所もない。
- 結論：**現状は無関係**。

【改修方法】

対応不要。従来通り`global.json`にSDKバージョンを厳密に合わせて`dnx`を使いたい場合は、`dnx`スクリプトの代わりに`dotnet dnx`を明示的に実行する。

【参考記事】

- （特になし）

### dnx.ps1 ファイルが .NET SDK に含まれていない
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/dnx-ps1-removed

【前提知識】

- **shimスクリプトとは**
  本体のプログラムを呼び出すための、薄い「橋渡し用」スクリプトのこと。ここでは`dnx.ps1`（PowerShell用）や`dnx.cmd`（コマンドプロンプト用）が、実体である`dnx`機能を呼び出すための入り口となっている。
- **PowerShellの`--`の特殊扱いとは**
  PowerShellでは、コマンドライン引数中の`--`をPowerShell自身が特別に解釈してしまうことがあり、他のシェル（bash等）と違って`--`をそのまま後続コマンドへ透過的に渡せない場合がある。

【説明】

.NET 10 Preview 7の時点では、Windows版.NET SDKに`dnx.cmd`と`dnx.ps1`の両方が同梱されていた。しかし`dnx.ps1`経由で`dnx`を使うと、PowerShell特有の`--`の扱いにより、`dnx`自身へ渡したいオプションが正しく渡らず、ツール側のオプションとして誤認識されてしまう不具合があった（例：`dnx dotnet-serve -- --help`のつもりが、`dnx`自体のヘルプではなく`dotnet-serve`のヘルプになってしまう、といった挙動）。

.NET 10 GA以降ではこの`dnx.ps1`自体が同梱されなくなった。`dnx.cmd`（コマンドプロンプト用）は引き続き含まれており、こちらを使えば従来通りツールを実行できる。

【放置したときの影響】

PowerShellのプロファイルやスクリプトの中で`dnx.ps1`を明示的にフルパス指定で呼び出しているような特殊なケースでない限り影響はない。通常は`dnx`コマンドをそのまま打てば、Windowsのコマンド解決の仕組みにより`dnx.cmd`が自動的に見つかって実行されるため、多くの場合は気づかないはず。

【プロジェクトでの調べ方】

- dicom-tool-3のスクリプト（`deploy.bat`、`start-all.bat`、`stop-all.bat`）を確認したが、いずれも`.bat`ファイルであり、`dnx.ps1`や`dnx`コマンドへの依存はない。
- PowerShellスクリプト(`.ps1`)自体もリポジトリ内に見当たらない。
- 結論：**現状は無関係**。

【改修方法】

対応不要。もし`dnx.ps1`を直接呼び出していた場合は`dnx.cmd`に切り替える。

【参考記事】

- （特になし）

### ファイル レベルのディレクティブの二重引用符は許可されません
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/file-level-directive-double-quotes

【前提知識】

- **ファイルベースアプリ（file-based app）とは**
  .NET 10で強化された機能で、csprojプロジェクトファイルを作らずに、単一の`.cs`ファイルだけを`dotnet run app.cs`のように直接実行できる仕組み。ちょっとしたスクリプト用途に向いている。
- **ファイルレベルディレクティブ（`#:`構文）とは**
  ファイルベースアプリの`.cs`ファイル冒頭に書く、`#:package Newtonsoft.Json@13.0.3`や`#:property TargetFramework=net10.0`のような特殊なコメント風の記法。これによりcsprojを書かずにパッケージ参照やMSBuildプロパティを指定できる。

【説明】

以前（.NET 10 RC2以前のプレビュー版）は、このディレクティブの中で二重引用符`"`を使っても、エラーにはならないが期待通りにも動かない（特殊文字としてそのままMSBuildにエスケープされて渡ってしまい、例えば`#:property Prop="my test"`が`<Prop>&quot;my test&quot;</Prop>`のような意味不明な値になる）という中途半端な状態だった。

.NET 10 GA以降では、ディレクティブ内に二重引用符が含まれているとビルド時エラーになるよう変更された。これは「今は動かないが動きそうに見える」曖昧な状態を解消し、将来的に引用符サポートを追加する余地を残すための、意図的なエラー化。

【放置したときの影響】

ファイルベースアプリ（`dotnet run foo.cs`のような単一ファイル実行）を使っていない、通常のcsprojベースのプロジェクトには一切影響がない。ファイルベースアプリの`#:`ディレクティブ内で、スペースを含む値を引用符で囲もうとしていた場合にのみビルドエラーになる。

【プロジェクトでの調べ方】

- dicom-tool-3は`DicomTool.Api`・`DicomTool.Worker`など、すべて通常の`.csproj`ベースのプロジェクトで構成されており、`dotnet run *.cs`のようなファイルベースアプリの実行形態は使われていない。
- リポジトリ内で`#:`から始まるディレクティブを持つ`.cs`ファイルがないか検索したが該当なし。
- 結論：**現状は無関係**。

【改修方法】

対応不要。仮にファイルベースアプリを使うことになった場合は、`#:`ディレクティブでは引用符を使わない。スペースなど引用符が必要な値は、`Directory.Build.props`にプロジェクトメタデータとして書くか、`dotnet project convert`でファイルベースアプリを通常のプロジェクトに変換する。

【参考記事】

- （特になし）

### `dotnet new sln` 既定値は SLNX ファイル形式
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/dotnet-new-sln-slnx-default

【前提知識】

- **ソリューションファイル(.sln)とは**
  複数のcsprojプロジェクトを束ねて管理するためのファイル形式。Visual Studio等で「ソリューションを開く」ときに読み込まれる。
- **SLNX形式とは**
  .NET SDK 9.0.200から使えるようになった、新しいソリューションファイル形式。従来の`.sln`はテキストとして人間には読みにくい独自形式だったが、`.slnx`はシンプルなXML形式で書かれており、可読性・差分の見やすさ（gitでのコンフリクト解消のしやすさ含む）に優れる。
- **`dotnet new sln`とは**
  新しい空のソリューションファイルを生成するCLIコマンド。

【説明】

以前は、`dotnet new sln`を実行すると、旧来の`.sln`形式（テキストベースだが独自形式で読みにくい）のファイルが生成されていた。

.NET 10からは、`dotnet new sln`の既定の出力形式が`.slnx`（XML形式）に変わった。SLNX形式は.NET 9.0.200から主要な.NETツールで十分にサポートされるようになっており、開発者にとって管理しやすい形式であることが実証されたため、この変更を通じてSLNX形式の利用を推奨する狙いがある。

【放置したときの影響】

**dicom-tool-3自身が既にこの変更を先取りする形で`.slnx`形式（`DicomTool.slnx`）を採用しているため、この項目は「今後新しく`dotnet new sln`でソリューションを作る際の既定値が変わる」という話であり、既存の`DicomTool.slnx`には何の影響もない**。強いて言えば、今後サブプロジェクト用に別のソリューションファイルを追加作成する際、明示的に`--format sln`を付けない限り自動的に`.slnx`になる、という点を知っておけばよい。

【プロジェクトでの調べ方】

- `DicomTool.slnx`の中身を確認したところ、既に

  ```xml
  <Solution>
    <Folder Name="/backend/">
      <Project Path="backend/DicomTool.Api/DicomTool.Api.csproj" />
      ...
  ```

  というSLNX形式（XML形式）で書かれており、`backend`・`frontend/timeline`・`services`・`shared`の各フォルダに8つのプロジェクトが登録されている。
- 旧来の`.sln`ファイルはリポジトリ内に存在しない。
- 結論：**この変更は既に先取り済みであり、追加対応は不要**。

【改修方法】

対応不要。もし何らかの理由で`.sln`形式が必要になった場合は、`dotnet new sln --format sln`を使う。

【参考記事】

- （特になし）

### `dotnet package list` 復元を実行します
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/dotnet-package-list-restore

【前提知識】

- **`dotnet package list`とは**
  プロジェクトが参照しているNuGetパッケージの一覧を表示するコマンド。
- **復元(restore)とは**
  csprojに書かれた`PackageReference`を元に、実際にNuGetパッケージ本体をダウンロードしてキャッシュに配置し、依存関係グラフを解決する処理。`dotnet build`の内部では自動的に復元が走るが、`dotnet package list`のような一覧表示系のコマンドは、必ずしも復元とセットではなかった。

【説明】

以前は、`dotnet package list`はその場にある情報（過去に復元した結果）を元に一覧を表示するだけで、復元を自動では行わなかった。そのため、パッケージを追加・変更した直後に`dotnet package list`を実行すると、最新の状態が反映されていない古い情報が表示されることがあった。

.NET 10からは、`dotnet package list`が一覧表示の前に自動的に復元を実行するようになり、常に最新の状態を反映した正確な一覧が得られるようになった。復元が失敗した場合は一覧を表示せず、エラーメッセージ（プレーンテキスト/JSON両対応）をログに記録する。

【放置したときの影響】

多くの場合、単に「より正確な情報が見られるようになった」という改善であり、悪影響は少ない。ただし、以下のようなケースでは注意が必要。

- NuGetの復元にネットワークアクセスが必要な環境で、オフラインだと`dotnet package list`自体が復元エラーで失敗するようになる（以前は復元しないので実行できていた）。
- CI等で`dotnet package list`をパイプライン内の情報表示目的だけに使っていた場合、余計な復元処理が走ることで実行時間がわずかに伸びる可能性がある。

【プロジェクトでの調べ方】

- dicom-tool-3内で`dotnet package list`を使用しているスクリプトやドキュメントがないか検索したが、該当箇所はなかった（開発者が手動でパッケージ一覧を確認したいときに個別に実行する程度の使い方が想定される）。
- 自動化されたスクリプトから呼ばれていないため、影響は軽微。

【改修方法】

対応不要。もし復元をスキップしたい場合は`dotnet package list --no-restore`を使う。

【参考記事】

- （特になし）

### `dotnet restore` 推移的なパッケージを監査する
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/nugetaudit-transitive-packages

【前提知識】

- **NuGetAuditとは**
  .NET 8で導入された機能で、`dotnet restore`実行時に、参照しているNuGetパッケージに既知のセキュリティ脆弱性がないかを自動チェックし、あれば警告（`NU1901`〜`NU1904`など）を出す仕組み。
- **直接参照(direct)と推移的な参照(transitive)の違い**
  「直接参照」は自分のcsprojに`<PackageReference Include="X" />`と明示的に書いたパッケージ。「推移的な参照」は、自分は直接書いていないが、直接参照しているパッケージがさらに依存している（芋づる式に付いてくる）パッケージのこと。例えば`A`を直接参照すると、`A`が依存する`B`・`C`は推移的な参照になる。

【説明】

以前（.NET 8導入時）は、NuGetAuditは既定で「直接参照」のみをチェックしていた（`NuGetAuditMode=direct`）。.NET 9のプレビュー版では一時的に全パッケージ（`all`）に変わったが、正式リリース版では`direct`に戻された、という経緯がある。

.NET 10以降をターゲットとするプロジェクトでは、既定値が改めて`all`に変更された。つまり、自分が直接書いていない「推移的な依存パッケージ」に脆弱性が見つかった場合も、`dotnet restore`実行時に警告が出るようになる。.NET 9以下をターゲットとするプロジェクトでは`direct`のままなので変わらない。

理由は単純で、脆弱性のあるパッケージは、直接参照であっても推移的参照であっても、実際にそのコードがアプリに組み込まれて悪用されうる点は変わらないため。

【放置したときの影響】

- もしプロジェクトで`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`を設定している場合、これまで表面化しなかった推移的パッケージの脆弱性警告が**復元エラー（ビルド失敗）**に化ける可能性がある。
- そうでない場合も、`dotnet restore`実行時のログに大量の脆弱性警告が新たに出るようになり、本当に重要な警告が埋もれてしまう恐れがある。
- 特にdicom-tool-3のように多くのNuGetパッケージ（fo-dicom、Npgsql、Temporalio、HotChocolate等）に依存する構成では、それらがさらに依存する推移的パッケージの数が多く、警告が出る可能性は相応にある。

【プロジェクトでの調べ方】

- 全csprojに`TreatWarningsAsErrors`の設定がないか確認したところ、いずれのプロジェクトにも設定はなかった（`grep`で該当なし）。そのため、仮に推移的パッケージの脆弱性警告が出ても、現状はビルドエラー化はしない見込み。
- 実際に警告が出るかどうかを確認するには、リポジトリのルートで以下を実行し、`NU190x`系の警告が出力されるかを見るのが手っ取り早い。

  ```bash
  dotnet restore DicomTool.slnx
  ```

- また`dotnet nuget why <パッケージ名>`コマンドを使うと、警告が出た推移的パッケージがどの直接参照パッケージ経由で入ってきたかを追跡できる。

【改修方法】

- 警告が出ても直ちに対応不要な場合が多いが、`TreatWarningsAsErrors`を将来有効化する予定があるなら、以下のように監査警告だけは除外しておくと安全。

  ```xml
  <PropertyGroup>
    <WarningsNotAsErrors>NU1901;NU1902;NU1903;NU1904;$(WarningsNotAsErrors)</WarningsNotAsErrors>
  </PropertyGroup>
  ```
- 実際に脆弱性があると分かったパッケージは、`dotnet nuget why`で経路を特定し、可能であれば直接参照へ昇格させて新しいバージョンに上げる。
- 個別の警告を意図的に無視したい場合は、`NuGetAuditSuppress`をcsprojに追加する。

  ```xml
  <ItemGroup>
      <NuGetAuditSuppress Include="https://github.com/advisories/xxxx" />
  </ItemGroup>
  ```
- 推移的パッケージまでの監査が過剰だと感じる場合は、プロジェクトを`direct`に戻すこともできる。

  ```xml
  <PropertyGroup>
    <NuGetAuditMode>direct</NuGetAuditMode>
  </PropertyGroup>
  ```

【参考記事】

- （特になし）

### `dotnet tool install --local` 既定でマニフェストを作成する
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/dotnet-tool-install-local-manifest

【前提知識】

- **ローカルツール(local tool)とは**
  `dotnet tool install -g`のようにPC全体にインストールするグローバルツールとは異なり、特定のリポジトリ配下だけで使う.NETツール。チームメンバー間でツールのバージョンを揃えるのに使う。
- **ツールマニフェスト(`.config/dotnet-tools.json`)とは**
  「このリポジトリでローカルツールとして何がインストールされているか」を記録するJSONファイル。`dotnet tool install --local <ツール名>`を実行する前に、通常`dotnet new tool-manifest`で明示的に作成しておく必要があった。

【説明】

以前は、`.config/dotnet-tools.json`（マニフェスト）が存在しないフォルダで`dotnet tool install --local`を実行すると、「マニフェストファイルが見つかりません」というエラーで失敗し、先に`dotnet new tool-manifest`を手動で実行する必要があった。

.NET 10からは、`--create-manifest-if-needed`オプションが既定で有効になり、マニフェストが存在しない場合は自動的に作成されるようになった（可能であればリポジトリのルートに作成される）。

【放置したときの影響】

このリポジトリでは影響が非常に小さい。強いて言えば、「マニフェストがないことを意図的にエラーとして検知し、マニフェスト作成を促すガイドを表示する」ような独自のスクリプトを書いていた場合、そのエラーが発生しなくなり、ガイドが表示されなくなる可能性がある程度。

【プロジェクトでの調べ方】

- `.config/dotnet-tools.json`がリポジトリ内に存在するか確認したところ、`find . -iname "dotnet-tools.json"`および`.config`フォルダの検索は共にヒットなし。dicom-tool-3では現状ローカルツールのマニフェスト自体を使っていない（`dotnet ef`等はグローバルインストールもしくはプロジェクト参照の`Microsoft.EntityFrameworkCore.Design`パッケージ経由で利用している）。
- 結論：**現状はローカルツール自体を使っていないため無関係**。将来`dotnet tool install --local`を使い始めた場合、マニフェストが自動生成される点だけ覚えておけばよい。

【改修方法】

対応不要。従来通りエラーで気づきたい場合は、`dotnet tool install --local <ツール名> --create-manifest-if-needed=false`を使う。

【参考記事】

- （特になし）

### `dotnet watch` stdout ではなく stderr にログを記録する
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/dotnet-watch-stderr

【前提知識】

- **`dotnet watch`とは**
  ソースコードの変更を検知して自動的に再ビルド・再実行してくれる開発用コマンド（`dotnet watch run`のように使う）。開発中にファイルを保存するたびに手動で`dotnet run`し直さなくて済む。
- **LSP(Language Server Protocol)・MCPサーバーとは**
  エディタとの通信や、AIツールとの通信のように、`stdout`をプログラム間のメッセージのやり取り専用チャネルとして予約して使うタイプのアプリケーション。こうしたアプリを実行する際に`stdout`に無関係なログが混ざると通信が壊れてしまう。

【説明】

以前は、`dotnet watch`が出すログメッセージ（「ファイルの変更を検知しました」「再起動します」など）は`stdout`に出力されていた。.NET 10からは、これらのログメッセージが`stderr`に出力されるように変更された。

これは「`dotnet`のCLIコマンドは、`stdout`チャネルを（アプリ本体の出力のために）隠さない・占有しないようにする」という一連の方針の一部（項目2の変更とも同じ流れ）。特にLSPサーバーやMCPサーバーのように`stdout`を通信専用に使うアプリを`dotnet watch`経由で動かす場合に、`dotnet watch`自身のログが混ざらないようにする狙いがある。

【放置したときの影響】

通常の開発（ターミナルで`dotnet watch run`して目視でログを見る）では、ターミナル上の見え方に実質差はなく影響なし。影響が出るのは、`dotnet watch`の`stdout`出力だけをファイルにリダイレクトしてログとして保存・解析しているようなスクリプトがある場合で、その場合はログメッセージが記録されなくなる。

【プロジェクトでの調べ方】

- dicom-tool-3の開発フローを確認したところ、`start-all.bat`は各サービスを`dotnet run`で起動する運用であり、`dotnet watch`自体は現状のスクリプトからは呼ばれていない（`start-all.bat`内の`dotnet run`表示はコメント/ヘルプ表示目的）。
- `dotnet watch`の出力をリダイレクトして解析するようなツールも見当たらない。
- 結論：**現状は無関係**。開発者が手元で`dotnet watch run`を使うこと自体はよくあるが、その場合もターミナルでの見た目は変わらない。

【改修方法】

対応不要。`stdout`だけをリダイレクトしてログを保存したい場合は、`2>&1`を使って`stderr`を`stdout`にまとめてからリダイレクトする。

```bash
dotnet watch run 2>&1 | tee watch.log
```

【参考記事】

- （特になし）

### project.json でサポートされていません `dotnet restore`
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/dotnet-restore-project-json-unsupported

【前提知識】

- **`project.json`とは**
  .NET Core登場初期（〜.NET Core 1.0のプレビュー版まで）に使われていた、プロジェクト定義ファイルの旧形式。現在標準の`.csproj`（`PackageReference`形式）に2017年に完全に置き換えられ、以来非推奨扱いになっている、非常に古い遺物。

【説明】

以前は、`dotnet restore`コマンドが（互換性維持のため）`project.json`ベースの古いプロジェクトの依存関係も復元できていた。.NET 10からは、`dotnet restore`が`project.json`ベースのプロジェクトを単に無視するようになり、復元処理の対象外になった。

`project.json`は2017年に`PackageReference`形式へ完全に置き換えられ、移行用コマンドの`dotnet migrate`も.NET Core 3.0時点で既にCLIから削除されている、いわば「移行期間がとうに終わった」古い機能であり、今回でサポートが完全に打ち切られた形。

【放置したときの影響】

**dicom-tool-3のような2020年代以降に作られたモダンなプロジェクトには、そもそも`project.json`ファイルが存在しないため一切影響がない**。

【プロジェクトでの調べ方】

- リポジトリ全体を`project.json`というファイル名で検索したが該当なし。全プロジェクトが標準の`.csproj`（`PackageReference`形式）で構成されている。
- 結論：**完全に無関係**。

【改修方法】

対応不要。

【参考記事】

- （特になし）

### SHA-1 フィンガープリントのサポートは非推奨になりました `dotnet nuget sign`
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/dotnet-nuget-sign-sha1-deprecated

【前提知識】

- **`dotnet nuget sign`とは**
  作成したNuGetパッケージ(`.nupkg`)に、証明書を使ってデジタル署名を付けるコマンド。署名により「このパッケージは確かにこの発行者が作ったものであり、改ざんされていない」ことを検証できるようになる。
- **証明書のフィンガープリント（指紋）とは**
  証明書の内容から計算したハッシュ値で、証明書を一意に特定するための短い識別子。`--certificate-fingerprint`オプションで、署名に使う証明書をこの値で指定する。
- **SHA-1とSHA-2(SHA256等)とは**
  どちらもハッシュ関数（データから固定長の値を計算するアルゴリズム）の種類。SHA-1は古く、現在は「衝突攻撃」（異なるデータから同じハッシュ値を意図的に作り出す攻撃）に対して弱いことが知られており、暗号学的な安全性の観点から非推奨とされている。SHA-2ファミリ（SHA256/SHA384/SHA512）がその後継として推奨される。

【説明】

以前（.NET 9まで）は、`dotnet nuget sign`はSHA-1とSHA-2どちらのフィンガープリントも受け付けており、SHA-1を使った場合は「安全でないハッシュアルゴリズムを使っている」という警告(`NU3043`)が出るだけで、署名処理自体は成功していた。

.NET 10からは、この`NU3043`警告がエラーに格上げされ、SHA-1フィンガープリントを使った署名操作自体がブロックされるようになった。より強固なセキュリティ基準を強制するための変更。

【放置したときの影響】

**自分たちでNuGetパッケージを作成・署名して配布する運用をしていない限り無関係**。dicom-tool-3のように、社内で使う学習用アプリケーションであり、自作のNuGetパッケージを外部に配布する予定がない場合はまず関係しない。

もし将来、自作ライブラリ（例えば`DicomTool.Shared`）をNuGetパッケージとして切り出して署名付きで配布する、といった話になった場合にのみ関係してくる。

【プロジェクトでの調べ方】

- リポジトリ内で`dotnet nuget sign`・`certificate-fingerprint`をgrepしたが該当なし。
- 各csprojの`IsPackable`設定を確認したところ、`DicomTool.Api.Tests`のみ`<IsPackable>false</IsPackable>`が明示されている以外、他のプロジェクトも配布用NuGetパッケージとしての署名運用は行っていない。
- 結論：**現状は無関係**。

【改修方法】

対応不要。将来自作パッケージに署名する場合は、SHA256（推奨）・SHA384・SHA512のいずれかのフィンガープリントを使用する。

【参考記事】

- （特になし）

### MSBUILDCUSTOMBUILDEVENTWARNING エスケープ ハッチの削除
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/custom-build-event-warning

【前提知識】

- **MSBuildのビルドイベント(`BuildEventArgs`)とは**
  MSBuildがビルド中に発生させる「メッセージ」「警告」「エラー」等のイベントを表す基底クラス。ビルドログをカスタム処理するツール（カスタムロガー）を自作する際に、このクラスを継承して独自のイベント型を作ることがある。
- **エスケープハッチ(escape hatch)とは**
  ある機能を無効化・迂回するための、一時的な抜け道・回避策として用意されたオプションや環境変数のこと。恒久的な仕様ではなく、あくまで暫定的な救済措置という位置づけであることが多い。
- **環境変数とは**
  OS上でプロセスに渡される、キーと値のペアの設定情報。`MSBUILDCUSTOMBUILDEVENTWARNING=1`のようにセットしておくと、対応するツール側がその値を読んでいれば挙動を変えられる。

【説明】

もともと.NET 8で、独自の`CustomBuildEventArgs`派生クラス（自作のビルドイベント型）の扱いに関するセキュリティ上の警告・制限が導入された経緯がある（元の破壊的変更は`custombuildeventargs`ページ参照）。その際、暫定的な救済措置として`MSBUILDCUSTOMBUILDEVENTWARNING`環境変数を設定すれば、従来通りカスタムビルドイベントの処理を許可できる「エスケープハッチ」が用意されていた。

.NET 10では、この暫定的なエスケープハッチ自体が完全に削除された。この環境変数を設定していても、もう何の効果もなくなる。

理由は、そもそもこの環境変数はあくまで一時的な回避策として提供されていたものであり、正式な恒久対応ではなかったため。

【放置したときの影響】

- 自作のMSBuildロガーやビルド拡張で`CustomBuildEventArgs`を独自に継承したクラスを作り、かつ`MSBUILDCUSTOMBUILDEVENTWARNING`環境変数に依存して動かしていた場合のみ影響がある。
- dicom-tool-3のような一般的なASP.NET Core / Blazor / WinFormsアプリケーションでは、自作のMSBuildロガーやカスタムビルドイベント型を実装することは通常ない。

【プロジェクトでの調べ方】

- リポジトリ全体を`MSBUILDCUSTOMBUILDEVENTWARNING`・`CustomBuildEventArgs`でgrepしたが、どちらも該当なし。
- カスタムMSBuildタスクやロガー(`.targets`/`.props`ファイルで独自の`ITask`/`ILogger`実装を定義している箇所)も見当たらない。
- 結論：**完全に無関係**。

【改修方法】

対応不要。仮にカスタムビルドイベントを拡張したくなった場合は、新しく用意された`ExtendedCustomBuildEventArgs`等の組み込み拡張イベントを使う。

【参考記事】

- （特になし）

### MSBuild カスタム カルチャ リソースの処理
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/msbuild-custom-culture

【前提知識】

- **カルチャ(culture)とは**
  `en-US`（アメリカ英語）や`ja-JP`（日本語）のように、言語・地域を表すコード。.NETの多言語対応（ローカライズ）で、`Resources.ja-JP.resx`のようなファイル名の一部として使われる。
- **カルチャ固有のリソースディレクトリとは**
  多言語対応アプリで、`bin/Debug/net10.0/ja-JP/MyApp.resources.dll`のように、言語ごとのリソースDLLを言語コード名のフォルダに分けて配置する慣習。MSBuildはビルド時に、フォルダ名が「カルチャコードっぽい」名前だと自動的にこの「言語別リソース用フォルダ」として特別扱いしていた。
- **「カルチャコードっぽい名前」による誤検出の問題**
  例えば`bin`配下に`as`（Assamese語のカルチャコードとたまたま一致）のような技術的な名前のディレクトリを作っていた場合、それが意図せずカルチャ固有リソースディレクトリとして扱われてしまう、という誤爆が起きうる。

【説明】

以前は、MSBuildが「フォルダ名がカルチャコードに似ている」というだけで、自動的にそのフォルダをカルチャ固有のリソースディレクトリとして扱っていた。この自動判定により、実際には多言語リソースとは無関係な、たまたまカルチャコードと同じ名前のフォルダ（ハッシュ値ベースの名前や技術的な略称など）が誤って巻き込まれ、意図しないリソースアセンブリが生成されてしまう不具合があった。

MSBuild 17.14（.NET 9.0.200〜9.0.300、および.NET 10 Preview 1に対応）以降では、このカスタムカルチャ処理が既定で無効になり、`EnableCustomCulture`プロパティを明示的に`true`にした場合のみ有効になるオプトイン方式に変更された。

【放置したときの影響】

- **多言語対応（ローカライズ）機能を使っていないプロジェクトには影響がない。**
- 多言語対応をしているプロジェクトで、フォルダ名によるカルチャ自動検出に依存していた場合、.NET 10移行後にカルチャ固有リソースが正しく認識されずビルド成果物からリソースDLLが漏れる可能性がある。

【プロジェクトでの調べ方】

- dicom-tool-3全体で`.resx`ファイル（多言語リソースファイル）を検索したが該当なし（`find . -iname "*.resx"`が空）。
- `EnableCustomCulture`・`CustomCultureExcludeDirectories`のいずれもcsproj/propsファイルに設定なし。
- そもそも本プロジェクトはUI文言を含めて日本語のみを対象としており、多言語ローカライズの仕組み自体を導入していない。
- 結論：**現状は無関係**。

【改修方法】

対応不要。将来多言語対応を行いカルチャ固有リソースディレクトリの自動認識に依存したくなった場合は、以下をcsprojに追加する。

```xml
<PropertyGroup>
  <EnableCustomCulture>true</EnableCustomCulture>
</PropertyGroup>
```

【参考記事】

- （特になし）

### NuGet によって削除された直接参照に対して NU1510 が発生する
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/nu1510-pruned-references

【前提知識】

- **パッケージの「プルーニング(pruning、剪定)」とは**
  ターゲットフレームワーク（例：`net10.0`）が標準で提供している機能と重複するNuGetパッケージ参照を、ビルド時に自動的に無視・除外する仕組み。例えば`System.Text.Json`は現在.NETランタイムに標準搭載されているため、`net10.0`をターゲットにしている場合わざわざ`PackageReference`で明示的に参照しなくても、SDKが提供する版が使われる。
- **`PackageReference`とは**
  csprojで`<PackageReference Include="パッケージ名" Version="バージョン" />`のように書く、NuGetパッケージへの依存を宣言する記法。
- **NU1510とは**
  「このパッケージ参照は不要（プラットフォームが既に提供しているため）」であることを知らせるNuGetの警告番号。

【説明】

以前は、ターゲットフレームワークが既に提供している機能と重複する`PackageReference`があっても、SDKはその中身を単に無視するだけで、警告は出さなかった（書いても実害はないが放置されがちだった）。

.NET 10 SDK以降では、プルーニングが有効な状態で、かつプロジェクトが.NET 10以降をターゲットにしている場合、こうした「もう不要になった直接パッケージ参照」に対して`NU1510`警告が出るようになった。

理由は、開発者のメンテナンス負担軽減。不要な参照を早期に見つけて削除することで、パッケージの復元・更新の手間やダウンロード時間を減らし、より綺麗なビルド成果物を保てるようにする狙い。

【放置したときの影響】

ビルド自体は通常通り成功するため（あくまで警告なので）緊急性は低いが、放置し続けるとビルドログに`NU1510`警告が溜まっていき、本当に注意すべき他の警告が埋もれやすくなる。また、次項（22番、`PrunePackageReference`によるプライベート化）とセットで理解すると、将来的にはこうした不要な参照が自動的に`.nuspec`から除外される流れになっていくため、早めに整理しておくと見通しが良くなる。

【プロジェクトでの調べ方】

- 各csprojの`PackageReference`を確認したところ、`fo-dicom`・`Temporalio`・`Npgsql.EntityFrameworkCore.PostgreSQL`・`HotChocolate.AspNetCore`など、いずれも.NET標準ライブラリには含まれない、外部の実際に必要なパッケージのみが参照されている。`System.Text.Json`や`System.Memory`のような「.NET本体に取り込まれ済みの汎用パッケージ」を明示的に参照している箇所は見当たらなかった。
- 実際に警告が出るかどうかを確認するには、`dotnet build DicomTool.slnx`（または`dotnet restore`）を実行し、`NU1510`という文字列がログに出力されるかを確認するのが確実。

  ```bash
  dotnet build DicomTool.slnx | grep NU1510
  ```

- 現時点でのソースコードの静的な確認からは、該当しそうな参照は見当たらない。

【改修方法】

もし`NU1510`警告が出た場合は、該当する`PackageReference`をcsprojから削除する。マルチターゲットプロジェクトで、古いフレームワーク向けにのみそのパッケージが必要な場合は、`Condition`で対象フレームワークを絞り込む。

```xml
<PackageReference Include="System.Text.Json" Version="8.0.5"
                   Condition="!$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net8.0'))" />
```

【参考記事】

- （特になし）

### ランタイム アセットのない NuGet パッケージは、deps.jsonに含まれません
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/deps-json-trimmed-packages

【前提知識】

- **`deps.json`とは**
  `dotnet publish`や`dotnet build`の出力に生成される、`アプリ名.deps.json`というファイル。「このアプリが実行時に必要とする依存アセンブリ・パッケージの一覧」がツリー状に記録されており、.NETランタイムはこれを見て必要なDLLを読み込む。
- **ランタイムアセット(runtime assets)とは**
  実行時に実際に読み込まれるDLL等の成果物のこと。NuGetパッケージの中には、DLLを含まず、ビルド時の解析専用（アナライザー）や、単に他のパッケージをまとめるためのメタパッケージのように「実行時には何も提供しない」種類のものもある。
- **セキュリティスキャナーとは**
  `deps.json`のような依存関係リストを読み取り、既知の脆弱性がある古いバージョンのライブラリが使われていないかを機械的にチェックするツール。

【説明】

以前は、参照しているすべてのNuGetパッケージやプロジェクトが、実際にランタイムアセットを提供していようがいまいが、無条件に`deps.json`のライブラリエントリとして書き出されていた。

.NET 10からは、「ランタイムアセットを提供しない」かつ「そのエントリを消しても他のライブラリへの依存関係パスが壊れない」場合に限り、そのパッケージが`deps.json`から除外されるようになった。

理由は、実際には使われていないライブラリのエントリが`deps.json`に残っていると、依存関係の正確性が下がり、それを解析するセキュリティスキャナー等が誤検知（実際は使っていないのに脆弱性ありと誤判定する等）を起こしやすくなるため。

【放置したときの影響】

多くの場合、実行時の挙動に影響はない（そもそも使われていなかった情報が消えるだけ）。ただし、以下のようなケースで問題になりうる。

- リフレクションなどを使って、`deps.json`の内容を独自に解析し「このパッケージが参照されているかどうか」を判定するような自作ツールがある場合、期待したエントリが消えていて誤判定する可能性がある。
- プラグイン機構等で、`AssemblyDependencyResolver`のような仕組みを使い`deps.json`の情報をもとに動的にアセンブリを解決している場合、稀にエッジケースで影響が出ることがある。

【プロジェクトでの調べ方】

- dicom-tool-3で`deps.json`を直接読み込んで独自解析するようなコード（`AssemblyDependencyResolver`やリフレクションでの依存解決）がないか検索したが、該当なし。
- `DicomTool.Worker`や`DicomTool.DicomScp`のような動的プラグイン読み込みの仕組みも見当たらない（すべて通常のNuGet参照によるコンパイル時静的解決）。
- 結論：**現状は無関係**。実際の挙動変化を目視で確認したい場合は、`dotnet publish`後に生成される`*.deps.json`を開き、アナライザー専用パッケージ（例：`Microsoft.EntityFrameworkCore.Design`のように`PrivateAssets=all`かつビルド時専用のパッケージ）のエントリが減っているかを見ればよい。

【改修方法】

対応不要。もし従来通りすべてのライブラリを`deps.json`に含めたい場合は、以下を設定する。

```xml
<PropertyGroup>
  <TrimDepsJsonLibrariesWithoutAssets>false</TrimDepsJsonLibrariesWithoutAssets>
</PropertyGroup>
```

【参考記事】

- （特になし）

### バージョンのない PackageReference でエラーが発生する
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/nu1015-packagereference-version

【前提知識】

- **`Version`属性の省略とは**
  `<PackageReference Include="Some.Package" />`のように、`Version`属性を書かずにパッケージ参照だけ書くこと。通常は誤って書き忘れたケースがほとんどだが、「[中央パッケージ管理(Central Package Management、CPM)](https://learn.microsoft.com/ja-jp/nuget/consume-packages/central-package-management)」という仕組みを使っている場合は、バージョンを個々のcsprojではなく`Directory.Packages.props`という1つのファイルにまとめて指定するため、意図的に各csproj側では`Version`を省略する、という正当な使い方もある。
- **NU1604・NU1015とは**
  どちらもNuGetのエラー/警告番号。それぞれ「パッケージのバージョン下限がない」ことを知らせるメッセージだが、番号によって重大度（警告かエラーか）やメッセージ内容が異なる。

【説明】

以前は、`Version`が指定されていない`PackageReference`があると、NuGetは「プロジェクトの依存関係に下限がない」という趣旨の`NU1604`**警告**を出しつつも、復元処理自体は（そのパッケージの最小バージョンを使って）続行していた。この挙動は分かりにくく、「下限がない」というメッセージだけでは何をすればいいか初心者には伝わりにくかった。

.NET 10からは、バージョンが指定されていないパッケージ参照に対して`NU1015`**エラー**が発生するようになった（中央パッケージ管理を使っている場合は、そもそもバージョンをcsproj側に書く必要がないため、この変更による影響はない）。

【放置したときの影響】

もし現状のcsprojにバージョン省略の`PackageReference`があり、かつ中央パッケージ管理（`Directory.Packages.props`）を導入していない場合、**.NET 10へ更新した瞬間に`dotnet restore`・`dotnet build`が失敗するようになる**。これまで警告で済んでいたものが、いきなりビルド不能になるため影響度は高め。

【プロジェクトでの調べ方】

- 全csprojの`PackageReference`を確認したところ、`HotChocolate.AspNetCore`・`Npgsql.EntityFrameworkCore.PostgreSQL`・`Temporalio`・`fo-dicom`・`xunit`など、**すべての`PackageReference`に`Version`属性が明示的に指定されている**ことを確認した（バージョン省略の箇所はゼロ件）。
- `Directory.Packages.props`（中央パッケージ管理用ファイル）自体もリポジトリ内には存在しない。つまり、dicom-tool-3では中央パッケージ管理を使わず、各csprojで個別にバージョンを指定する従来型の運用をしている。
- 結論：**現状のすべてのプロジェクトでバージョン指定漏れはなく、この変更によるビルド失敗リスクはない**。

【改修方法】

現状は対応不要。ただし今後新しいパッケージを追加する際に、`Version`属性を書き忘れないよう注意する。書き忘れると次のようなエラーになる。

```diff
- <PackageReference Include="Some.Package" />
+ <PackageReference Include="Some.Package" Version="1.2.3" />
```

【参考記事】

- （特になし）

### PrunePackageReference が直接排除可能な参照を民営化する
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/prune-packagereference-privateassets

【前提知識】

- **`PrunePackageReference`機能とは**
  項目19で説明した「プルーニング（不要参照の除外）」を有効にするMSBuildプロパティ。有効にすると、ターゲットフレームワークが標準提供している機能と重複するパッケージ参照を自動的に無視できる。
- **`.nuspec`ファイルとは**
  `dotnet pack`でNuGetパッケージを作るときに生成される、「このパッケージが依存する他のパッケージは何か」をターゲットフレームワークごとに記述したメタデータファイル。パッケージを配布したときに、利用者側のNuGetが読む「このパッケージを使うには、追加でこれらの依存パッケージも要りますよ」という設計図にあたる。
- **`PrivateAssets`・`IncludeAssets`属性とは**
  `PackageReference`に付けられるオプションで、依存関係の伝播や成果物への含め方を制御する。`PrivateAssets=all`は「このパッケージへの依存を、自分をさらに参照する側には伝播させない（＝自分のパッケージのみが使い、下流には見せない）」、`IncludeAssets=none`は「参照はするが、実際のアセット（DLL等）は取り込まない」という指定。
- **「民営化(privatize)」という訳語について**
  この項目タイトルの「民営化」はMicrosoft Learnの機械翻訳による直訳的な表現で、原義は「（依存関係リストへの伝播を止めて）非公開扱いにする」という意味。「プライベート化」と読み替えるとわかりやすい。

【説明】

以前は、プルーニングが有効になっていても、ターゲットフレームワークが提供する機能と重複するパッケージが、生成される`.nuspec`の依存関係リストにはそのまま載ってしまっていた（ビルド成果物には影響しなくても、パッケージのメタデータ上は「このパッケージにも依存している」と表示され続けていた）。

.NET 10からは、こうした「プラットフォームによって直接排除可能な」`PackageReference`が自動的に`PrivateAssets=all`かつ`IncludeAssets=none`としてマークされ、`.nuspec`の依存関係リストからも除外されるようになった。つまり、実際に配布されるパッケージのメタデータが、そのターゲットフレームワークで本当に必要な依存関係だけを正確に反映するようになった。

【放置したときの影響】

**このプロジェクトのように、自分たちのライブラリをNuGetパッケージとして`dotnet pack`で配布する運用をしていない場合、実質的な影響はない**（`.nuspec`はパッケージ配布時にのみ意味を持つファイルであるため）。もし将来`DicomTool.Shared`等をNuGetパッケージとして切り出して配布する場合には、生成される`.nuspec`の依存関係リストがより正確になる、というポジティブな変化として現れる。

【プロジェクトでの調べ方】

- 各csprojの`IsPackable`設定を確認したところ、明示的に`false`にしているのは`DicomTool.Api.Tests`のみで、他のプロジェクトはデフォルト値のままだが、実際に`dotnet pack`でNuGetパッケージとして配布する運用（NuGetサーバーへのpush等）はドキュメント上見当たらない。全プロジェクトはASP.NET Core Webアプリ・Workerサービス・WinFormsアプリとして`dotnet run`/`dotnet publish`で直接実行・デプロイする構成であり、ライブラリとしてNuGet配布する対象は存在しない。
- 結論：**現状は無関係**。

【改修方法】

対応不要。将来自作ライブラリをパッケージ化して配布する場合、`dotnet pack`後に生成される`.nuspec`ファイルの依存関係を確認し、意図した依存関係だけが載っているかを確認するとよい。

【参考記事】

- （特になし）

### HTTP 警告が `dotnet package list` および `dotnet package search` でエラーとして表示
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/http-warnings-to-errors

【前提知識】

- **NuGetパッケージソースとは**
  NuGetパッケージを取得してくる場所（サーバー）。`nuget.org`のような公開フィードのほか、社内向けのプライベートフィードを`nuget.config`に登録して使うこともある。
- **HTTPとHTTPSの違い**
  HTTPSは通信内容が暗号化される安全な通信、HTTPは暗号化されない安全でない通信。パッケージソースがHTTPの場合、通信経路上で内容の盗聴・改ざんのリスクがある。
- **`allowInsecureConnections`とは**
  `nuget.config`内でパッケージソースごとに設定できる属性で、「このソースはHTTPでもよい」と明示的に許可するためのもの。

【説明】

以前は、`dotnet package list`や`dotnet package search`などのコマンドで、暗号化されていないHTTPのパッケージソースを使うと「非HTTPSアクセスは将来のバージョンで削除されます」という趣旨の警告が出ていたが、操作自体はそのまま続行できていた。

.NET 10 Preview 4以降では、この警告が既定でエラーとして扱われるようになり、`allowInsecureConnections="true"`を`nuget.config`に明示的に設定しない限り、HTTPソースへのアクセスがブロックされる。

理由は、セキュリティで保護されていないHTTP通信を既定でブロックし、意図せず脆弱な通信経路を使い続けてしまうことを防ぐため。

【放置したときの影響】

このリポジトリのように、公開されている`nuget.org`（常にHTTPS）のみをパッケージソースとして使っている場合は無関係。社内独自の古いプライベートNuGetサーバーをHTTPのまま運用している場合にのみ、`dotnet package list`/`dotnet package search`がエラーで失敗するようになる。

【プロジェクトでの調べ方】

- `nuget.config`ファイルがリポジトリ内に存在するか確認したところ、見つからなかった（`find . -iname "nuget.config"`が空）。`nuget.config`がない場合、NuGetは既定のグローバル設定（通常は`nuget.org`のHTTPSフィードのみ）を使う。
- 各csprojの`PackageReference`で参照しているパッケージ（`fo-dicom`、`Temporalio`、`HotChocolate.AspNetCore`等）はいずれも公開のnuget.orgから取得可能な一般的なパッケージであり、独自の社内フィードを使っている形跡はない。
- 結論：**現状は無関係**。

【改修方法】

対応不要。将来社内向けの独自NuGetフィードをHTTPで運用する必要が出た場合は、HTTPSへの移行を検討するか、どうしてもHTTPが必要な場合のみ以下を`nuget.config`に追加する。

```xml
<packageSources>
  <add key="internal-feed" value="http://internal-nuget/index.json" allowInsecureConnections="true" />
</packageSources>
```

【参考記事】

- （特になし）

### NUGET_ENABLE_ENHANCED_HTTP_RETRY環境変数が削除されました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/nuget-enhanced-http-retry-removed

【前提知識】

- **指数バックオフ(exponential backoff)による再試行とは**
  通信が失敗したときに、すぐ・一定間隔で再試行するのではなく、「1秒後→2秒後→4秒後→8秒後…」のように待ち時間を毎回倍々に増やしながら再試行する方式。サーバーが一時的に高負荷になっている場合に、一斉に再試行が集中してさらに負荷が増す事態を避けやすい。
- **固定遅延による再試行とは**
  指数バックオフとは対照的に、常に一定の待ち時間（例：200ミリ秒）で再試行する、より単純な方式。

【説明】

.NET SDK 6.0.300から、NuGetのHTTP通信が失敗した際の再試行方式は「指数バックオフ」が既定になっていたが、それ以前の「固定200ミリ秒遅延」方式に戻したい場合のために、`NUGET_ENABLE_ENHANCED_HTTP_RETRY`環境変数を`false`に設定するという抜け道が用意されていた。

.NET 10からは、この環境変数自体が完全に無効化され、常に指数バックオフでの再試行が使われるようになった。約4年間指数バックオフが既定動作として運用されており、問題を示すフィードバックがなかったため、この抜け道オプションが撤去された。

【放置したときの影響】

この環境変数を意図的に設定して古い再試行方式に戻していた場合を除き、通常は影響がない。dicom-tool-3のような一般的な開発環境では、この環境変数を設定する理由が特にない。

【プロジェクトでの調べ方】

- リポジトリ内および開発環境設定ファイル（`.env`相当のもの、`appsettings.json`等）に`NUGET_ENABLE_ENHANCED_HTTP_RETRY`という文字列がないか検索したが該当なし。
- 結論：**現状は無関係**。

【改修方法】

対応不要。

【参考記事】

- （特になし）

### NuGet 監査ソースでは、既定で安全でない HTTP が許可されなくなりました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/nuget-audit-source-http-disallowed

【前提知識】

- **監査ソース(`auditSource`)とは**
  項目12で説明したNuGetAudit（脆弱性監査）機能が、脆弱性情報を取得しにいく先のサーバー。`nuget.config`の`<auditSources>`セクションで指定する。既定では`nuget.org`の脆弱性データベースが使われる。
- **NU1302とは**
  安全でないHTTPの監査ソースが使われた際に発生するNuGetのエラー番号。

【説明】

.NET 10.0.400 SDK以降、`auditSource`にHTTP（暗号化なし）のURLが設定されていて、`allowInsecureConnections="true"`が明示されていない場合、`NuGetAudit`（項目12参照）の処理自体が`NU1302`エラーで失敗するようになった。以前はHTTPの監査ソースでも警告なしにそのまま処理が続行していた。

理由は、通常のパッケージソースが既にHTTPSを既定で要求している（項目23参照）ことと足並みを揃え、脆弱性情報を取得する監査ソースについても同じセキュリティ基準を適用するため。

【放置したときの影響】

独自の`auditSource`をHTTPで`nuget.config`に設定していない限り無関係。既定の監査ソース（`nuget.org`、常にHTTPS）を使っている場合は影響しない。

【プロジェクトでの調べ方】

- `nuget.config`自体がリポジトリ内に存在しないため（項目23参照）、独自の`auditSource`設定はそもそも存在しない。既定のnuget.org（HTTPS）の脆弱性データベースが使われる。
- 結論：**現状は無関係**。

【改修方法】

対応不要。将来独自の監査ソースを設定する場合はHTTPSのURLを使う。

```xml
<auditSources>
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</auditSources>
```

【参考記事】

- （特になし）

### NuGet は、無効なパッケージ ID のエラーをログに記録します
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/nuget-packageid-validation

【前提知識】

- **パッケージIDとは**
  NuGetパッケージを一意に識別する名前（例：`Newtonsoft.Json`、`fo-dicom`）。NuGetにはパッケージID自体に使ってよい文字種・形式のルールがある。
- **NuGetがパッケージIDからURLを組み立てる仕組みとは**
  `dotnet restore`等でNuGetパッケージを取得する際、内部的には「パッケージID＋バージョン」からNuGetサーバー上のダウンロードURLを機械的に組み立てて通信している。パッケージIDに想定外の記号や文字が混ざっていると、意図しないURLが組み立てられてしまうリスクがある。

【説明】

以前は、NuGetがパッケージIDからURLを組み立てる際、パッケージIDの形式（文字種・長さ等）を検証していなかった。そのため、不正な形式・想定外のパッケージIDが渡されても、検証エラーになることなくそのままURL構築処理へ進んでしまっていた。

.NET 10からは、NuGetリソースを通じてURLを構築する際にパッケージIDが検証されるようになり、NuGetの期待するフォーマットに準拠していないパッケージIDが渡された場合は例外がスローされ、URLの構築自体が行われなくなった。

理由はセキュリティ強化。不正な形式の入力（安全でない、あるいは意図しない入力）がそのまま処理されるリスクを減らすため。

【放置したときの影響】

通常のパッケージ名（英数字・ドット・ハイフン等の一般的な文字のみで構成されるパッケージID）を使っている限り、まず影響はない。何らかの理由でパッケージIDを動的に生成・組み立てるツールを自作しており、そのIDに想定外の文字が混入するようなケースでのみ例外が発生する可能性がある。

【プロジェクトでの調べ方】

- 各csprojの`PackageReference`で使われているパッケージID（`HotChocolate.AspNetCore`、`Microsoft.AspNetCore.Authentication.JwtBearer`、`Npgsql.EntityFrameworkCore.PostgreSql`、`fo-dicom`、`Temporalio`、`Swashbuckle.AspNetCore`、`xunit`、`NSubstitute`など）はいずれも一般的な英数字・ドット・ハイフンのみで構成された正規のパッケージ名であり、不正な形式のものは一つもない。
- パッケージIDを動的に組み立てるようなカスタムスクリプト・ツールもリポジトリ内には存在しない。
- 結論：**現状は無関係**。

【改修方法】

対応不要。万一この検証によって正当なはずのパッケージ取得がブロックされる不具合に遭遇した場合の一時的な回避策として、環境変数`NUGET_DISABLE_PACKAGEID_VALIDATION`を`true`に設定して検証を無効化できる（あくまで一時的な回避策として使うこと）。

【参考記事】

- （特になし）

### `ToolCommandName` ツール以外のパッケージに対して設定されていない
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/sdk/10.0/toolcommandname-not-set

【前提知識】

- **`ToolCommandName`とは**
  `PackAsTool=true`の.NETツールプロジェクトにおいて、「実際にターミナルから打ち込むコマンド名」を指定するMSBuildプロパティ（例：`dotnet-ef`というツールなら、ユーザーは`dotnet ef`と打つ）。
- **`PackAsTool`とは**
  項目3でも登場した、そのプロジェクトを.NETツールとしてパッケージ化するかどうかを指定するプロパティ。

【説明】

以前は、プロジェクトが.NETツールかどうか（`PackAsTool`の値）に関わらず、ビルドやパック操作の過程で`ToolCommandName`プロパティが常に自動的に何らかの値を持って設定されていた。ツールでない通常のプロジェクトにとってはそもそも意味のない値であり、混乱の元だった。

.NET 10からは、`ToolCommandName`は`PackAsTool=true`（つまり実際に.NETツールであるプロジェクト）の場合にのみ設定されるようになった。

【放置したときの影響】

**通常のASP.NET CoreアプリやWinFormsアプリのような、.NETツールではない一般的なプロジェクトにとっては影響がない**。唯一影響がありうるのは、MSBuildのカスタムターゲットや自作スクリプトの中で「`ToolCommandName`プロパティの値が常に何か設定されていること」を前提にしたロジックを書いていた場合で、その場合は空（未設定）を想定していない処理があるとエラーになりうる。

【プロジェクトでの調べ方】

- 全csproj・props・targetsファイルに対して`ToolCommandName`をgrepしたが該当なし。
- 項目3で確認した通り、`PackAsTool`を使っている.NETツールプロジェクトもdicom-tool-3内には存在しない。
- 結論：**完全に無関係**。

【改修方法】

対応不要。将来もし`ToolCommandName`に依存するカスタムMSBuildロジックが必要になった場合は、明示的にプロパティを設定するか、そのプロジェクトを`PackAsTool=true`の.NETツールにする。

```xml
<PropertyGroup>
  <ToolCommandName>your-command-name</ToolCommandName>
</PropertyGroup>
```

【参考記事】

- （特になし）
