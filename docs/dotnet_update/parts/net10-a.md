# .NET 10 での破壊的変更（担当分 16項目）

## Containers

### 既定の .NET イメージで Ubuntu を使用する
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/containers/10.0/default-images-use-ubuntu

【前提知識】

- **コンテナー（Dockerコンテナー）とは**
  アプリと、それを動かすのに必要な最小限のOS・ライブラリ一式を1つの「箱（イメージ）」にまとめる技術。`docker pull mcr.microsoft.com/dotnet/sdk:10.0`のように、Microsoftが公式に配布している.NET入りのイメージを取得して使うのが一般的。
- **Linuxディストリビューションとは**
  一口に「Linux」と言っても、Ubuntu・Debian・Alpineなど中身が微妙に違う複数の種類（ディストリビューション）がある。コンテナーの土台としてどれを選ぶかによって、使えるパッケージ管理コマンドや、セキュリティパッチが提供される期間（サポート期間）が変わってくる。
- **イメージの「タグ」とは**
  `mcr.microsoft.com/dotnet/sdk:10.0`の`10.0`の部分を「タグ」と呼ぶ。バージョン番号だけを指定した場合（OS名を指定しない場合）、Microsoftが「これが今の既定です」と決めたディストリビューションが使われる。`10.0-noble`のようにOSのコードネーム（Ubuntu 24.04のコードネームが"noble"）を明示的に指定することもできる。

【説明】

.NET 9以前は、OS名を指定しない既定のコンテナータグ（`10.0`など）はDebianベースのイメージを指していた。.NET 10からは、この既定がUbuntu（24.04 "Noble Numbat"）に変更された。さらに.NET 10では、Debianベースのイメージ自体が配布されなくなった（Debian版を使いたい場合は自分でカスタムイメージを作る必要がある）。

変更理由は「サポート期間の違い」。DebianとNET本体のメインラインサポート期間はだいたい同じ長さだが、Debianの方がリリースのタイミングの都合で先にサポート切れになりやすい。一方Ubuntuのサポート期間ははるかに長く、「.NETのあるバージョンのサポートが切れる前にOS側のサポートが切れてしまう」という事故が起きにくい。つまり、コンテナーを長く安全に運用しやすくするための変更。

【放置したときの影響】

公式ドキュメントでも「ほとんどのシナリオでアクションは不要」と明記されている。挙動としては、既定タグでイメージをビルドし直すと中身のOSがDebianからUbuntuに変わるだけで、通常は気づかないことが多い。ただし、以下のようなケースでは影響が出うる。

- Dockerfile内で`apt-get`を使い、Debian固有のパッケージ名やAPTリポジトリ設定を直接書いている場合（Ubuntuでもaptコマンド自体は使えるが、パッケージの有無やバージョンが微妙に異なることがある）。
- ベースイメージのOSが変わったことを前提に、CI/CDのセキュリティスキャンツールなどがDebian用の脆弱性データベースを参照している場合。

【プロジェクトでの調べ方】

まず「.NETの公式コンテナーイメージを使ってDockerで動かしているサービスがあるか」を確認する。

```
Glob: **/Dockerfile*
```
で検索したところ、dicom-tool-3リポジトリ内には`Dockerfile`という名前のファイルは1つも見つからなかった。リポジトリ直下の`docker-compose.yml`はPostgreSQLとTemporalサーバーを起動するためのもので、これらは.NETとは無関係の既製イメージ（`postgres:...`や`temporalio/...`など）を使っている。C#の各サービス（`backend/DicomTool.Api`、`services/DicomTool.Worker`、`services/DicomTool.DicomScp`など）はいずれも`dotnet run`でホストPC上に直接起動する運用であり、**この変更は現時点のdicom-tool-3には影響しない**。

将来、これらのサービスをDockerコンテナー化する話が出た場合にだけ関係してくる項目、と覚えておけばよい。

【改修方法】

現状は改修不要。将来Dockerfileを書く際に、既定タグがUbuntuになったことを念頭に置く程度でよい。もしDebianを明示的に使いたい事情が生じた場合は、タグを明示するか自前でベースイメージを作る。

```dockerfile
# 改修前（意識せず書いていた場合。.NET 10からは中身がUbuntuになる）
FROM mcr.microsoft.com/dotnet/aspnet:10.0

# Debianを明示したい場合（.NET 10ではDebian版イメージ自体が提供されないため、
# 実際には別途カスタムイメージを自作する必要がある。Ubuntuを許容するのが基本の対応）
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

## Globalization

### 環境変数の名前が DOTNET_ICU_VERSION_OVERRIDE に変更
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/globalization/10.0/version-override

【前提知識】

- **ICU（International Components for Unicode）とは**
  文字の大文字/小文字変換、文字列の並び替え（照合順序）、日付や数値の書式など、「言語や国ごとに違うルール」（グローバリゼーション、通称"globalization"）を処理するための業界標準ライブラリ。.NETは文字列比較やカルチャ（`ja-JP`など）を扱う処理の多くを、内部的にこのICUに委譲している。Linux上で動く.NETアプリの多くは、OSにインストールされたICUライブラリを利用する。
- **環境変数とは**
  OSやアプリの起動時に、外部から動作を切り替えるための「設定値」。C#のコードを書き換えなくても、OS側で`SET 変数名=値`（Windows）や`export 変数名=値`（Linux/Mac）としておくだけで、アプリの挙動を変えられる。.NETランタイムは`DOTNET_`から始まる名前の環境変数を多数サポートしており（例：`DOTNET_ENVIRONMENT`）、これらは「.NET構成スイッチ」と呼ばれる。
- **バージョンオーバーライドとは**
  通常は.NETやOSが自動的に「使うICUのバージョン」を選ぶが、動作検証やトラブルシュートのために「特定バージョンのICUを強制的に使わせたい」場面がある。そのための仕組みがこの環境変数。

【説明】

以前は`CLR_ICU_VERSION_OVERRIDE`という環境変数名で、Linux上で読み込むICUライブラリの優先バージョンを指定できた。.NET 10からはこの環境変数名が`DOTNET_ICU_VERSION_OVERRIDE`に変更された。

変更理由は単純で、.NETの他の構成スイッチ環境変数の命名規則（`DOTNET_`プレフィックス）に合わせて一貫性を持たせるため。`CLR_`という接頭辞は.NETの内部コード名である"CLR"（Common Language Runtime）に由来する古い名残であり、他の設定項目の多くがすでに`DOTNET_`に統一されているのにこの項目だけ取り残されていた、という位置づけの変更。

【放置したときの影響】

このような環境変数を意図的に設定して運用しているケースはかなりレアだが、放置した場合の影響は「サイレントに無視される」という地味に厄介なもの。

```bash
# .NET 9以前は効いていたが、.NET 10ではこの変数名はもう認識されない。
# エラーにもならず、単に「指定していないのと同じ」扱いになってしまう。
export CLR_ICU_VERSION_OVERRIDE=72.1
```

たとえば「特定のICUバージョンの不具合を回避するために、一時的に古いバージョンへ固定していた」といった運用をしていた場合、.NET 10に上げた瞬間にこの固定が効かなくなり、意図しないバージョンのICUが使われて文字列比較の結果が微妙に変わる…といった、気づきにくい不具合につながる可能性がある。

【プロジェクトでの調べ方】

```
Grep: "ICU_VERSION_OVERRIDE" (プロジェクト全体)
```
で検索したが、dicom-tool-3のリポジトリ内でこの環境変数（新旧いずれの名前も）を参照・設定している箇所は見つからなかった。`appsettings.json`や起動スクリプト、Dockerfile等でも同様。**この変更は現時点のdicom-tool-3には影響しない。**

念のため、Windows上で開発運用している限りはそもそもこの環境変数はLinux向けの仕組みなので影響しにくい点も補足しておく（本プロジェクトは開発機がWindows、VM側がLinuxである点は他の項目同様に注意）。

【改修方法】

現状は改修不要。もし将来、Linux環境（`dicom-pacs-vm`など）でICUバージョンを固定する必要が出てきた場合は、新しい名前で設定する。

```bash
# 改修前（.NET 10では効かない）
export CLR_ICU_VERSION_OVERRIDE=72.1

# 改修後
export DOTNET_ICU_VERSION_OVERRIDE=72.1
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

## ツールをインストールする

### dotnet.acquire API for VS Code が常に最新のダウンロードを行う必要がなくなりました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/install-tool/3.0.0/vscode-dotnet-acquire-no-latest

【前提知識】

- **.NET Install Tool for VS Codeとは**
  VS Codeの拡張機能（C# Dev Kitなど）が、内部的に自分専用の.NETランタイムを必要とするときに使う「配管役」の拡張機能。ユーザーのプロジェクトを実行するための.NETとは別に、「言語サーバー」などVS Code拡張機能自身の裏方処理を動かすための.NETランタイムを、ユーザーの目に触れないところでダウンロード・管理している。
- **`dotnet.acquire`コマンドとは**
  他のVS Code拡張機能（C# Dev Kitなど）が「私はこのバージョンの.NETランタイムが欲しいです」とこのInstall Toolに依頼するためのAPI（VS Codeのコマンド呼び出し）。裏側でランタイムのダウンロード・インストールを行い、そのランタイムの実行ファイルへのパスを返す。
- **これはあくまでVS Code拡張機能の裏方インフラの話**であり、`dotnet build`や`dotnet run`でアプリを動かすときに使われるSDK/ランタイムとは別物である点に注意。

【説明】

以前の`dotnet.acquire`は、呼び出されるたびに「今使えるランタイムの中で本当に最新のものは何か」をネットワーク越しにチェックし、もしより新しいバージョンがあればダウンロード・インストールしてからパスを返していた。この処理は、VS Codeの拡張機能が起動するたび（実質的にVS Codeの起動のたび）に走っていた。

.NET Install Tool バージョン3.0.0からは、この「毎回最新版をチェックする」動作をやめ、既にインストール済みのランタイムをそのまま使い回すようになった。新しいバージョンのチェックは、設定可能な遅延時間（既定5分）が経過したあとに1日1回だけバックグラウンドで行われる。

変更理由は起動速度の改善。ネットワーク環境が悪いユーザー（下位3%）では、この最新版チェックとダウンロードだけで起動時に9～36秒もの遅延が発生していた。中央値のユーザーでも287ミリ秒の遅延があった。ユーザープロジェクトの実行に直接使われるランタイムではないのに、拡張機能の起動のたびにこの待ち時間が発生するのは無駄が大きい、という判断。

【放置したときの影響】

これは「dicom-tool-3のC#コードそのもの」に影響する変更ではなく、「VS Code拡張機能を自作している開発者」向けの変更である。dicom-tool-3のようなアプリケーション開発者が影響を受けるのは、自分たちがVS Code拡張機能を開発・配布していて、その中で`dotnet.acquire` APIを呼んでいる場合に限られる。

放置した場合の実害は「意図せず古いランタイムを使い続けてしまう」こと。たとえば拡張機能側が特定のバグ修正やセキュリティパッチが入った最新ランタイムを前提にしていた場合、ユーザー環境では1日1回のチェックまで更新が遅延するため、ごく短期間、古いランタイムのまま動いてしまう可能性がある。

【プロジェクトでの調べ方】

dicom-tool-3自体がVS Code拡張機能を開発しているかどうかを確認した。

```
Glob: **/*.vsix, .vscode/**
```
などで確認したところ、リポジトリ内に`.vscode`フォルダーそのものが存在せず、VS Code拡張機能のプロジェクト（`package.json`に`"engines": {"vscode": ...}`があるようなもの）も見当たらなかった。**この変更はdicom-tool-3には全く関係しない。** 開発者がVS Codeエディタ自体を使ってこのリポジトリを開発する分には、単に「拡張機能の起動が速くなった」という体感の変化があるだけで、コード側の対応は不要。

【改修方法】

改修不要（本プロジェクトはVS Code拡張機能を開発していないため）。

もし将来、社内ツールとしてVS Code拡張機能を作り、その中で常に最新のランタイムを強制したい場合だけ、以下のように`forceUpdate: true`を指定する。

```javascript
// 改修前（.NET Install Tool 3.0.0未満の挙動を期待していたコード）
const dotnetRuntimePath = (await vscode.commands.executeCommand(
    'dotnet.acquire',
    { version: '10.0', requestingExtensionId }
)).dotnetPath;

// 改修後（毎回最新版を強制したい場合のみ明示的に指定する）
const dotnetRuntimePath = (await vscode.commands.executeCommand(
    'dotnet.acquire',
    { version: '10.0', requestingExtensionId, forceUpdate: true }
)).dotnetPath;
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

## Interop

### IDispatchEx COM オブジェクトを IReflect にキャストできない
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/interop/10.0/idispatchex-ireflect-cast

【前提知識】

- **COM（Component Object Model）とは**
  Windowsに古くから存在する「言語に依存しないコンポーネント連携の仕組み」。Excel、Internet Explorer由来のHTMLエンジンなど、多くのWindows標準機能やレガシーアプリがCOMオブジェクトとして操作可能になっている。.NETからは`System.Runtime.InteropServices`名前空間の仕組みを使ってCOMオブジェクトを呼び出せる（相互運用＝Interop）。
- **`IDispatchEx`とは**
  COMの中でも、「実行時にメンバー（メソッドやプロパティ）を動的に呼び出す」ための拡張インターフェイス。VBScriptやJScriptのような動的言語からCOMオブジェクトを操作するために使われてきた仕組みで、`htmlfile`（Internet Explorerが提供するHTML文書オブジェクト）などがこれを実装している。
- **`IReflect`とは**
  .NET側にもともとある、リフレクション（実行時に型やメンバー情報を調べたり呼び出したりする仕組み）のためのインターフェイス。「動的にメンバーを呼び出せるオブジェクト」を表現する手段として、.NETは内部的に`IDispatchEx`を実装するCOMオブジェクトを`IReflect`として扱えるようにしていた。
- **キャストとTypeLoadExceptionとは**
  C#で`obj is IReflect`のように「あるオブジェクトが特定のインターフェイスを実装しているかどうか」を調べたり、`(IReflect)obj`のように型変換したりすることを「キャスト」と呼ぶ。`TypeLoadException`は「読み込もうとした型が見つからない・壊れている」際にランタイムが投げる例外。

【説明】

.NET 5以降、`IDispatchEx`を実装するCOMオブジェクト（`htmlfile`など）を`IReflect`型にキャストすること自体はコンパイルも実行時のキャストも成功していた。しかし実際に得られた`IReflect`インスタンスの、どのメンバー（`InvokeMember`など）を呼び出しても、必ず`TypeLoadException`が発生していた。つまり「キャストだけは成功するが、その後は何をやっても例外で失敗する」という、実質的に使い物にならない機能だった。

.NET 10では、この「キャストは成功するが使えない」という中途半端な挙動そのものをやめ、キャストの時点で失敗するように変更された。

変更理由は、機能として実質使えなかった上に、投げられていた`TypeLoadException`が「.NETに実際には存在したことのない型」に言及するという、原因調査する開発者を余計に混乱させる内容だったため。「動くふりをして実際は使えない」より「最初から使えないと分かる」方が親切、という判断。

【放置したときの影響】

dicom-tool-3のようなDICOM通信・医療系Webアプリでは、Excel操作やInternet Explorerの旧HTMLエンジンをCOM経由で使うようなコードを書く機会はまず無いため、実際に踏む可能性は低い。もし影響がある場合、以下のようなコードが例外の投げどころが変わる。

```csharp
using System.Reflection;

var file = Activator.CreateInstance(Type.GetTypeFromProgID("htmlfile"));

// .NET 9以前: trueになる（が、その後IReflectとして何かしようとすると必ずTypeLoadException）
// .NET 10以降: falseになる
bool supported = file is IReflect;
```

影響は「エラーの発生タイミングが早まる」方向の変化であり、元々使えなかった機能なので実害としては軽微。

【プロジェクトでの調べ方】

```
Grep: "IDispatchEx" / "IReflect" / "GetTypeFromProgID" (*.cs 全体)
```
で検索したが、いずれも0件だった。dicom-tool-3はDICOM通信やASP.NET Core、Temporalワーカー、WinFormsトレイアプリで構成されており、COM相互運用やExcel自動化のようなコードは含まれていない（`tools/excel_hidden_sheet_csv_exporter`というツールがあるが、これはExcelファイルをライブラリ経由で読むものであり、COM経由のオートメーションではない可能性が高いため、念のため下記のフォローアップで確認する価値はある）。**現時点のC#コードにはこの変更の影響はない。**

【改修方法】

改修不要。万が一将来、COM経由でCOMオブジェクトが`IDispatchEx`を実装しているかを判定するコードを書く場合は、以下のように直接`IDispatchEx`をチェックする形に置き換える。

```csharp
// 改修前（.NET 10では例外の意味合いが変わるため非推奨）
var file = Activator.CreateInstance(Type.GetTypeFromProgID("htmlfile"));
bool supported = file is IReflect;

// 改修後（本来問いたかった「IDispatchExを実装しているか」を直接尋ねる）
[System.Runtime.InteropServices.ComImport]
[System.Runtime.InteropServices.Guid("A6EF9860-C720-11D0-9337-00A0C90DCAA9")]
interface IDispatchEx { }

var file = Activator.CreateInstance(Type.GetTypeFromProgID("htmlfile"));
bool supported = file is IDispatchEx;
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### 単一ファイル アプリで実行可能ディレクトリ内のネイティブ ライブラリが検索されなくなりました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/interop/10.0/native-library-search

【前提知識】

- **P/Invoke（プラットフォーム呼び出し）とは**
  C#から、OSが提供するネイティブのDLL（Windowsの`.dll`やLinuxの`.so`）の関数を直接呼び出す仕組み。`[DllImport("kernel32.dll")]`のように書くのが典型例。DICOM関連ツールでも、画像処理ライブラリなどをネイティブDLL経由で呼ぶ場合にこの仕組みが登場しうる。
- **単一ファイルアプリ（Single-file deployment）とは**
  通常、.NETアプリを発行（publish）すると、本体の`.exe`（または`.dll`）に加えて大量の依存DLLがフォルダーにばらまかれる。単一ファイル発行を使うと、それらをすべて1つの実行ファイルにまとめて配布できる。`dotnet publish -p:PublishSingleFile=true`のように指定する。配布や運用が楽になるため、常駐アプリやCLIツールでよく使われる。
- **NativeAOTとは**
  通常の.NETアプリは実行時にJITコンパイル（機械語への変換）を行うが、NativeAOTは事前（発行時）にネイティブの機械語へすべて変換しきってしまう発行方式。起動が速く、.NETランタイム自体を同梱する必要がない代わりに、リフレクションなど一部の機能に制約がある。
- **ネイティブライブラリの「検索パス」とは**
  P/Invokeで`[DllImport("foo")]`と書いたとき、OSは「`foo`という名前のライブラリをどのフォルダーから探すか」というルールに従って探索する。このルールを制御するのが`DllImportSearchPath`という列挙型（フラグ）。

【説明】

以前は、単一ファイルアプリとして発行した実行ファイルは、起動時に「自分自身が置かれているフォルダー（アプリケーションディレクトリ）」を、ネイティブライブラリの検索対象（`NATIVE_DLL_SEARCH_DIRECTORIES`）に自動的に加えていた。そのため、P/Invoke側で明示的に「アセンブリディレクトリを検索してよい」と指定していなくても、事実上常にアプリのフォルダーが検索されていた。Windows以外でNativeAOTを使う場合も同様に、`rpath`（実行ファイルが依存ライブラリを探す際の追加パス）が常にアプリケーションディレクトリに設定されていた。

.NET 10からは、この「常に検索する」という自動追加をやめた。単一ファイルアプリ／NativeAOTでも、通常の.NETアプリと同じルールに統一され、`DllImportSearchPath.AssemblyDirectory`（P/Invokeの既定の動作にも含まれる）を明示的に指定するか、フラグを何も指定しない場合にのみ、アプリのフォルダーが検索されるようになった。逆に、他の検索フラグ（`System32`など）だけを指定していた場合は、アプリのフォルダーはもう検索されない。

変更理由は「単一ファイルアプリだけ特別扱いされていて紛らわしかった」ため。通常の.NETアプリでの検索フラグの挙動と一貫性を持たせる目的の変更。

【放置したときの影響】

もしdicom-tool-3の各サービス（たとえば`services/DicomTool.DicomScp`）を将来「単一ファイル発行」で配布するようになり、かつネイティブDLL（画像圧縮ライブラリなど）をP/Invokeで呼んでいる場合、以下のようなコードが影響を受ける可能性がある。

```csharp
// 検索フラグとしてSystem32だけを明示している場合
[DllImport("mylib")]
[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
static extern void SomeNativeFunction();
```

このコードは、単一ファイル発行された場合、.NET 9以前は「System32を探しつつ、実際にはアプリのフォルダーも一緒に探してくれていた（隠れた救済措置）」が、.NET 10からはSystem32だけしか探さなくなる。もしアプリと一緒に配布していたネイティブDLLに依存していた場合、`DllNotFoundException`が発生するようになる。

【プロジェクトでの調べ方】

```
Grep: "DllImportSearchPath" / "[DllImport(" / "NativeLibrary\." (*.cs 全体)
```
で検索したところ、P/Invoke（`DllImport`属性）やネイティブライブラリのロード（`NativeLibrary.Load`）を行っている箇所は1件も見つからなかった。DICOM通信部分は`fo-dicom`ライブラリ（マネージド実装のDICOMライブラリ）を使っており、独自にネイティブDLLをP/Invokeで呼ぶ実装にはなっていない。**現時点のC#コードにはこの変更の影響はない。**

また、`Glob: **/*.csproj`で全プロジェクトの設定を確認した限り、`PublishSingleFile`を有効にしているプロジェクトも現状は存在しない（`services/DicomTool.TrayApp`は将来単一ファイル配布の候補になりうるが、現状は未設定）。両方の条件（単一ファイル発行 かつ P/Invoke使用）が揃わない限りこの変更は関係しない。

【改修方法】

現状は改修不要。将来、単一ファイル発行のアプリでアプリケーションディレクトリ内のネイティブDLLをP/Invokeで読み込む必要が出てきた場合は、フラグを明示的に追加する。

```csharp
// 改修前（.NET 9以前は暗黙にアプリのフォルダーも探してくれていた）
[DllImport("mylib")]
[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
static extern void SomeNativeFunction();

// 改修後（.NET 10でアプリのフォルダーも探させたい場合はAssemblyDirectoryを明示的に足す）
[DllImport("mylib")]
[DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.AssemblyDirectory)]
static extern void SomeNativeFunction();
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### DllImportSearchPath.AssemblyDirectory を指定すると、アセンブリ ディレクトリのみが検索されます
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/interop/10.0/search-assembly-directory

【前提知識】

- 前項目（単一ファイルアプリのネイティブライブラリ検索）と同じ`DllImportSearchPath`・P/Invokeの知識が前提になる。あわせて以下も押さえておく。
- **「アセンブリディレクトリ」とは**
  P/Invokeを呼び出しているC#コードが含まれる`.dll`（アセンブリ）が置かれているフォルダーのこと。通常はビルド成果物のフォルダー（例：`bin/Debug/net10.0/`）を指す。
- **フォールバックとは**
  「第一候補で見つからなかったら、次の候補を試す」という段階的な探索の仕組み。今回の変更は、まさにこの「次の候補を試す」段階が行われなくなる、という話。

【説明】

以前は、P/Invokeの検索フラグとして`DllImportSearchPath.AssemblyDirectory`だけを指定した場合、ランタイムはまずアセンブリディレクトリを探し、そこで見つからなければ、OS標準のライブラリ検索の仕組み（Windowsなら`PATH`環境変数や`System32`など）に自動的にフォールバックしていた。

.NET 10からは、`AssemblyDirectory`だけを唯一の検索フラグとして指定した場合、本当にアセンブリディレクトリの中だけしか探さなくなった。そこで見つからなければ、OSの既定探索にフォールバックせず、直接`DllNotFoundException`がスローされる。

変更理由は、このフォールバック動作が「フラグで明示的に検索範囲を絞ったつもりなのに、実際には他の場所も探されてしまう」という矛盾を生んでいたため。検索フラグの本来の設計（指定した場所だけを探す）と実際の挙動を一致させ、わかりやすさを優先した変更。

【放置したときの影響】

以下のようなコードがある場合、挙動が変わる可能性がある。

```csharp
[DllImport("example.dll", DllImportSearchPath = DllImportSearchPath.AssemblyDirectory)]
public static extern void ExampleMethod();
```

- .NET 9以前：アセンブリディレクトリに`example.dll`が無くても、OSのPATH等から見つかれば動いていた。
- .NET 10以降：アセンブリディレクトリに`example.dll`が無ければ、その時点で`DllNotFoundException`。

つまり「本当はOSのPATHにあるDLLに（意図せず）助けられていた」ようなケースがあると、.NET 10でいきなり動かなくなる可能性がある。

【プロジェクトでの調べ方】

前項目と同じ調査結果を再利用できる。

```
Grep: "DllImportSearchPath" (*.cs 全体)
```
で0件。dicom-tool-3にはP/Invoke自体が存在しないため、**この変更は現時点のdicom-tool-3には一切影響しない。**

【改修方法】

改修不要。将来この属性を使う場面が出てきた場合は、「フォールバックしてほしいのか、アセンブリディレクトリだけに絞りたいのか」を意識して書く。

```csharp
// 改修前の意図が「アセンブリディレクトリを優先しつつOSの標準探索にもフォールバックしたい」だった場合、
// .NET 10ではAssemblyDirectory単体指定だとフォールバックしないので注意
[DllImport("example.dll", DllImportSearchPath = DllImportSearchPath.AssemblyDirectory)]
public static extern void ExampleMethod();

// 改修後（フォールバックが欲しいなら、そもそも検索フラグを指定しない＝既定の挙動に任せる）
[DllImport("example.dll")]
public static extern void ExampleMethod();
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

## リフレクション

### InvokeMember/FindMembers/DeclaredMembers の制限付き注釈
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/reflection/10/ireflect-damt-annotations

【前提知識】

- **リフレクションとは**
  実行時に「この型にはどんなプロパティ・メソッドがあるか」を調べたり、名前を指定して動的にメソッドを呼び出したりする.NETの仕組み（`System.Reflection`名前空間）。JSONシリアライザーやDIコンテナ、ORMなど、多くのライブラリが内部でリフレクションを使っている。
- **トリミング（Trimming）とは**
  アプリ発行時に「実際には使われていないコード」をアセンブリから削除し、配布サイズを小さくする最適化。ただしリフレクションで「実行時に文字列で指定した名前のメンバーを呼び出す」ようなコードは、静的解析だけでは「本当に使われているかどうか」を判断できないため、トリミングによって誤って削除されてしまうリスクがある。
- **`DynamicallyAccessedMembersAttribute`（DAMT）とは**
  「このメソッドの引数として渡された型については、こういう種類のメンバー（例：パブリックメソッドのみ、全メンバーなど）はトリミングで消さないでほしい」とツールに伝えるための注釈（アノテーション）属性。`DynamicallyAccessedMemberTypes.All`のように指定する。値が大きいほど「多くの種類のメンバーを残す」が、その分トリミングの効果（削減できるサイズ）は小さくなる。
- **`IReflect`・`TypeInfo`とは**
  `IReflect`は「動的にメンバーを検索・呼び出せる型」を表すインターフェイス。`TypeInfo`は`System.Type`をリフレクションでさらに詳しく扱うためのクラスで、これらを自作クラスで実装・継承することは、フレームワーク開発者など一部の高度な用途でのみ行われる。

【説明】

`System.Reflection.IReflect.InvokeMember`、`System.Type.FindMembers`、`System.Reflection.TypeInfo.DeclaredMembers`という3つのAPIには、トリミング解析用の`DynamicallyAccessedMembers`注釈が付けられている。以前はこれらすべてに最も広い`DynamicallyAccessedMemberTypes.All`（「あらゆる種類のメンバーを保持してね」という指定）が付いていた。

.NET 10では、この注釈がより実態に即した、狭い範囲のものに変更された。以前の`All`指定は「広すぎて」、たとえばクラスが実装しているインターフェイスのメソッドまで余計にキャプチャしてしまい、意図しない実行時警告や、本来は安全でないはずのリフレクション呼び出しが警告なく通ってしまう、といった問題を引き起こしていた。

この変更が関係してくるのは、フレームワーク開発者などが独自に`IReflect`を実装したクラスを書いたり、`TypeInfo`を継承した独自クラスを書いたりする、かなり特殊なシナリオに限られる。一般的なアプリケーション開発でこれらのAPIを「呼び出す側」として使うだけなら、通常は影響しない。

【放置したときの影響】

dicom-tool-3で`IReflect`を実装したり`TypeInfo`を継承したりするようなコードを書く可能性はほぼない（これはASP.NET Coreや動的言語ランタイムの内部実装など、ごく限られた高度な用途のためのAPI）。仮に該当するコードがあった場合、トリミング発行（`PublishTrimmed=true`）を使っているプロジェクトでのみ、以下のように注釈を見直す必要が出てくる。

```csharp
// 該当するようなコード（フレームワーク開発者向けの特殊な例）
class MyType : IReflect
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] // 以前の広すぎる注釈
    public object InvokeMember(string name, BindingFlags invokeAttr, Binder? binder, object? target,
        object?[]? args, ParameterModifier[]? modifiers, CultureInfo? culture, string[]? namedParameters)
    { ... }
}
```

トリミング発行をしていないプロジェクトでは、そもそも注釈の内容が実行時の挙動に影響しないため、実害はまず発生しない。

【プロジェクトでの調べ方】

```
Grep: "FindMembers" / "DeclaredMembers" / "InvokeMember" / ": TypeInfo" (*.cs 全体)
```
で検索したが、いずれも0件だった。dicom-tool-3のどのプロジェクト（`backend/DicomTool.Api`、`services/DicomTool.Worker`、`services/DicomTool.DicomScp`、`services/DicomTool.TrayApp`など）でも`IReflect`の実装や`TypeInfo`の継承は行われていない。

さらに、`Grep: "PublishTrimmed"`でも全プロジェクトを検索したが該当箇所は0件で、トリミング発行自体をまだ使っていない。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

改修不要。将来、独自にリフレクションベースの動的ディスパッチ機構（プラグイン機構など）を実装する際に`IReflect`や`TypeInfo`を継承する場合にだけ、以下のように注釈を必要最小限に絞り込むことを検討する。

```csharp
// 改修前（過度に広い注釈）
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]

// 改修後（実際に必要なメンバー種別だけに絞る）
[DynamicallyAccessedMembers(
    DynamicallyAccessedMemberTypes.PublicMethods |
    DynamicallyAccessedMemberTypes.NonPublicMethods)]
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### Type.MakeGenericSignatureType 引数の検証
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/reflection/10/makegeneric-signaturetype-validation

【前提知識】

- **ジェネリック型定義とは**
  `List<T>`のように、型パラメーター（`T`）を持ったまま「まだ具体的な型が決まっていない」状態の型のこと。これに対して`List<int>`のように型パラメーターへ実際の型を当てはめたものを「構築されたジェネリック型」と呼ぶ。C#コードでは`typeof(List<>)`のように書くとジェネリック型定義を取得できる。
- **`Type.MakeGenericSignatureType`とは**
  リフレクションでジェネリック型を動的に組み立てるための、かなりニッチなAPI。通常アプリ開発者が直接使うことはまず無く、リフレクションを高度に扱うライブラリ（式木を扱うライブラリなど）の内部実装で使われることがある程度のAPI。

【説明】

以前は`Type.MakeGenericSignatureType(Type genericTypeDefinition, Type[] typeArguments)`の第1引数`genericTypeDefinition`には、本来ジェネリック型定義であるべきところ、実際には非ジェネリック型など「何でも」渡すことができてしまっていた。ジェネリック型定義でない型を渡した場合でも、内部でエラーにはならず、結果として意味の通らない（非センシカルな）型が作られてしまっていた。

.NET 10からは、この引数が本当にジェネリック型定義であるかどうかを検証するようになり、そうでない場合は即座に`ArgumentException`がスローされるようになった。

変更理由は単純に「不正な入力を早期にエラーとして検出できるようにするため」。以前は不正な使い方をしても気づかれないまま処理が進んでしまい、後になって原因不明の不具合として表面化するリスクがあった。

【放置したときの影響】

このAPIは非常にニッチで、dicom-tool-3のような業務アプリケーションコードで直接呼び出すことはまず考えられない。仮に何らかのライブラリ内部でこのAPIが誤った引数で使われていた場合、以前は静かに変な型が生成されていたのに対し、.NET 10では即座に例外が飛ぶようになる、という違いがある。

```csharp
// 非ジェネリック型を渡してしまった場合
// .NET 9以前: エラーにならず、意味の通らない型が作られてしまう
// .NET 10以降: ArgumentExceptionがスローされる
Type instantiatedType = Type.MakeGenericSignatureType(typeof(string), new[] { typeof(int) });
```

【プロジェクトでの調べ方】

```
Grep: "MakeGenericSignatureType" (プロジェクト全体)
```
で検索したが0件だった。dicom-tool-3ではこの低レベルなリフレクションAPIを使用しているコードもライブラリも見当たらない。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

改修不要。もし将来このAPIを使う場面が出てきたら、呼び出し前に型がジェネリック型定義かどうかを確認する。

```csharp
// 改修前
Type instantiatedType = Type.MakeGenericSignatureType(originalType, instantiation);

// 改修後
Type instantiatedType = originalType.IsGenericTypeDefinition
    ? Type.MakeGenericSignatureType(originalType, instantiation)
    : originalType;
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

## シリアル化

### System.Text.Json はプロパティ名の競合をチェックします
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/serialization/10/property-name-validation

【前提知識】

- **`System.Text.Json`とは**
  .NET標準のJSONシリアライザー。C#のオブジェクトをJSON文字列に変換（シリアル化）したり、その逆（逆シリアル化）を行ったりするための機能。backend/DicomTool.ApiのようなASP.NET Core Web APIでは、リクエスト/レスポンスのJSON変換に標準で使われている。
- **ポリモーフィズム（多態性）とシリアル化とは**
  C#では、基底クラス`Animal`型の変数に、実際には派生クラス`Dog`のインスタンスが入っていることがある（ポリモーフィズム）。これをJSONにシリアル化してから復元する際、「このJSONは元々どの派生クラスだったのか」という情報をJSON側にも埋め込んでおかないと、正しい型に戻せない。`System.Text.Json`ではこの「型を表す情報」を`$type`のような特別な名前のプロパティとしてJSONに自動的に出力する仕組み（`[JsonPolymorphic]`、`[JsonDerivedType]`属性で設定）を持っている。
- **メタデータプロパティとは**
  上記の`$type`のほか、参照のループ検出・保持のための`$id`・`$ref`など、シリアライザー自身が管理のために予約している特別な名前のJSONプロパティ。`TypeDiscriminatorPropertyName`のように、この名前自体をカスタマイズできる場合もある（例：`$type`の代わりに`Type`という名前を使う、など）。

【説明】

以前は、開発者が独自に定義したプロパティ名（例：クラスに`public string Type { get; set; }`のようなプロパティを持たせる）が、たまたまシリアライザーが予約しているメタデータプロパティ名（`$type`のカスタム名として設定した`Type`など）と衝突していても、シリアライザーは何もチェックしていなかった。その結果、同じ名前のプロパティがJSON内に2つ出力される（重複キー）という、正しく読み戻せない壊れたJSONが生成されてしまうことがあった。

.NET 10からは、このような名前の衝突をシリアライザー作成時・シリアル化実行時に検証するようになり、衝突が見つかると`InvalidOperationException`が早期に発生するようになった。

変更理由は、実行時（特にデシリアライズ時）になって初めて壊れたJSONだと発覚するより、シリアライズしようとした時点でエラーとして検出できたほうが、開発中に問題へ気づきやすいため。

【放置したときの影響】

もしdicom-tool-3のDTO（GraphQLモデルやAPIレスポンス型など）で、`[JsonPolymorphic]`によるポリモーフィックシリアル化を使いつつ、たまたま`Type`のような名前のプロパティを独自に持たせていた場合、.NET 10ではシリアライザーの作成時点で例外が発生するようになる。

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(Dog), "dog")]
public abstract class Animal
{
    public abstract string Type { get; } // ← メタデータ用の"Type"と名前が衝突
}

// .NET 9以前: 実行はできるが、{"Type":"dog","Type":"Dog"} のような壊れたJSONを出力してしまう
// .NET 10以降: InvalidOperationExceptionが即座にスローされる（作成時または初回シリアル化時）
```

【プロジェクトでの調べ方】

```
Grep: "JsonPolymorphic" / "JsonDerivedType" / "TypeDiscriminatorPropertyName" (*.cs 全体)
```
で検索したところ、0件だった。dicom-tool-3のbackend/DicomTool.ApiはGraphQL（HotChocolate）を主に使っており、System.Text.Jsonのポリモーフィックシリアル化機能自体を利用していない。frontend/timeline（Blazor WebAssembly）側のDTO（`GraphQLModels.cs`など）も確認したが、同様に`[JsonPolymorphic]`は使われていない。**この変更は現時点のdicom-tool-3には影響しない。**

念のため補足すると、この変更は「ポリモーフィックシリアル化を使っていて、かつ名前が衝突している場合」のみ発生するため、通常のDTOをそのままシリアル化しているだけの箇所（本プロジェクトの大半のコード）には無関係。

【改修方法】

現状は改修不要。将来ポリモーフィックシリアル化を導入する際は、メタデータ名と衝突しうるプロパティ名を避けるか、衝突するプロパティに`[JsonIgnore]`を付ける。

```csharp
// 改修前（"Type"がメタデータ名と衝突する可能性）
[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(Dog), "dog")]
public abstract class Animal
{
    public abstract string Type { get; }
}

// 改修後（プロパティ名を変える、または無視する）
[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(Dog), "dog")]
public abstract class Animal
{
    [JsonIgnore]
    public abstract string Category { get; } // 名前を変更してメタデータ名との衝突を回避
}
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### XmlSerializer が ObsoleteAttribute でマークされたプロパティを無視しなくなりました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/serialization/10/xmlserializer-obsolete-properties

【前提知識】

- **`XmlSerializer`とは**
  C#のオブジェクトをXML形式のテキストへ変換したり、その逆を行ったりするための.NET標準クラス（`System.Xml.Serialization`名前空間）。SOAP通信や、レガシーなXML設定ファイルの読み書きなどで使われる。dicom-tool-3のようなDICOM関連システムでは、DICOM自体はXMLではなくバイナリ形式が主だが、周辺のレポートファイルや設定ファイルでXMLが使われる可能性はある。
- **`[Obsolete]`属性とは**
  「このメンバー（プロパティやメソッド）はもう非推奨なので、新しいコードでは使わないでほしい」ということをコンパイラに伝えるための属性。これを付けると、そのメンバーを使っているコードをビルドした際に警告（`IsError = true`にするとエラー）が出るようになる。あくまで「開発者への警告」を目的とした属性であり、本来は実行時の挙動（シリアル化されるかどうかなど）には影響しないはずのものだった。
- **`[XmlIgnore]`属性とは**
  `XmlSerializer`に対して「このプロパティはXMLシリアル化の対象から明示的に除外してほしい」と伝えるための属性。`[Obsolete]`とは本来まったく別の目的の属性。

【説明】

以前の`XmlSerializer`には、意図しないバグとして、`[Obsolete]`属性が付いたプロパティを、あたかも`[XmlIgnore]`が付いているかのように扱い、XMLシリアル化の対象から除外してしまう挙動があった。これは仕様ではなく単なるバグであり、「非推奨だと警告したいだけ」のつもりで`[Obsolete]`を付けたら、知らないうちにそのプロパティがXML出力から消えてしまう、という予期しない副作用を引き起こしていた。

.NET 10では、このバグが修正され、`[Obsolete]`が付いたプロパティも既定でシリアル化されるようになった。ただし、`[Obsolete(IsError = true)]`（「これを使うとコンパイルエラーにする」という強い非推奨指定）が付いている場合は、シリアライザーの作成時点で`InvalidOperationException`が投げられるようになる。以前の挙動に戻したい場合のために、`AppContext`スイッチ`Switch.System.Xml.IgnoreObsoleteMembers`も用意された。

変更理由は、「非推奨マークを付けただけなのに実行時の動作（シリアル化されるかどうか）まで変わってしまう」というのが`[Obsolete]`属性本来の目的（コンパイル時警告のみ）と矛盾していたため。バグ修正という位置づけだが、既存コードの挙動を変えるため破壊的変更として案内されている。

【放置したときの影響】

dicom-tool-3内で`XmlSerializer`と`[Obsolete]`を組み合わせて使っているクラスがあった場合、影響は大きく2パターンに分かれる。

1. **これまで意図せずXML出力から除外されていたプロパティが、.NET 10からは出力されるようになる。** 出力先のXMLを読む相手（他システムや過去バージョンとの連携）が、増えたプロパティを想定していないと、互換性問題（想定外のフィールドが増える）が起きる可能性がある。
2. **`[Obsolete(IsError = true)]`を付けたプロパティを持つクラスをシリアル化しようとしていた場合、.NET 10へ上げた瞬間に例外で落ちるようになる。** これは実行時にいきなり気づく形になるため、影響としては大きい。

```csharp
public class Example
{
    public string NormalProperty { get; set; } = "normal";

    [Obsolete("This property is deprecated")]
    public string ObsoleteProperty { get; set; } = "obsolete";
}

var serializer = new XmlSerializer(typeof(Example));
// .NET 9以前: ObsoletePropertyはXMLに出力されない
// .NET 10以降: ObsoletePropertyもXMLに出力されるようになる
```

【プロジェクトでの調べ方】

```
Grep: "XmlSerializer" (*.cs 全体)
```
で検索したが0件だった。dicom-tool-3全体で`System.Xml.Serialization.XmlSerializer`を使っている箇所は見当たらない（DTOやモデルのシリアル化はGraphQL/System.Text.Json中心で、XMLベースの通信・保存は行われていない）。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

現状は改修不要。将来XMLシリアル化を導入し、かつ`[Obsolete]`を付けたプロパティが存在する場合は、意図に応じて以下のいずれかを選ぶ。

```csharp
// 選択肢1: 「非推奨だがXMLには引き続き含めたい」なら何もしなくてよい（.NET 10の既定動作）

// 選択肢2: 「これまで通りXMLから除外したい」場合は、明示的に[XmlIgnore]を付ける
public class Example
{
    [Obsolete("This property is deprecated")]
    [XmlIgnore]
    public string ObsoleteProperty { get; set; } = "obsolete";
}

// 選択肢3: アプリ全体で旧挙動に戻したい場合はAppContextスイッチを起動時に設定する
AppContext.SetSwitch("Switch.System.Xml.IgnoreObsoleteMembers", true);
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

## Windows Presentation Foundation (WPF)

### 空の ColumnDefinitions と RowDefinition は許可されていません
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/wpf/10.0/empty-grid-definitions

【前提知識】

- **WPF（Windows Presentation Foundation）とは**
  Windowsデスクトップアプリを作るためのUIフレームワークの1つ。画面のレイアウトをC#ではなくXAMLというXMLベースのマークアップ言語で記述するのが特徴。dicom-tool-3の`services/DicomTool.TrayApp`はデスクトップUIを持つが、WPFではなくWinForms（`System.Windows.Forms`）で作られている点に注意（両者は別のUIフレームワークで、XAMLはWPF固有の機能）。
- **`Grid`と`ColumnDefinitions`/`RowDefinitions`とは**
  WPFのレイアウトで最もよく使う要素の1つが`Grid`（表形式のレイアウト）。何行・何列にするかを`<Grid.ColumnDefinitions>`・`<Grid.RowDefinitions>`の中に`<ColumnDefinition />`・`<RowDefinition />`を並べて宣言する。
- **XAMLのコンパイルエラー（`MC3063`など）とは**
  XAMLファイルはビルド時に専用のコンパイラ（マークアップコンパイラ）でチェックされ、問題があると`MCxxxx`という形式のエラーコードとともにビルドが失敗する。`MC3063`は「プロパティに値が設定されていない」ことを示すエラー。

【説明】

以前は、`<Grid.ColumnDefinitions>`や`<Grid.RowDefinitions>`をXAML内で宣言しておきながら、その中身（`<ColumnDefinition />`など）を1つも書かず空のままにしても、ビルドは正常に通っていた。この場合、実行時のレイアウトは「1行1列」という既定状態のまま扱われていた。

.NET 10からは、この「宣言だけあって中身が空」というパターンがビルドエラー（`MC3063`）になるように変更された。

変更理由は、この変更を単独で意図したものではなく、WPFに新しく追加された「Grid XAML短縮構文」（`<Grid RowDefinitions="Auto,*" ColumnDefinitions="*,Auto" />`のように属性で簡潔に行・列を書ける新機能）を実装した副作用として発生したもの。

【放置したときの影響】

dicom-tool-3はWPFアプリを含まないため、実害は発生しない。仮にWPFプロジェクトが将来追加され、以下のようなXAMLがあった場合はビルドが失敗するようになる。

```xml
<!-- .NET 9以前はビルドが通っていた（意図的に空にしていたのか、書き忘れなのか判別しづらいコード） -->
<Grid>
  <Grid.ColumnDefinitions>
  </Grid.ColumnDefinitions>
</Grid>
```

```
error MC3063: Property 'ColumnDefinitions' does not have a value.
```

【プロジェクトでの調べ方】

```
Glob: **/*.xaml
```
で検索したところ、dicom-tool-3リポジトリ内には`.xaml`ファイルが1件も存在しなかった。`services/DicomTool.TrayApp`のプロジェクトファイル（`DicomTool.TrayApp.csproj`）を確認しても、`<UseWindowsForms>true</UseWindowsForms>`とあるのみで、WPF向けの`<UseWPF>true</UseWPF>`設定は無い。**dicom-tool-3にはWPFプロジェクトが存在しないため、この変更（および次のDynamicResourceの変更）は現時点で一切影響しない。**

【改修方法】

改修不要（WPFを使用していないため）。将来WPFデスクトップアプリを追加する場合に備えて、「空の`<Grid.ColumnDefinitions>`は書かない」というルールだけ覚えておけばよい。

```xml
<!-- 修正例：少なくとも1つの要素を入れる -->
<Grid>
  <Grid.ColumnDefinitions>
    <ColumnDefinition />
  </Grid.ColumnDefinitions>
</Grid>
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### DynamicResource を正しく使用すると、アプリケーションがクラッシュする
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/wpf/10.0/dynamicresource-crash

【前提知識】

- **WPFのリソースシステムとは**
  WPFでは色・ブラシ・スタイルなどの「使い回す値」を`ResourceDictionary`にキーを付けて登録しておき、XAMLの各所から参照する仕組みがある。参照方法には大きく2種類あり、`StaticResource`はXAMLの読み込み時に一度だけ値を解決するのに対し、`DynamicResource`は実行時にリソースの値が変わるたびに参照先を再評価する（例えばアプリのテーマ切り替えなどで使われる）。
- **`SolidColorBrush`と`Color`の違いとは**
  WPFで「色」を扱う型は複数ある。`Color`は純粋な色の値（RGBAなど）そのものを表す構造体。`SolidColorBrush`はその色を使って図形やテキストを塗りつぶすための「ブラシ」オブジェクトで、`Color`をラップしたもの。`Background`や`Foreground`のようなプロパティは`Brush`型を要求するため、そこに`Color`をそのまま渡すのは型として誤り。

【説明】

以前は、`DynamicResource`で参照先として本来期待される型（例：`Brush`が必要な場所に`SolidColorBrush`）ではなく、誤った型（例：`Color`）を指定してしまっていても、WPFアプリはクラッシュせずに動き続けていた。内部的には型が合わずエラーになっているにもかかわらず、値は既定値にフォールバックし、`InvalidOperationException`は出力（デバッグ出力など）に表示されるだけで、アプリの実行自体は継続していた。

.NET 10からは、この「誤った型を指定しているのにクラッシュせず動き続ける」という状態が許されなくなり、`XamlParseException`が発生してアプリケーションがクラッシュするようになった。

変更理由は`DynamicResource`のパフォーマンス向上のため。以前の「動き続けさせる」実装は、内部的に型の不一致を毎回吸収するための余分な処理を必要としており、これが最適化の妨げになっていた。誤りを早期に（実行時に派手に）検出できるようにする代わりに、パフォーマンスを改善するトレードオフを選んだ変更。

【放置したときの影響】

dicom-tool-3はWPFアプリを含まないため実害は発生しない。仮にWPFプロジェクトがあり、以下のようなXAMLの型の誤りがあった場合は、これまで「見た目がおかしいだけで動いていた」ものが、.NET 10からは即座にクラッシュするようになる。

```xml
<!-- ResourceNameのColorに"Color"型のDynamicResourceを割り当てようとしている（本来はBrushであるべき） -->
<SolidColorBrush x:Key="RedColorBrush" Color="#FFFF0000" />
<SolidColorBrush x:Key="ResourceName" Color="{DynamicResource RedColorBrush}" />
```

```
System.Windows.Markup.XamlParseException: Set property 'System.Windows.ResourceDictionary.Source' threw an exception.
```

【プロジェクトでの調べ方】

前項目と同様、`Glob: **/*.xaml`で0件だったことに加え、`.csproj`に`<UseWPF>true</UseWPF>`を設定しているプロジェクトも存在しないことを確認済み。**dicom-tool-3にはWPFプロジェクトが存在しないため、この変更は現時点で一切影響しない。**

【改修方法】

改修不要（WPFを使用していないため）。将来WPFを使う場合は、`DynamicResource`で参照するリソースのキーが、実際に使われる場所の型（`Brush`なのか`Color`なのかなど）と一致しているかを確認する習慣をつける。

```xml
<!-- 修正例：Colorキーで登録し、Brush側でColorプロパティとして参照する -->
<Color x:Key="RedColor">#FFFF0000</Color>
<SolidColorBrush x:Key="ResourceName" Color="{DynamicResource RedColor}" />
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

## ネットワーク

### PublishTrimmed で HTTP/3 のサポートが既定で無効になっている
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/networking/10.0/http3-disabled-with-publishtrimmed

【前提知識】

- **HTTP/3とは**
  HTTPプロトコルの最新バージョン。QUICという新しい下位プロトコル（従来のTCPの代わりにUDP上で動く）を使うことで、通信の高速化や接続切り替えへの耐性向上などを狙ったもの。.NETの`System.Net.Http`（`HttpClient`）は内部的にHTTP/3をサポートしている。
- **`msquic`とは**
  .NETがHTTP/3を実際に喋るために内部で使っているネイティブライブラリ（QUICプロトコルの実装）。これが実行環境に存在しない・正しく初期化できない場合、HTTP/3は実質的に使えない。
- **`PublishTrimmed`／`PublishAot`とは**
  「トリミング（Trimming）」の項目で説明した通り、発行時に未使用コードを削って配布サイズを減らす機能が`PublishTrimmed`。`PublishAot`はNativeAOTでの発行を有効にするオプション。どちらも、`.csproj`のプロパティとして`<PublishTrimmed>true</PublishTrimmed>`のように明示的に設定するオプトイン機能であり、既定では無効。

【説明】

以前は、`PublishTrimmed`や`PublishAot`を有効にしていても、HTTP/3関連のコードはトリミングされずアプリに含まれたままだった。しかし、HTTP/3を実際に動かすには`msquic`ネイティブライブラリが必要で、これが使えない環境（多くの一般的な実行環境）では、そもそもHTTP/3は動作していなかった。つまり「動かないコードのために、無駄に配布サイズだけが増えていた」状態だった。

.NET 10からは、`PublishTrimmed`または`PublishAot`が`true`の場合、HTTP/3のサポートコードが既定でトリミングされ、アプリに含まれなくなった。

変更理由は、HTTP/3を実際に機能させるには追加のセットアップ（msquicの用意など）が必要で、多くの場合そのままでは動作しないため、トリミング／AOT発行という「サイズと起動速度を重視するシナリオ」において、動作しない機能のためのコードを削って最適化する方が理にかなっている、という判断。

【放置したときの影響】

dicom-tool-3の各サービスは現状トリミング発行やAOT発行を使用していないため、直接の実害は無い。ただし、将来これらの最適化を導入し、かつHTTP/3を積極的に使いたい場面（例：外部のHTTP/3対応サーバーとの高速通信）があった場合、何も設定しないと以下のようにHTTP/3が使えなくなる。

```xml
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>
  <!-- Http3Supportを明示しないと、HTTP/3関連コードごとトリミングされてしまう -->
</PropertyGroup>
```

【プロジェクトでの調べ方】

```
Grep: "PublishTrimmed" / "PublishAot" (*.csproj 全体)
```
で全プロジェクトファイルを検索したが、いずれも0件だった。dicom-tool-3のすべてのサービス（`backend/DicomTool.Api`、`services/DicomTool.Worker`、`services/DicomTool.DicomScp`、`services/DicomTool.TrayApp`、`services/DicomTool.StorageGuard`、`frontend/timeline`）は現状トリミング発行・AOT発行のいずれも使用していない。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

現状は改修不要。将来トリミング／AOT発行かつHTTP/3が必要になった場合は、以下のプロパティを追加する。

```xml
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>
  <Http3Support>true</Http3Support>
</PropertyGroup>
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### MailAddress では、連続するドットの検証が適用されます
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/networking/10.0/mailaddress-consecutive-dots

【前提知識】

- **`System.Net.Mail.MailAddress`クラスとは**
  C#で「1つのメールアドレス」を表現するためのクラス。単にメールを送信する際の宛先指定だけでなく、「この文字列は妥当なメールアドレスの形式か？」を検証する目的で使われることもある（コンストラクターに文字列を渡し、例外が出なければ一応の形式チェックを通過した、とみなす使い方）。
- **RFC 5322／RFC 2822とは**
  電子メールのメッセージ形式（メールアドレスの書式を含む）を定めたインターネット標準の仕様書（RFC = Request for Comments）。「ローカル部（`@`より前）」「ドメイン部（`@`より後）」いずれにおいても、ドットを連続させる（`..`のように）ことは仕様上、正式には認められていない。
- **`FormatException`とは**
  文字列を特定の形式のデータに変換しようとした際、その文字列が期待する形式に合っていなかった場合にスローされる標準的な例外。

【説明】

以前の`MailAddress`は、`test..address@example.com`のようにローカル部やドメイン部にドットが連続しているメールアドレスであっても、例外を投げずに受け入れてしまっていた（本来この形式はRFC上は無効）。

.NET 10からは検証がより厳格になり、連続するドットを含むメールアドレスを渡すと`FormatException`がスローされるようになった。これにより`MailAddress`の挙動がRFC 5322/RFC 2822の仕様によりいっそう忠実になった。

変更理由は、標準仕様に準拠していない緩い検証のままだと、`MailAddress`をメールアドレスのバリデーション（入力検証）目的で使っているアプリケーションが、本来は無効なはずのアドレスを誤って「妥当」と判定してしまう問題があったため。

【放置したときの影響】

dicom-tool-3が「患者や医師のメールアドレスを入力させ、`MailAddress`クラスで形式チェックをしている」ような機能を持っていた場合、以下のように挙動が変わる。

```csharp
using System.Net.Mail;

// .NET 9以前: 例外なく通ってしまう（本来は無効な形式）
// .NET 10以降: FormatExceptionがスローされる
var email = new MailAddress("test..address@example.com");
```

これは基本的には「より厳密に、正しく検証されるようになる」という改善方向の変更のため、放置した場合の実害としては、これまで（誤って）許可されていた一部の入力が、.NET 10からは弾かれるようになる、という程度。もし業務データの中に実際にこの形式（連続ドット）のメールアドレスが登録済みで、それを読み込む処理に`MailAddress`を使っていた場合は、実行時に予期しない例外で処理が止まる可能性がある。

【プロジェクトでの調べ方】

```
Grep: "MailAddress" / "EmailAddress" (*.cs 全体)
```
で検索したが、いずれも0件だった。dicom-tool-3にはメール送信機能やメールアドレスのバリデーション処理自体が現状実装されていない（DICOM通信・検査データ管理が主目的のシステムであり、メール関連の機能は無い）。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

現状は改修不要。将来メールアドレスの検証機能を実装する場合は、`FormatException`を想定した例外処理を組み込んでおく。

```csharp
using System.Net.Mail;

try
{
    var email = new MailAddress(inputEmail);
}
catch (FormatException ex)
{
    // ユーザーに「メールアドレスの形式が正しくありません」等を案内する
    Console.WriteLine($"Invalid email address: {ex.Message}");
}
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### ブラウザーの HTTP クライアントで既定で有効になっているストリーミング HTTP 応答
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/networking/10.0/default-http-streaming

【前提知識】

- **Blazor WebAssembly（Blazor WASM）とは**
  C#とWebAssembly（ブラウザ上でネイティブに近い速度でコードを実行できる仕組み）の技術を使って、ブラウザの中でC#コードをそのまま動かすことができるSPA（シングルページアプリケーション）フレームワーク。サーバー側にC#ランタイムを置く必要がなく、静的ファイルとして配信してブラウザ上で完結して動く。dicom-tool-3の`frontend/timeline`プロジェクトはまさにこのBlazor WebAssemblyで作られている（`Microsoft.NET.Sdk.BlazorWebAssembly`）。
- **Blazor WASM上の`HttpClient`とは**
  通常の.NETの`HttpClient`はOSのソケットAPIを直接使うが、Blazor WASM環境ではブラウザのサンドボックス内で動くため、内部的にはブラウザ標準の`fetch` APIを経由して通信している。
- **ストリーミングとバッファリングの違いとは**
  HTTPレスポンスの受信方法には大きく2種類ある。「バッファリング」は、レスポンス全体をメモリ上に一度に読み込んでから処理を始める方式（`MemoryStream`として扱われる）。「ストリーミング」は、データが届いた端から少しずつ順番に読み進めていく方式。ストリーミングの方がメモリ効率がよく、巨大なレスポンスや逐次到着するデータ（サーバー送信イベントに近いもの）を扱いやすい一方、同期的な読み取り（`Stream.Read`など、待たずにすぐ結果を返すAPI）はサポートされない場合がある。
- **`HttpContent.ReadAsStreamAsync`とは**
  `HttpClient`で受け取ったレスポンスの本文（ボディ）を、`Stream`（読み込み用のストリームオブジェクト）として取得するための非同期メソッド。

【説明】

以前のBlazor WASM環境では、`HttpClient`は既定でHTTPレスポンス全体をバッファリングしていた。`HttpContent.ReadAsStreamAsync`を呼び出すと、内部的に`MemoryStream`（メモリに全部読み込み済みのストリーム）が返っていた。ストリーミングで受信したい場合は`WebAssemblyEnableStreamingResponse`というオプションを使って明示的にオプトインする必要があった。

.NET 10からは、この既定動作が逆転し、ストリーミングでの受信が既定で有効になった。その結果、`HttpContent.ReadAsStreamAsync`は`MemoryStream`ではなく`BrowserHttpReadStream`という別の種類のストリームを返すようになった。この`BrowserHttpReadStream`は、同期的なストリーム操作（結果を待たずにすぐ返す`Read`など）をサポートしていない。

変更理由は、`GetFromJsonAsAsyncEnumerable`（JSONの配列を1件ずつ非同期に列挙しながら処理していく機能）のようなストリーミングを前提としたユースケースをサポートするため。

【放置したときの影響】

dicom-tool-3の`frontend/timeline`はBlazor WebAssemblyで作られており、この変更の対象環境に該当する。ただし実際のコード（`Services/GraphQLClient.cs`）を確認したところ、レスポンスの読み取りには`response.Content.ReadFromJsonAsync<T>(...)`（`System.Net.Http.Json`が提供する非同期の拡張メソッド）を使っており、これは内部的に`ReadAsStreamAsync`を使いつつも、あくまで非同期に読み進める作りになっている。同期的なストリーム操作（`.Result`で待つ、`Stream.Read`を同期的に呼ぶなど）は使われていない。そのため、**現状のコードは新しいストリーミング既定動作でも問題なく動作すると考えられる。**

一般論として、もし同期的にストリームを読むコード（`stream.Read(buffer, 0, count)`のような同期メソッド呼び出しや、`.Result`で無理やり同期化しているコード）がBlazor WASM側にあった場合は、.NET 10で例外や動作不良が発生しうる。

```csharp
// もしこのようなコードがあった場合は影響を受けうる（本プロジェクトには該当箇所なし）
var stream = await response.Content.ReadAsStreamAsync();
int bytesRead = stream.Read(buffer, 0, buffer.Length); // BrowserHttpReadStreamは同期読み取り非対応
```

【プロジェクトでの調べ方】

```
Grep: "ReadAsStreamAsync" / "ReadAsStream" / "HttpContent" / "\.Read\(" / "\.CopyTo\(" (frontend/timeline 配下, *.cs)
```
で検索したが該当なし。`frontend/timeline/DicomTool.Timeline/Services/GraphQLClient.cs`の実装を確認したところ、`_http.SendAsync(request)`でレスポンスを受け取ったあと`response.Content.ReadFromJsonAsync<GraphQLResponse<T>>(JsonOptions)`で非同期にJSONへ変換しており、同期ストリーム操作は使われていない。**この変更はdicom-tool-3に技術的には該当する環境（Blazor WASM）ではあるものの、実際のコードは非同期APIのみを使っているため、実害は無いと考えられる。**

【改修方法】

現状は改修不要。ただし念のため、将来`frontend/timeline`でストリームを扱うコードを追加する際は、必ず非同期のAPI（`ReadAsync`、`await`を使った読み取り）で統一する。もし何らかの理由で旧来のバッファリング挙動に戻したい場合は、以下のように明示的に無効化できる。

```csharp
// リクエスト単位で無効化する場合
request.Options.Set(new HttpRequestOptionsKey<bool>("WebAssemblyEnableStreamingResponse"), false);

// プロジェクト全体で無効化する場合（.csprojに追記）
```
```xml
<PropertyGroup>
  <WasmEnableStreamingResponse>false</WasmEnableStreamingResponse>
</PropertyGroup>
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### Uri 長さの制限が削除されました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/networking/10.0/uri-length-limits-removed

【前提知識】

- **`System.Uri`クラスとは**
  URL/URI（`https://example.com/path?query=1`のような文字列）をパースし、スキーム・ホスト・パス・クエリ文字列などの各部分に分解して扱うための.NET標準クラス。HTTP通信を行うほぼすべてのC#コードでどこかしら登場する基本的な型。
- **`data:` URIとは**
  `data:image/png;base64,iVBORw0KG...`のように、外部のURLを指すのではなく、データそのもの（Base64エンコードされたバイナリなど）をURI文字列の中に直接埋め込む形式。画像などを1つのファイルやリクエストに埋め込みたい場合に使われることがある。データ量次第でURI文字列自体が非常に長くなりうる。

【説明】

以前は、`Uri`のコンストラクターや`Uri.TryCreate`で新しい`Uri`インスタンスを作ろうとした際、URI文字列全体の長さがおおよそ65,000文字を超えると、`UriFormatException`（「Uri文字列が長すぎます」）が発生し、インスタンスを作成できなかった。

.NET 10からは、この長さ制限が撤廃され、実質的に上限なく長い`Uri`インスタンスを作成できるようになった（ただし文字列としての実用上の上限や、スキーム・ホストなど一部コンポーネントには引き続き別の制約が残る場合がある）。

変更理由は2つある。1つは、多くのHTTPサーバーが受け付けるURLの長さ制限は、そもそも`Uri`クラスの65,000文字よりずっと短いことが多く、`Uri`側の制限が実務上あまり意味をなしていなかったこと。もう1つは、`Uri`が「URIっぽい情報」を表現する.NETの事実上の標準的な入れ物として、HTTPリクエストとは無関係な用途（`data:`URIで大きなバイナリを表現する、システム間でクエリ文字列に大きなデータを詰めてやり取りする、など）にも広く使われており、65,000文字という制限がそうした正当な用途の妨げになっていたこと。

【放置したときの影響】

これは「制限を撤廃する」方向の変更であり、通常は既存コードを壊す方向には働きにくい（今まで例外が出ていたケースが通るようになるだけ）。ただし、以下のようなケースでは注意が必要。

- これまで「65,000文字を超えたら`UriFormatException`が飛ぶ」ことを利用して、間接的に長さの入力検証を行っていた場合、.NET 10からはその防波堤が無くなる。極端に長い文字列を`Uri`に渡しても例外にならず処理が先に進んでしまい、後段の別の場所（実際にHTTPリクエストを送るタイミングなど）で予期しないエラーになる可能性がある。

```csharp
// .NET 9以前はここでUriFormatExceptionが飛んでいたので、それを入力検証代わりにしていた場合、
// .NET 10ではここでは例外が出ず、後続処理まで進んでしまう
var uri = new Uri($"https://host/{userSuppliedLongString}");
```

【プロジェクトでの調べ方】

```
Grep: "new Uri\(" (*.cs 全体)
```
で検索したところ、`services/DicomTool.Worker/Program.cs`（StorageGuard向けの`HttpClient.BaseAddress`設定）と`frontend/timeline/DicomTool.Timeline/Program.cs`（Blazorアプリ自身のオリジンをベースアドレスに設定）の2箇所で使われていることを確認した。いずれも設定ファイルやビルド時定数から取得した短い固定URLをそのまま`Uri`化しているだけで、ユーザー入力や巨大なクエリ文字列を含む可能性がある使い方ではない。**この変更は現時点のdicom-tool-3には実質的な影響はない。**

DICOM通信自体（C-ECHO/C-STORE/C-FIND/C-MOVEなど）はDIMSEというバイナリベースのプロトコルであり、HTTPのURLとは別物のため、そもそも本変更の対象になりにくい点も補足しておく。

【改修方法】

現状は改修不要。もし将来、ユーザー入力やクエリパラメーターを含む`Uri`を組み立てる処理を追加する場合は、`Uri`の例外に頼らず、アプリ側で明示的に長さのバリデーションを行うようにする。

```csharp
// 改修例：Uriの制限に頼らず、自前で妥当な長さの上限を設ける
const int MaxUriLength = 8000; // 実際に送信先となるHTTPサーバー等の制限に合わせて決める

if (candidateUriString.Length > MaxUriLength)
{
    throw new ArgumentException("URIが長すぎます。");
}

var uri = new Uri(candidateUriString);
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）
