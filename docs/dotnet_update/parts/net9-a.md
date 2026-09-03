## Containers

### .NET Monitor イメージがバージョン専用タグに簡略化されました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/containers/9.0/monitor-images

【前提知識】
- **.NET Monitorとは**
  コンテナの中で動いている.NETアプリの状態(メモリダンプ、GCの統計、ログ、トレースなど)を外部から取得するための、Microsoft公式の診断ツール。単体のコンテナイメージとして配布されており、監視対象アプリと並べて動かして使う。
- **ディストリビューションレス(distroless)イメージとは**
  「コンテナー イメージによる zlibのインストールの廃止」の項でも触れたが、コンテナーイメージはLinuxディストリビューション(Ubuntu、Azure Linuxなど)を土台に作られる。distrolessイメージは、シェルやパッケージマネージャーなど「アプリの実行に本来不要なもの」を極力削ぎ落とした、最小限のファイルだけを含むイメージ。攻撃対象領域(attack surface)が小さくなりセキュリティ上有利。
- **イメージタグとは**
  同じイメージ名の中でバージョンや種類を区別するための文字列(`mcr.microsoft.com/dotnet/monitor:8-cbl-mariner-distroless`の`8-cbl-mariner-distroless`部分)。

【説明】
.NET Monitor 8までは、Ubuntu Chiseled(Arm64/x64)とCBL-Mariner distroless(Arm64/x64)という2種類のLinuxディストリビューションをベースにしたイメージを両方提供しており、タグ名にも`-ubuntu-chiseled`や`-cbl-mariner-distroless`のようなディストリビューション名のサフィックスが付いていた。

.NET Monitor 9では、CBL-MarinerがAzure Linuxへ統合されたことを受けて、提供するイメージをAzure Linux distrolessの1種類だけに簡略化した。1つのディストリビューションしか提供しなくなったため、タグ名からもディストリビューション名のサフィックスが不要になり、`9`・`9.0`・`9.0.0`のようなバージョン番号だけのシンプルなタグに置き換えられた。`latest`タグの指す実体もUbuntu ChiseledからAzure Linuxベースに変わっている。

【放置したときの影響】
「.NET Monitorイメージを実際に使っている場合のみ」影響がある変更。使っていなければ完全に無関係。

使っている場合、CI/CDのマニフェストやDocker Compose、Kubernetesのマニフェストなどで`8-cbl-mariner-distroless`や`8-ubuntu-chiseled`のような旧タグを指定していると、.NET Monitor 9に対応するイメージが存在しないため、イメージのpullそのものが失敗する(＝デプロイが止まる)。「非推奨で警告が出るだけ」ではなく、該当タグが本当に無くなるタイプの変更なので注意。

【プロジェクトでの調べ方】
- `Dockerfile`という名前のファイルがリポジトリにあるか、また`docker-compose.yml`等に`dotnet-monitor`や`dotnet/monitor`という文字列がないかを確認する。
- 実際にdicom-tool-3を確認したところ、Dockerfileは1つも存在せず(各C#サービスは`dotnet run`で直接起動する運用)、`docker-compose.yml`もPostgreSQLとTemporal用のみで、.NET Monitorイメージは使用されていない。したがって現時点のdicom-tool-3にはこの変更は影響しない。

【改修方法】
該当なし(dicom-tool-3では未使用)。もし将来.NET Monitorをコンテナ監視用に導入する場合は、最初からバージョン専用タグ(`9`や`9.0`)で指定すればこの変更を意識する必要はない。既存の`8-cbl-mariner-distroless`等のタグを使っていた場合は`9`に書き換える。

【参考記事】
- 公式ドキュメントの個別URLが見当たらなかった項目だったため、Web検索で`https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/9.0/monitor-images`(英語版)の存在を確認し、そのja-jp版を実際に取得して本文を作成した。

## 暗号

### System.Security.Cryptography.Pkcs netstandard2.0 から削除された API
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/cryptography/9.0/api-removed-pkcs

【前提知識】
- **PKCS#7/CMSとは**
  電子署名や暗号化されたメッセージをやり取りするための標準フォーマット(Cryptographic Message Syntax)。`System.Security.Cryptography.Pkcs`名前空間はこれを.NETで扱うためのAPI群(`SignedCms`、`EnvelopedCms`など)。
- **netstandard2.0とは**
  .NET Framework・.NET(Core)・Xamarinなど、複数の異なるランタイムで共通して使えるAPIの「契約」を定めたターゲットフレームワーク(TFM)の一種。ライブラリを1つ作っておけば、netstandard2.0に対応する複数の実行環境で動かせる、という仕組み。
  NuGetパッケージは、1つのパッケージの中に複数のTFM向けのビルド成果物を同梱でき、利用側のプロジェクトの実行環境に応じて適切なビルドが自動的に選ばれる。

【説明】
`System.Security.Cryptography.Pkcs`パッケージのバージョン9.0.0〜9.0.2のnetstandard2.0向けビルドには、本来.NET Framework上には存在しないはずのAPI(`CmsSigner.PrivateKey`など)が誤って含まれていた。netstandard2.0をターゲットにするライブラリからこれらのAPIを呼ぶコードはコンパイルが通ってしまうが、実際に.NET Framework上でそのライブラリを実行すると、実行時に`MissingMemberException`が発生していた。

これはパッケージのビルド方法の変更に伴う「作り込みミス」であり、.NET Frameworkでは動作しえないAPIがnetstandard2.0向けに見えてしまっていたのが問題だった。そこでバージョン9.0.3で、これらの誤って含まれていたメンバーが削除された。以後は、これらのAPIを呼ぼうとすると、実行時エラーではなく「コンパイルエラー」になる(＝ビルドの時点で気づけるようになった)。

【放置したときの影響】
影響範囲がかなり限定的な変更。「netstandard2.0をターゲットにした共有ライブラリを書いていて、かつ上記のPKCS/CMS関連APIを使っており、それを.NET Framework環境で実行する」という組み合わせに該当しない限り無関係。

net8.0やnet9.0、net10.0のような具体的なTFMを直接ターゲットにしている通常のプロジェクトには全く影響しない。

【プロジェクトでの調べ方】
- 各プロジェクトの`<TargetFramework>`設定を確認する。dicom-tool-3の全csproj(`DicomTool.Api`、`DicomTool.Worker`、`DicomTool.TrayApp`、`DicomTool.DicomScp`など)を確認したところ、いずれも`net10.0`または`net10.0-windows`を直接指定しており、netstandard2.0をターゲットにしたプロジェクトは存在しなかった。
- `Pkcs`、`CmsSigner`、`SignedCms`、`EnvelopedCms`という文字列でリポジトリ全体をgrepしたが、いずれもヒットせず、そもそもPKCS/CMS関連APIは使用されていない。
- 以上より、dicom-tool-3にはこの変更は影響しない。

【改修方法】
該当なし。もし将来netstandard2.0のライブラリを作り、これらのAPIが必要になった場合は、`net8.0`など、これらのAPIを含む具体的なTFM向けにコンパイルするようにする。

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

### SafeEvpPKeyHandle.DuplicateHandle によるハンドルの up-ref
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/cryptography/9.0/evp-pkey-handle

【前提知識】
- **OpenSSLとEVP_PKEYとは**
  OpenSSLは、Linuxなどで広く使われている定番の暗号ライブラリ(C言語で書かれたネイティブライブラリ)。`EVP_PKEY`は、OpenSSL内部で「鍵(公開鍵・秘密鍵)」を表すデータ構造へのポインタのこと。.NETのLinux版の暗号実装は、内部的にこのOpenSSLを呼び出して鍵の生成や署名処理を行っている。
- **SafeHandleとは**
  OSやネイティブライブラリが管理するリソース(ファイルハンドル、鍵ハンドルなど)を、.NETのGC(ガベージコレクタ)と協調して安全に解放するためのラッパークラス。`SafeEvpPKeyHandle`はEVP_PKEYを.NET側から安全に扱うためのSafeHandle。
- **参照カウント(reference counting)とup-refとは**
  「同じリソースを今何人が使っているか」を数える仕組み。数(カウント)を1つ増やすことを俗に「up-ref」と呼び、そのリソースの利用者が1人増えたことを表す。カウントが0になったとき初めて実体が解放される。「複製(duplicate)」という言葉から連想される「コピーを新しく作る」動作とは異なる点に注意。

【説明】
以前の`SafeEvpPKeyHandle.DuplicateHandle()`は、その名の通り新しい`EVP_PKEY`の実体(コピー)を作成していた。そのため、複製後にOpenSSL APIを直接呼び出して片方のキーを書き換えても、もう片方(元のキー)には影響しなかった。このメソッドは、`ECDsaOpenSsl`や`RSAOpenSsl`のコンストラクタが`SafeEvpPKeyHandle`を受け取るときに内部で使われていた。

.NET9からは、`DuplicateHandle()`は新しい実体を作らず、既存の`EVP_PKEY`の参照カウントを1つ増やして「同じ実体を指す別のハンドル」を返すようになった(up-ref)。そのため、OpenSSL APIを外部から直接呼び出して`EVP_PKEY`の中身を書き換えると、複製された側のハンドル(および、それを使って作られた`ECDsaOpenSsl`/`RSAOpenSsl`インスタンス)にもその変更が反映されるようになった。この変更は、OpenSSLプロバイダーのサポートを有効にするために行われたもので、副次的にパフォーマンスも向上している。

【放置したときの影響】
影響は非常に限定的。`SafeEvpPKeyHandle`や`ECDsaOpenSsl`/`RSAOpenSsl`を直接扱うのは、Linux環境でOpenSSLと低レベルに相互運用するような、かなり専門的なシナリオに限られる。通常の`X509Certificate2`や`RSA.Create()`のような高レベルAPIしか使わない開発では、このクラス自体に触れることがない。

【プロジェクトでの調べ方】
`SafeEvpPKeyHandle`、`ECDsaOpenSsl`、`RSAOpenSsl`という文字列でリポジトリ全体をgrepしたが、いずれもヒットせず、dicom-tool-3ではこれらの低レベルなOpenSSL相互運用APIは使用されていない(暗号処理・TLS通信を今後実装する場合も、通常は`X509Certificate2`や`SslStream`などの高レベルAPI経由になると想定され、この変更の影響を受ける可能性は低い)。

【改修方法】
該当なし。もし使っている場合は、.NET API側に渡した`EVP_PKEY`を外部のOpenSSL APIで直接変更することを避け、変更が避けられないなら事前に`EVP_PKEY`自体のコピーを作成してから変更するようにする。

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

### 一部の X509Certificate2 および X509Certificate コンストラクターは廃止予定です
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/cryptography/9.0/x509-certificates

【前提知識】
- **X509証明書とは**
  TLS/HTTPS通信などで使われる、公開鍵とその所有者情報を結びつけるデジタル証明書の標準規格。.NETでは`X509Certificate2`クラスがこれを表す。
- **PKCS#12/PFXとは**
  証明書と、それに対応する秘密鍵をひとまとめにして、パスワードで保護した状態で保存できるファイル形式(拡張子`.pfx`や`.p12`)。サーバー証明書を配布する際によく使われる。
- **1つのコンストラクタが複数の形式を受け付ける、とは**
  これまでの`X509Certificate2`のコンストラクタは、渡された`byte[]`の中身が「X.509単体」なのか「PKCS#7」なのか「PKCS#12/PFX」なのかを自動判別して読み込んでいた。「よしなにやってくれる」便利な反面、意図と違う形式のデータを渡してしまっても気づけない、という危険もあった。

【説明】
`byte[]`や`ReadOnlySpan<byte>`、あるいはファイルパスとしての`string`を受け取る`X509Certificate`/`X509Certificate2`のコンストラクタ、および`X509Certificate2Collection.Import`メソッドが、.NET9以降で廃止予定(Obsolete)としてマークされた。これらを呼び出すと、コンパイル時に警告`SYSLIB0057`が出るようになる(実行自体はまだ可能)。

廃止予定にされた理由は、これらのAPIが「複数の形式を1つのコンストラクタで受け付ける」設計になっていたため、本来はX.509単体の証明書だけを読み込むつもりだったのに、渡されたデータが実はPKCS#12だった、というように意図と異なる形式で解釈されてしまう問題が起きていたこと。また、データの解釈のされ方次第で相互運用性の問題(他システムとの間で挙動が食い違う)も起きうる。

【放置したときの影響】
現時点では「廃止予定警告(SYSLIB0057)が出るだけ」であり、動作自体は変わらない(即座に動かなくなるわけではない)。ただし、プロジェクトで`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`のように警告をエラー扱いにする設定をしている場合は、この警告によってビルドそのものが失敗する。また、Obsolete属性が付いたAPIは将来のバージョンで完全に削除される可能性があるため、放置し続けるのは推奨されない。

コード例(警告が出るようになるケース):
```csharp
// SYSLIB0057警告が出る
var cert = new X509Certificate2(pfxBytes, password);
```

【プロジェクトでの調べ方】
`new X509Certificate2(`、`new X509Certificate(`、`X509Certificate2Collection`、`.Import(`という文字列でリポジトリ全体をgrepしたが、dicom-tool-3ではいずれもヒットせず、証明書を読み込むコードは現状存在しない(DICOM通信はTLSを使わない前提、またはfo-dicom等のライブラリが証明書処理を内包していると考えられる)。

TLS対応(DICOM TLSやHTTPS証明書のカスタム読み込みなど)を今後実装する際に、この変更を意識する必要がある。

【改修方法】
.NET9で新設された静的ファクトリクラス`X509CertificateLoader`の、対応するメソッド(`LoadCertificate`、`LoadPkcs12`、`LoadCertificateFromFile`など、読み込みたい形式が明確なメソッド)に置き換える。

```csharp
// before(廃止予定警告が出る、形式は自動判別)
var cert = new X509Certificate2(pfxBytes, password);

// after(読み込む形式(PKCS#12)を明示する)
var cert = X509CertificateLoader.LoadPkcs12(pfxBytes, password);
```

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし。詳細な回避策は本文中で案内されている[SYSLIB0057の解説ページ](https://learn.microsoft.com/ja-jp/dotnet/fundamentals/syslib-diagnostics/syslib0057)を参照)

### Windows 秘密キーの有効期間の簡素化
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/cryptography/9.0/private-key-lifetime

【前提知識】
- **秘密鍵の「有効期間(lifetime)」とは**
  証明書オブジェクトが内部で保持しているネイティブな秘密鍵のハンドル(OS側のリソース)を、いつまでメモリ上に保持し、いつOS側に「もう不要」と伝えて消去するか、という管理の話。C#のオブジェクト自体はGC(ガベージコレクタ)が「誰からも参照されなくなったら」自動で回収するが、その裏でOSが管理しているネイティブリソースは、`Dispose()`が呼ばれるかファイナライズされるタイミングで別途解放する必要がある。
- **X509Certificate2Collectionとは**
  複数の`X509Certificate2`をまとめて扱うためのコレクションクラス。PKCS#12/PFXファイルに複数の証明書が含まれている場合などに使う。

【説明】
Windows上で`PersistKeySet`や`EphemeralKeySet`を指定せずにPKCS#12/PFXを読み込むと、「秘密鍵をいつ不要と判断して消去するか」を.NETが自動的に管理する。以前は、単一の証明書を読み込む場合(`new X509Certificate2(pfx, password, flags)`)と、コレクションとして読み込む場合(`X509Certificate2Collection.Import(...)`)とで、異なる2種類のロジックが使われていた。

特にコレクション読み込みの場合、複数の`X509Certificate2`オブジェクトが同じネイティブな証明書データ(`PCERT_CONTEXT`)を指すコピーとして作られることがあり、そのうちの1つが先に破棄(またはガベージコレクションでファイナライズ)されると、他のコピー側の秘密鍵まで一緒に消去されてしまう、という予想外の挙動があった。この結果、次のようなコードが`CryptographicException`や`NullReferenceException`で失敗することがあった(コレクションの一部がGCで回収されただけで、残りのオブジェクトの秘密鍵操作が失敗する)。

.NET9では、この2種類のロジックを1本化し、常に「PKCS#12/PFXの読み込みから直接生成された`X509Certificate2`インスタンス」に有効期間が紐付くように簡素化された。これにより、以前失敗していたコードが正常に動作するようになった。

【放置したときの影響】
「動作が直る」方向の変更であり、通常は放置して問題ない。Windows環境で`X509Certificate2Collection.Import`を使ってPFXを読み込んでいる場合にのみ関係する。従来の「早期にキーが消えるバグ」に依存した独自の回避コードを書いていた場合にのみ、逆に予期しない挙動になりうるが、そうしたケースは稀。

【プロジェクトでの調べ方】
`X509Certificate2Collection`、`.Import(`という文字列でリポジトリ全体をgrepしたが、dicom-tool-3ではヒットせず、証明書のコレクション読み込みは行われていない(そもそも証明書を読み込む処理自体が現状存在しない)。DICOM通信のTLS対応(mTLS等)で証明書ストアを扱うようになった場合に関係してくる可能性がある。

【改修方法】
通常は改修不要。もしコレクション読み込みで得られた複製オブジェクトに対して`Dispose()`を呼ぶことで意図的にキー消去のタイミングを制御していた場合は、元の読み込み元オブジェクト側も正しく`Dispose()`されているか確認する。

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

## 配置

### 非推奨のデスクトップ Windows/macOS/Linux MonoVM ランタイム パッケージ
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/deployment/9.0/monovm-packages

【前提知識】
- **.NETには複数の実行エンジン(ランタイム)がある**
  通常のサーバー/デスクトップアプリで使われるのがCoreCLR。一方、モバイルアプリ(Xamarin/.NET MAUI)やBlazor WebAssemblyなど、軽量性やクロスプラットフォーム性を重視する場面ではMonoVMという別の実行エンジンが使われる。
- **自己完結型デプロイ(self-contained deployment)とは**
  アプリ本体と一緒に.NETランタイムそのものも同梱して配布する形態。配布先のPCに.NETランタイムがインストールされていなくても動く(その分、配布サイズは大きくなる)。
- **ランタイムパッケージとは**
  自己完結型デプロイを組む際に使われる、特定のOS・CPUアーキテクチャ向けのランタイム実体(バイナリ)を含むNuGetパッケージ(`Microsoft.NETCore.App.Runtime.*`のような命名)。

【説明】
これまで、デスクトップ(Windows/macOS/Linux)向けにMonoVMベースの自己完結型デプロイを組むための、公式にはドキュメント化されていないSDKスイッチが存在していた。.NET9からは、そのために使われていたMonoVM用のランタイムNuGetパッケージ群(`Microsoft.NETCore.App.Runtime.Mono.win-x64`など)が廃止され、パッケージそのものが提供されなくなった。理由は「これらのパッケージに対応した公式な.NETの利用シナリオが元々存在しなかった」ため(=そもそも非公式な使い方だった)。

【放置したときの影響】
通常のASP.NET Core Web API、コンソールアプリ、WinFormsアプリなどはCoreCLRを使うため、この変更とは無関係。デスクトップアプリの自己完結型デプロイでMonoVMを意図的に選んでいるような特殊なプロジェクトのみが対象になる。該当する場合は、パッケージの復元(NuGet restore)自体が失敗し、ビルドや発行(publish)が完全に止まる(「動かなくなる」影響が大きいタイプ)。

【プロジェクトでの調べ方】
各csprojに`<UseMonoRuntime>`のような明示的なMonoVM関連の設定がないか確認する。dicom-tool-3の全csproj(`DicomTool.Api`、`DicomTool.Worker`、`DicomTool.TrayApp`、`DicomTool.DicomScp`、`DicomTool.StorageGuard`、`DicomTool.Shared`など)を確認したところ、いずれも通常のCoreCLR前提の設定であり、MonoVM関連の記述は見当たらなかった。WinFormsの`DicomTool.TrayApp`もWindows向けのCoreCLRベースであり、この変更の対象外。

【改修方法】
該当なし(dicom-tool-3では未使用)。もし移行が必要になった場合は、.NET8 LTSを使い続けるか、対応するランタイムNuGetパッケージが用意されている構成(通常のCoreCLRベースの自己完結型デプロイなど)に切り替える。

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

### アプリのランタイム構成設定で環境変数が優先される
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/deployment/9.0/envvar-precedence

【前提知識】
- **runtimeconfig.jsonとは**
  .NETアプリをビルドすると生成される`<アプリ名>.runtimeconfig.json`というファイルで、GC(ガベージコレクション)のモードや、TieredCompilation(段階的JITコンパイル)の有無など、ランタイムの動作を細かく設定できる。csprojの`<ServerGarbageCollection>true</ServerGarbageCollection>`のような設定も、最終的にこのファイルに反映される。
  なお.NET8以前は`runtimeconfig.json`が優先されていたが、これはASP.NET CoreのappsettingsやIConfigurationの「環境変数が設定ファイルより優先されやすい」という一般的な作法とは逆方向だった、という背景がある。
- **環境変数による上書きとは**
  同じ設定を、ファイルを書き換えずに環境変数(例:`DOTNET_gcServer`)で一時的に上書きする仕組み。Dockerコンテナやサーバーの起動スクリプトから、ビルド成果物を変更せずに挙動を変えたいときによく使われる。

【説明】
以前は、`runtimeconfig.json`の設定値と、対応する環境変数の両方が設定されていた場合、`runtimeconfig.json`側が優先されていた(環境変数は実質無視されていた)。.NET9からはこの優先順位が逆転し、環境変数の方が`runtimeconfig.json`より優先されるようになった。

例えば、`runtimeconfig.json`で`System.GC.Server: true`(サーバーGCを使う設定)と書かれていても、環境変数`DOTNET_gcServer`が`0`(false)に設定されていれば、.NET9ではワークステーションGCで動くようになる(.NET8以前ではサーバーGCのまま動いていた)。この変更が行われた理由は、「.NETや他の一般的なソフトウェアの設定の考え方(環境変数が通常最も優先される)と一貫性を持たせるため」とされている。

【放置したときの影響】
「動作が変わって動かなくなる可能性がある」タイプの変更。特にDocker/Kubernetesのような環境で、コンテナオーケストレーション側やベースイメージ側が`DOTNET_`から始まる環境変数をデフォルトで注入していることがあり、それに気づかずcsproj側で`<ServerGarbageCollection>true</ServerGarbageCollection>`のように明示的に設定していた場合、.NET9に上げると意図せず環境変数側が勝つようになり、GCモードやその他のランタイム挙動が変わってメモリ使用量・スループットに影響が出る可能性がある。

【プロジェクトでの調べ方】
- 各サービスの起動環境(`docker-compose.yml`、Dockerfile、起動スクリプト、CI/CDのパイプライン定義)で`DOTNET_`から始まる環境変数が設定されていないか確認する。
- 各csprojで`<ServerGarbageCollection>`、`<ConcurrentGarbageCollection>`、`<TieredCompilation>`のようなランタイム構成関連の設定がないか確認する。
- dicom-tool-3では`docker-compose.yml`にPostgreSQLとTemporalの設定のみがあり、`DOTNET_`系の環境変数は設定されていない。各csprojにも上記のようなGC関連設定は見当たらず、現時点では環境変数と`runtimeconfig.json`の設定が競合する状況は確認できなかった。

【改修方法】
通常は改修不要。もし環境変数と`runtimeconfig.json`(またはcsproj)の両方で同じ設定を別の値で行っている場合は、どちらが本来意図した値かを確認し、片方に統一するか、意図的に環境変数側で上書きする設計に切り替える。CI/CD・コンテナ環境で意図しない`DOTNET_`系環境変数が注入されていないかも合わせて確認するとよい。

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

## 相互運用機能

### 既定での CET のサポート
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/interop/9.0/cet-support

【前提知識】
- **CET(Control-flow Enforcement Technology)とは**
  Intel/AMDのCPUが持つハードウェアのセキュリティ機能で、「ROP(Return-Oriented Programming、戻り先指向プログラミング)」と呼ばれる攻撃手法を防ぐためのもの。ROP攻撃とは、ソフトウェアの脆弱性を突いてスタック上の「関数呼び出し後に戻ってくるアドレス(リターンアドレス)」を書き換え、既存のプログラムの断片(ガジェット)を繋ぎ合わせて悪意ある処理を組み立てる攻撃手法。
  CETは、通常のスタックとは別に「シャドウスタック」という保護された領域にリターンアドレスの控えを保持しておき、関数から戻るときに両者が一致しなければ異常とみなしてプロセスを強制終了させる、という仕組みでこれを防ぐ。
- **相互運用機能(interop)とは**
  .NETのコードから、C/C++などで書かれたネイティブライブラリ(DLL/共有ライブラリ)を呼び出したり、逆に呼び出されたりする仕組み全般。`[DllImport]`(P/Invoke)がその代表例。
- **スレッドコンテキストとは**
  CPUのレジスタの状態(現在実行中の命令の位置=命令ポインタを含む)をまとめたもの。`SetThreadContext`のようなWindows APIを使うと、実行中のスレッドの「次にどの命令を実行するか」を外部から強制的に書き換えることができる(通常は例外処理やデバッガなどの高度な用途で使われる)。

【説明】
.NET9からは、.NETアプリの実行ファイル(`apphost`/`singlefilehost`)がCET対応(`/CETCOMPAT`コンパイラオプション)でビルドされるようになった。これにより、.NETプロセスに読み込まれた外部の共有ライブラリ(DLL)が、`SetThreadContext`や`RtlRestoreContext`/`NtContinue`、あるいは独自の例外ハンドラーを使ってスレッドの命令ポインタを書き換えようとした場合、その書き換え先が「シャドウスタック上」または「`/EHCONT`オプション等で許可された例外処理の継続先アドレステーブルに載っている場所」のいずれかでなければならなくなった。それ以外の場所に書き換えようとすると、プロセスがその場で強制終了させられる。目的はROP攻撃対策によるセキュリティ強化。

【放置したときの影響】
通常の.NETマネージドコードのみで完結するアプリには影響しない。影響するのは、独自にネイティブDLLをP/Invokeで読み込み、かつそのDLLが低レベルなスレッドコンテキスト操作(独自の例外処理機構、ファイバー/コルーチンの手動切り替え、一部のJIT系フックツールなど)を行っている場合で、この場合Windows上でCETと衝突しプロセスが突然終了する可能性がある。「動作が変わって動かなくなる可能性が大きい」タイプの変更だが、該当するシナリオは限定的。

【プロジェクトでの調べ方】
`[DllImport]`(P/Invoke)でネイティブDLLを呼び出している箇所をgrepで確認する。dicom-tool-3全体を`DllImport`で検索したが該当箇所は見当たらず、DICOM通信もfo-dicom等の管理コード(マネージドコード)ライブラリ経由で行われていると推測され、低レベルなネイティブDLLの直接呼び出しは行われていない。したがって現時点では影響しないと考えられる。ただし、将来的にスキャナ制御SDKや画像処理ライブラリなど、ネイティブDLLをP/Invokeで組み込む場合はこの変更を意識する必要がある。

【改修方法】
問題が発生した場合、プロジェクトファイルに以下を追加してCETをオプトアウトできる。

```xml
<PropertyGroup>
  <CETCompat>false</CETCompat>
</PropertyGroup>
```

または、Windowsのセキュリティアプリやグループポリシーで、対象アプリのみハードウェアによるスタック保護の適用を除外する設定も可能。

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

## JIT コンパイラ

### 浮動小数点から整数への変換が飽和している
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/jit/9.0/fp-to-integer

【前提知識】
- **JIT(Just-In-Time)コンパイラとは**
  .NETのコード(中間言語IL)を、実行するその場でCPUが直接実行できる機械語に変換する仕組み。x86/x64、Armなど、実行しているCPUの種類によって最終的に生成される機械語命令は異なる。
- **浮動小数点から整数へのキャストとは**
  `(int)someDouble`のように、`double`や`float`型の値を`int`などの整数型に変換する処理。変換先の型で表現できないほど大きい/小さい値を変換しようとした場合の挙動は、実はCPUのアーキテクチャや命令セットによって歴史的にバラバラだった。
- **飽和(saturating)動作とは**
  範囲を超えた値を「その型で表現できる最大値または最小値」に丸め込む処理のこと。例えば`int`に収まらないほど大きい値を変換しようとしたとき、単に切り捨てて意味不明な値にするのではなく、`int.MaxValue`に「頭打ち」させるイメージ。
- **NaN(Not a Number)とは**
  「数として定義されない値」を表す特殊な浮動小数点数(`0.0 / 0.0`の結果など)。

【説明】
以前は、`double`/`float`から整数型へのキャストで、変換先の型に収まらない値を渡した場合の挙動が、型ごとに一貫性のない、直感に反するものだった。例えば`int`への変換では、範囲外の値は大きすぎても小さすぎても一律`int.MinValue`になっていた(大きすぎる値のはずなのに最小値になる、という不自然さ)。`uint`への変換に至っては、ビット演算的な、見た目上意味の分かりにくい値が返っていた。

.NET9からは、全プラットフォームで一貫した「飽和動作」に統一された。大きすぎる値は変換先の型の最大値に、小さすぎる値は最小値に、`NaN`は0になる、という直感的なルールに揃えられた。目的は、動作を標準化・決定論的(常に同じ結果になること)にすること。

【放置したときの影響】
通常、想定内の範囲の値をキャストしているコードには影響しない。ただし、意図的あるいは無自覚に「範囲外になりうる値」をキャストしていた場合、結果が変わる。特に`uint`/`ulong`への変換は以前が「意味不明な値」だったため、そこに(意図せず)依存したコードがあると挙動が変わる可能性がある。

コード例:
```csharp
double x = 1e20; // int.MaxValueよりずっと大きい値
int result = (int)x;
// .NET 8以前: int.MinValue が返っていた(直感に反する)
// .NET 9以降: int.MaxValue が返る(「飽和」した直感的な結果)
```

【プロジェクトでの調べ方】
DICOMの画素値やメタデータ処理で、`double`/`float`から整数型への明示的なキャスト(`(int)`、`(uint)`、`(long)`など)を行っている箇所がないか確認する。特に画像のウィンドウ幅/レベル計算、ピクセルデータのスケーリング処理、座標変換など、境界値を超える可能性がある数値計算がないか注意する。

dicom-tool-3のソースコードを確認したところ、現状は画素値・ピクセルデータそのものの数値変換ロジック(ウィンドウ幅/レベル調整や画像処理)は見当たらず、主にDICOMメタデータの管理・保存・転送が中心の実装になっている。そのため現時点でこの変更が実害を及ぼす箇所は確認できなかったが、将来的に画素値処理(サムネイル生成やビューア機能など)を追加する場合は、境界値を超える変換がないか注意すること。

【改修方法】
通常は改修不要(むしろ以前より安全で予測可能な挙動になる)。パフォーマンスが最優先で、以前の(プラットフォーム依存だが高速な)変換挙動が必要な場合は、`float`/`double`/`Half`型に追加された`ConvertToIntegerNative<TInteger>`のような新しいメソッドを使う選択肢もある。ただしこれらのメソッドはプラットフォーム固有の挙動であり、以前の挙動と一致するとは限らない点に注意。

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

### 一部の SVE API が削除されました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/jit/9.0/sve-apis

【前提知識】
- **SVE(Scalable Vector Extension)とは**
  Arm系CPU(Armv8以降の一部)が持つ「SIMD(Single Instruction Multiple Data)」命令セットの一種で、1回の命令で複数のデータをまとめて処理できる高速な演算機能。画像処理や数値計算のような、大量のデータを同じ手順で処理する場面で威力を発揮する。
- **イントリンシック(intrinsics)とは**
  C#のメソッド呼び出しのような見た目をしているが、実際にはJITコンパイラがその呼び出しをCPUの特定の命令に直接置き換えてくれる、非常に低レベルな最適化用API。`System.Runtime.Intrinsics.Arm.Sve`名前空間がArm SVE用のintrinsicsを提供している。
- **Gather系命令とは**
  「複数の異なるメモリアドレスから、一気にまとめてデータを集めてくる」処理を1命令で行う特殊な命令。ここでの「32ビットアドレス」とは、集めてくる先のアドレスをベクトル内で32ビット幅の値として表現している、という意味。

【説明】
Arm SVEのAPIのうち、入力パラメータとして32ビット幅のアドレスを受け取る一部のGatherPrefetch系・GatherVector系のメソッドが、「関連するテストカバレッジが不足している」という理由で.NET9で削除された。将来、テストが十分に整備された時点で再度有効化される可能性がある、と説明されている。

【放置したときの影響】
一般的なアプリ開発でこの超低レベルなArm SVE intrinsicsを直接使うことはまずない(自作の高性能数値計算ライブラリやコーデック実装など、非常に専門的なケースに限られる)。使っていなければ影響はゼロ。

【プロジェクトでの調べ方】
`Sve.GatherPrefetch`、`GatherVectorInt16SignExtendFirstFaulting`、`GatherVectorSByteSignExtendFirstFaulting`、あるいは`System.Runtime.Intrinsics.Arm`名前空間全体の利用有無をgrepで確認する。dicom-tool-3ではこれらのキーワードはいずれもヒットせず、Arm intrinsicsを直接使うコードは存在しない。

【改修方法】
該当なし。もし使っていた場合は、64ビットアドレスを入力として受け取る対応するオーバーロードに置き換える。

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

## ネットワーク

### HttpClient メトリックが無条件に server.port 報告されます
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/networking/9.0/server-port-attribute

【前提知識】
- **OpenTelemetryとは**
  アプリの動作状況(メトリクス、ログ、トレース)を標準化された形式で収集・送信するための、業界標準の仕組み。「メトリクス(metrics)」は「HTTPリクエストにかかった時間」のような数値の時系列データを指す。
  .NETの`HttpClient`は標準でこうしたメトリクスを自動的に発行する機能を持っており、これをOpenTelemetryの計装(instrumentation)ライブラリ経由でPrometheusやGrafanaなどの監視ツールに連携できる。
- **属性(attribute)とは**
  メトリクスに付与されるタグ(ラベル)のこと。例えば`server.port=443`のように、「どのポートに対する通信のメトリクスか」を識別するための追加情報。OpenTelemetryの仕様では、属性ごとに「必須(Required)」「条件付き必須(Conditionally Required)」といった付与レベルが定義されている。

【説明】
.NET8で`HttpClient`メトリクスが導入された当初、`server.port`属性は「条件付き必須」として扱われており、通信先ポートが対応するプロトコルの既定ポート(HTTPなら80、HTTPSなら443)と一致しない場合にだけ付与されていた。その後、OpenTelemetryの仕様側で`server.port`の必須レベルが「必須(Required)」に変更されたため、.NET9では`http.client.request.duration`・`http.client.connection.duration`・`http.client.open_connections`という3つの計装すべてで、既定ポートであっても常に`server.port`属性が付与されるように変更された。目的はOpenTelemetry標準への準拠と、計装間の一貫性確保。

【放置したときの影響】
HttpClientメトリクスをPrometheus等で監視し、既存のクエリ・ダッシュボードで「ポート番号によるグルーピングやフィルタ」を行っていない場合は、`server.port`というタグが増えるだけで実害は少ない。ただし、これまで1本の系列(time series)として集計されていたメトリクスが、ポートごとに複数の系列に分かれてしまい、集計結果(合計値やパーセンタイル計算など)が変わったりダッシュボードのグラフが崩れたりする可能性がある。HttpClientメトリクス自体を使っていなければ全く影響しない。

【プロジェクトでの調べ方】
`AddHttpClientInstrumentation`のようなOpenTelemetryのセットアップコード、`http.client.request.duration`等のメトリクス名の利用、Prometheus/Grafanaの設定ファイルの有無を確認する。

dicom-tool-3では現状、OpenTelemetryやPrometheus関連の計装コード・設定ファイルは見当たらなかった。`AddHttpClient`自体はDicomTool.Worker(`Program.cs`)で1箇所使用されているが、メトリクス収集の仕組みまでは導入されていない。したがって現時点では影響しない。将来的に監視基盤(OpenTelemetry + Prometheus等)を導入する際に留意する必要がある。

【改修方法】
通常は改修不要。監視クエリ側で`server.port`タグの存在を考慮するようにクエリを修正する(例: ポートを問わず集計したい場合は`sum by (server.address)`のように集計対象からポートを外す、あるいは明示的にポートも含めて集計し直す)。

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

### HttpClientFactory のログはデフォルトでヘッダー値を編集します
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/networking/9.0/redact-headers

【前提知識】
- **`IHttpClientFactory`とログの関係**
  `AddHttpClient`で登録した`HttpClient`には、HTTPリクエスト/レスポンスの詳細を`Trace`レベルのログに自動的に出力する機能が組み込まれている。通常、アプリのログレベルは`Information`以上に設定されていることが多く、`Trace`レベルのログは明示的に有効化しない限り出力されない。
- **編集(redact)とは**
  ログに書き出す際、センシティブな値を`*`などの記号に置き換えて隠すこと。`Authorization`ヘッダー(認証トークン)のような値がログにそのまま残ってしまうと、ログを閲覧できる人やログ漏洩時に認証情報が漏れてしまう危険がある。

【説明】
以前は、`RedactLoggedHeaders`メソッドで「マスクすべきヘッダー」を明示的に指定しない限り、すべてのヘッダーの値がそのままログに出力されていた。つまり「危険なものだけを指定して隠す」ブロックリスト方式であり、指定を忘れると機密情報がそのまま流出するリスクがあった。

.NET9からはこれが逆転し、`RedactLoggedHeaders`を呼ばない限り、すべてのヘッダー値が既定でマスクされるようになった。つまり「安全と分かっているものだけを指定して見せる」許可リスト方式に変わった。理由は「ログ出力を既定で安全にするため」。

【放置したときの影響】
「非推奨になる」のではなく「ログの中身が変わる」動作変更で、セキュリティ的にはむしろ安全になる方向。ただし、これまでヘッダーの中身をTraceログで見ながらトラブルシューティングをしていた場合、.NET9に上げると突然すべてのヘッダー値が`*`に置き換わり、調査に必要な情報が見えなくなったように感じる点に注意。なお、TraceレベルのログはデフォルトのMinimumレベル(通常Information以上)では出力されない設定になっていることが多く、本番運用でこの変更が顕在化するケースは限定的。

【プロジェクトでの調べ方】
`AddHttpClient`の呼び出し箇所と、`appsettings.json`等のログレベル設定(`Logging:LogLevel`)で`System.Net.Http.HttpClient`カテゴリを`Trace`レベルに明示的に設定していないかを確認する。

dicom-tool-3では、DicomTool.Workerの`Program.cs`で`AddHttpClient`が1箇所使用されている(StorageGuardサービスへの問い合わせ用)。関連するappsettings.json等のログ設定を確認したが、`System.Net.Http.HttpClient`カテゴリを個別にTraceレベルへ引き上げる設定は見当たらず、既定のログレベルのままであれば、この変更が実運用上目に見える影響を及ぼすことはないと考えられる。

【改修方法】
通常は改修不要。トラブルシューティング等でヘッダーの値をあえてログに出したい場合は、`RedactLoggedHeaders`で許可するヘッダー名(または条件)を明示する。

```csharp
// 例: 特定のヘッダーだけマスクを解除する
services.AddHttpClient("storage-guard")
    .RedactLoggedHeaders(h => h is "X-Trace-Id"); // このヘッダーだけ値をログに出す

// (非推奨・注意)全ヘッダーのマスクを無効化する場合
services.AddHttpClient("storage-guard")
    .RedactLoggedHeaders(_ => false);
```

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

### HttpClientFactory はプライマリ ハンドラーとして SocketsHttpHandler を使用
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/networking/9.0/default-handler

【前提知識】
- **HttpMessageHandlerとプライマリハンドラーとは**
  `HttpClient`が実際にネットワーク通信を行う部分を「ハンドラー(`HttpMessageHandler`)」と呼ぶ。歴史的には`HttpClientHandler`という実装が使われてきたが、.NET Core以降、内部的にはより低レベルで高性能な`SocketsHttpHandler`に置き換わっており、`HttpClientHandler`は実際には`SocketsHttpHandler`をラップする形になっていた。
  `IHttpClientFactory`(`AddHttpClient`)経由で作る`HttpClient`は、複数のハンドラーを積み重ねた「パイプライン」として構成され、一番奥にあって実際に通信を行うハンドラーのことを「プライマリハンドラー」と呼ぶ。
- **DIコンテナのシングルトンとHttpClientの寿命の話**
  DI(依存性注入)コンテナに登録されたシングルトンサービスの中に、名前付き/型付きの`HttpClient`を保持し続けてしまうと、内部の接続がDNSの変更(サーバーのIPアドレス変更など)に追従できなくなる、という有名な落とし穴がある。`SocketsHttpHandler`には接続をどれくらいの期間使い回すかを制御する`PooledConnectionLifetime`というプロパティがあり、これを設定すると定期的に接続が再作成されるようになる。

【説明】
以前、`AddHttpClient`で明示的に指定しない場合のプライマリハンドラーは常に`HttpClientHandler`型のインスタンスだった。そのため、`ConfigurePrimaryHttpMessageHandler`のコールバック内で`(HttpClientHandler)h`のようにキャストし、`UseCookies`や`ClientCertificates`などのプロパティを設定するコードが広く書かれていた。

.NET9からは、対応するプラットフォームでは既定のプライマリハンドラーが`SocketsHttpHandler`型のインスタンスに変わり、その`PooledConnectionLifetime`が`HandlerLifetime`(既定30分)と同じ値に自動設定されるようになった。この結果、従来通り`HttpClientHandler`へキャストしていたコードは、実行時に`InvalidCastException`で失敗するようになった。

この変更が行われた理由は、`IHttpClientFactory`利用時によくある問題(名前付き/型付きクライアントがシングルトンサービスに誤ってキャプチャされ、想定より長期間使い回されてしまい、DNSの変更に追従できなくなる)を、既定の状態でも軽減するため。

【放置したときの影響】
「動作が変わって動かなくなる可能性が大きい」タイプの変更。`ConfigurePrimaryHttpMessageHandler`内で`HttpClientHandler`への直接キャストをしているコードがあると、実行時に`InvalidCastException`が発生してアプリの該当処理が確実に落ちる。

コード例:
```csharp
// .NET 8までは動いていたが、.NET 9では InvalidCastException が発生しうる
services.AddHttpClient("test")
    .ConfigurePrimaryHttpMessageHandler((h, _) =>
    {
        ((HttpClientHandler)h).UseCookies = false; // ここで例外
    });
```

【プロジェクトでの調べ方】
`ConfigurePrimaryHttpMessageHandler`、`(HttpClientHandler)`というキャスト表現でリポジトリ全体をgrepする。

dicom-tool-3では`AddHttpClient`はDicomTool.Workerの`Program.cs`の1箇所のみで使われており、そのコードは`client.BaseAddress = new Uri(storageGuardBaseUrl)`という設定のみで、`ConfigurePrimaryHttpMessageHandler`や`HttpClientHandler`へのキャストは一切使われていない。したがって、この変更の影響は受けない。

【改修方法】
`HttpClientHandler`と`SocketsHttpHandler`の両方を型チェックして分岐する、あるいはプライマリハンドラーを明示的に指定する。

```csharp
services.AddHttpClient("test")
    .ConfigurePrimaryHttpMessageHandler((h, _) =>
    {
        if (h is HttpClientHandler hch) hch.UseCookies = false;
        if (h is SocketsHttpHandler shh) shh.UseCookies = false;
    });
```

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

### HttpListenerRequest.UserAgent が null 許容
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/networking/9.0/useragent-nullable

【前提知識】
- **Null許容参照型(nullable reference types)とは**
  C#8以降の機能で、`string`のように「参照型」の変数やプロパティに`?`を付けるか(`string?`)付けないか(`string`)で、「その値がnullになりうるか」をコンパイラに伝える仕組み。プロジェクトで`<Nullable>enable</Nullable>`を有効にしていると、nullチェックを忘れているコードに対してコンパイラが警告を出してくれる。
  ただし、この注釈(アノテーション)自体はライブラリの作者が実際の挙動に合わせて正しく付ける必要があり、間違っていると「実際はnullになりうるのに、型情報上はnullにならないことになっている」というズレが生じる。
- **HttpListenerとは**
  ASP.NET Core以前から.NETに存在する、素のHTTPサーバーを自前で立てるための低レベルAPI。Kestrel(ASP.NET Coreの標準サーバー)とは別物。

【説明】
`HttpListenerRequest.UserAgent`(リクエストの`User-Agent`ヘッダーの値)は、実際にはクライアントがそのヘッダーを送ってこなければnullになりうるプロパティであるにもかかわらず、これまで「null許容ではない(non-nullable)」と誤って注釈されていた。.NET9で、この注釈が実態に合わせて「null許容(nullable、`string?`)」に修正された。

【放置したときの影響】
これは「実行時の挙動が変わる」変更ではなく、「型注釈(コンパイル時の情報)が実態に合わせて正しくなる」変更。そのため、Null許容参照型を有効にしているプロジェクトで、`UserAgent`をnullチェックなしにそのまま使っている(例:`request.UserAgent.Contains(...)`のように直接メソッドを呼んでいる)コードがあると、コンパイル時に新しい警告(CS8602など)が出るようになる。

これは「非推奨になっただけで影響は小さい」というより、「元々nullになりうる箇所だったのに気づいていなかった、実行時に`NullReferenceException`が起きるリスクを抱えていたコードが、警告という形で可視化された」と捉えるべき変更。警告が出ても即座にビルドが失敗するわけではないが、`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`の設定があるとビルド失敗につながる。

【プロジェクトでの調べ方】
`HttpListener`および`.UserAgent`という文字列でリポジトリ全体をgrepする。dicom-tool-3では`HttpListener`自体を使っている箇所は見当たらず(ASP.NET Core/Kestrelベースの`DicomTool.Api`であり、素の`HttpListener`は使用していない)、この変更の影響を受けない。

【改修方法】
該当なし。もし使っていた場合は、`request.UserAgent`を使う前に`is not null`等でnullチェックを追加する。

```csharp
// before
string ua = request.UserAgent.ToUpper();

// after
string ua = (request.UserAgent ?? string.Empty).ToUpper();
```

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

### HttpClient EventSource イベントでの URI クエリの変更
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/networking/9.0/query-redaction-events

【前提知識】
- **EventSourceとは**
  .NETの低レベルなイベント発行/収集の仕組みで、Windowsのイベントトレーシング基盤(ETW)や`dotnet-trace`のような診断ツールから、ランタイム内部の詳細なイベントを覗くために使われる。`HttpClient`/`SocketsHttpHandler`は通信の開始やリダイレクトなどのタイミングで、この仕組み経由のイベント(`EventSource`名:`System.Net.Http`)を発行している。
- **クエリ文字列(query string)とは**
  URLの`?`以降の部分(`?key=value&...`)。検索キーワード、ページングトークン、APIキーなど、機密性のある情報が含まれることがある。

【説明】
以前は、`HttpClient`/`SocketsHttpHandler`が発行する`EventSource`イベント(例:通信開始を表す`RequestStart`イベントの`pathAndQuery`パラメータ、リダイレクトを表す`Redirect`イベントの`redirectUri`パラメータ)に、クエリ文字列やユーザー情報、URLフラグメント部分がそのまま含まれていた。

.NET9からは、これらの部分が`*`という文字に置き換えられる(マスクされる)ようになった。目的はプライバシー強化で、機密情報が診断用のトレースログに意図せず残ってしまうことを防ぐため。

【放置したときの影響】
「非推奨になった」変更ではなく挙動変更だが、この`EventSource`を能動的に購読・分析していない通常のプロジェクトにはまず影響しない。ETWトレースや`dotnet-trace`を使ってHTTP通信を詳細に調査するようなデバッグ作業を行っている場合、これまでトレースから見えていたURLのクエリ部分が見えなくなり、調査がしづらくなる可能性がある。

【プロジェクトでの調べ方】
`EventListener`、`EventSource`という文字列、および`dotnet-trace`関連の設定・ドキュメントの有無をリポジトリ内で確認する。dicom-tool-3では`EventListener`/`EventSource`を独自に使っているコードは見当たらなかった(フロントエンドVueコンポーネントに`DiagnosticListener`という無関係な語がヒットしたのみで、.NET側の該当コードはなし)。したがって現時点では影響しない。

【改修方法】
通常は改修不要。マスクを無効化して元の情報(クエリ文字列を含むURL)を見たい場合は、以下いずれかの方法でAppContextスイッチを設定する。

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="System.Net.Http.DisableUriRedaction" Value="true" />
</ItemGroup>
```

または環境変数`DOTNET_SYSTEM_NET_HTTP_DISABLEURIREDACTION`を`true`に設定する。なお、このスイッチは次の項目(IHttpClientFactoryログでのマスキング)も同時に無効化する。

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

### IHttpClientFactory ログにおける URI クエリの秘匿
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/networking/9.0/query-redaction-logs

【前提知識】
前項(HttpClient EventSourceイベントでのURIクエリの変更)と対になる変更。こちらは`EventSource`(低レベルな診断用トレース)ではなく、`ILogger`(`Microsoft.Extensions.Logging`、いわゆる普通のアプリケーションログ。ASP.NET Coreの標準的なロギングの仕組み)経由で出力される、`IHttpClientFactory`の既定のログの話。

【説明】
以前は、`IHttpClientFactory`の既定のログ実装が、`ILogger`に渡すログメッセージの中にリクエスト先URLのクエリ文字列をそのまま含めていた。.NET9からは、クエリ部分が`*`に置き換えられ、ユーザー情報・フラグメント部分も削除されるようになった。理由・仕組みは前項(EventSourceイベント)と共通(機密情報を含みやすいクエリ文字列を既定でログから除外することで、プライバシーを強化する)で、同じAppContextスイッチ(`System.Net.Http.DisableUriRedaction`)で両方まとめて無効化できる。

【放置したときの影響】
`IHttpClientFactory`のログをInformationレベル以上で出力しており(既定のログ設定でも、リクエストURLを含むログはこのレベルで出ることがある)、かつログ基盤(Application Insights、CloudWatch、ELK Stackなど)でクエリ文字列の値(検索キーワードやページングトークンなど)を見て運用・調査をしている場合、それらが`*`でマスクされて見えなくなる。逆に言えば、「機密情報が意図せずログに残っていた」状態が.NET9で自動的に改善される、とも言える変更で、「動作が変わって困る」というよりは「セキュリティが強化されて、これまで見えていた情報の一部が見えなくなる」という性質のもの。

コード例(ログ出力イメージ):
```
# .NET 8まで
GET https://api.example.com/search?query=secret-token

# .NET 9以降
GET https://api.example.com/search?*
```

【プロジェクトでの調べ方】
`AddHttpClient`の利用箇所と、ログ設定(`appsettings.json`の`Logging:LogLevel:System.Net.Http.HttpClient`)を確認する。dicom-tool-3のDicomTool.Workerにおける`AddHttpClient`呼び出し(StorageGuardサービスへの問い合わせ用)に関して、appsettings.json等のログ設定を確認したが、`System.Net.Http.HttpClient`カテゴリを個別に引き上げる設定は見当たらず、既定のログレベルのままであれば実運用上目立った影響はないと考えられる。

【改修方法】
通常は改修不要。クエリ文字列を意図的にログへ残したい特別な理由がある場合(機密情報が含まれないと確信できる場合)のみ、前項と同じAppContextスイッチで無効化する。

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="System.Net.Http.DisableUriRedaction" Value="true" />
</ItemGroup>
```

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

## シリアル化

### BinaryFormatter により常にスローされる
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/serialization/9.0/binaryformatter-removal

【前提知識】
- **シリアライズ/デシリアライズとは**
  「シリアライズ」とは、メモリ上のオブジェクトを、ファイル保存やネットワーク送信が可能なバイト列(またはテキスト)に変換すること。「デシリアライズ」はその逆で、保存されたバイト列から元のオブジェクトを復元すること。
- **BinaryFormatterとは**
  .NETオブジェクトをそのままバイナリ形式でシリアライズ/デシリアライズする古いAPI。「任意の型のオブジェクトを、型情報ごと保存・復元できてしまう」という特性上、悪意のあるバイト列を読み込ませることで、攻撃者が意図しないコードを実行させてしまう、いわゆる「デシリアライズ脆弱性」の温床として長年問題視されてきた。

【説明】
.NET5以降、`BinaryFormatter`の使用にはすでに警告(SYSLIB0011)が出るようになっており、Microsoftは段階的な廃止計画を進めていた。.NET9はその最終段階にあたり、標準搭載(in box)の`BinaryFormatter`実装は、どのような設定をしていても常に例外をスローするようになった。以前存在した「特別な設定をすることで使用を継続できる」というオプトイン設定も削除された。理由は単純に、セキュリティリスクが高すぎるため。

【放置したときの影響】
「動作が変わって動かなくなる可能性が非常に大きい」変更。もし`BinaryFormatter`を使ってシリアライズ/デシリアライズしている既存コード(古い形式のキャッシュファイル、セッション情報、レガシーなプロセス間通信フォーマットなど)があれば、.NET9では確実に例外(`PlatformNotSupportedException`など)が発生し、該当機能が完全に動かなくなる。

コード例:
```csharp
var formatter = new BinaryFormatter();
using var stream = File.OpenRead("data.bin");
var obj = formatter.Deserialize(stream); // .NET 9では必ず例外がスローされる
```

【プロジェクトでの調べ方】
`BinaryFormatter`という文字列でリポジトリ全体をgrepする。dicom-tool-3では該当箇所は見当たらなかった。DICOMファイル自体のパース処理はDICOM標準フォーマットのライブラリ(fo-dicom等)によるものであり、`BinaryFormatter`とは無関係。

【改修方法】
`System.Text.Json`や`MessagePack`など、より安全な代替シリアライザーに移行する。どうしても`BinaryFormatter`が必要な特殊な事情がある場合は、Microsoftの非サポートの別NuGetパッケージを追加することで引き続き使用できる手段もあるが、セキュリティリスクを十分理解した上で自己責任で行う必要がある。

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし。詳細は[BinaryFormatter移行ガイド](https://learn.microsoft.com/ja-jp/dotnet/standard/serialization/binaryformatter-migration-guide/)を参照)

### Null 許容 JsonDocument プロパティの JsonValueKind.Null への逆シリアル化
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/serialization/9.0/jsondocument-props

【前提知識】
- **System.Text.JsonとJsonDocumentとは**
  `System.Text.Json`は.NET標準のJSON処理ライブラリ。`JsonDocument`は、JSON文字列をパースして「木構造(DOM)」として保持するクラスで、`JsonElement`という単位でツリーの各要素をたどっていくことができる。
- **JsonValueKindとは**
  ある要素が「オブジェクト」「配列」「文字列」「数値」「真偽値」「null」のどれであるかを表すenum。
- **C#のnullとJSONのnullは別概念**
  C#の`null`は「変数が何のオブジェクトも参照していない状態」を指すが、JSONの`null`はあくまで「JSONの値としてnullという値が入っている状態」であり、パースした結果として得られる`JsonDocument`オブジェクト自体が(C#として)nullかどうかとは、本来別の話。

【説明】
JSON文字列全体がリテラルの`null`(つまり`"null"`という文字列そのもの)だった場合に、それを`JsonSerializer.Deserialize<JsonDocument>(...)`でデシリアライズすると、以前はC#の`null`参照がそのまま返ってきていた。.NET9からは、C#の`null`ではなく、「`JsonValueKind.Null`という種類(kind)を持つ、null**ではない**`JsonDocument`インスタンス」が返るようになった。

理由は、ネストされた(オブジェクトのプロパティなど、入れ子になった)JSON null値の扱いと、ルートレベルのJSON null値の扱いに不整合があったこと、また`JsonDocument.Parse`メソッドの挙動(こちらは以前からnullでない`JsonDocument`を返していた)との一貫性を取るため。以前の「C#のnullを返す」挙動はバグとみなされ、修正された。

【放置したときの影響】
「動作が変わって動かなくなる可能性がある」変更。`if (doc is null)`のようなnullチェックでルートレベルのJSON nullを判定しているコードがあると、.NET9では常に`false`になり、想定していた分岐に入らなくなる。一方で、この変更によって新たに`NullReferenceException`が発生するようになるわけではない(むしろオブジェクトが必ず非nullで返るようになるので、その面では安全寄りとも言える)が、業務ロジックとしては壊れる可能性がある。

コード例:
```csharp
var doc = JsonSerializer.Deserialize<JsonDocument>("null");
if (doc is null)
{
    // .NET 8まではここに入っていたが、.NET 9では入らなくなる
}
```

【プロジェクトでの調べ方】
`JsonSerializer.Deserialize<JsonDocument>`という形の呼び出し、および`JsonDocument`型に対するnullチェックのパターンをgrepで確認する。dicom-tool-3では`JsonDocument`/`JsonValueKind`を直接使っている箇所は見当たらなかった。GraphQL(HotChocolate等)やAPIのDTOへの型付きデシリアライズが中心と推測され、`JsonDocument`を経由した動的なJSON解析は行われていない。

【改修方法】
nullチェックを、`JsonValueKind.Null`も考慮したパターンマッチに置き換える。

```csharp
// before
if (doc is null) { ... }

// after
if (doc is null || doc.RootElement.ValueKind == JsonValueKind.Null) { ... }
```

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)

### System.Text.Json メタデータ リーダーが現在、メタデータ プロパティ名のエスケープを解除
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/serialization/9.0/json-metadata-reader

【前提知識】
- **参照の保持(preserve references)とポリモーフィズムとは**
  `System.Text.Json`には、循環参照を含むオブジェクトグラフ(あるオブジェクトが間接的に自分自身を参照しているような構造)を正しくシリアライズ/デシリアライズするための「参照の保持」機能があり、`$id`/`$ref`/`$values`のような`$`から始まる特殊なプロパティ名(メタデータプロパティ)をJSON内に埋め込んで、どの要素とどの要素が同じインスタンスかを管理する。
  また、ポリモーフィズム(多態性。基底クラスの変数に派生クラスのインスタンスを代入して扱う、オブジェクト指向の基本機能の1つ)のシリアライズでも、実際にはどの派生型だったかを判別するための特殊プロパティ名(型判別子)を使う。
- **Unicodeエスケープシーケンスとは**
  JSON文字列の中で、`$`のように書くと`$`という1文字を別の表記方法で表せる、という仕組み。人間が見た目上異なる文字列に見えても、パース結果としては`$id`と`$id`は全く同じ文字列(`$id`)になるべきもの。

【説明】
以前は、`System.Text.Json`のメタデータリーダーが、プロパティ名の中のUnicodeエスケープを正しく解釈(デコード)しないまま、メタデータプロパティかどうかを比較・判定していた。そのため、本来`$id`と全く同じ意味であるはずの`$id`のようなエスケープ表記のプロパティ名が、「メタデータプロパティではない、ただの普通のプロパティ」として素通りしてしまっていた。

これにより2つの問題があった。1つは、本来働くべき「無効なメタデータプロパティ名の検証」をエスケープによってすり抜けられてしまう可能性があったこと。もう1つは、逆に自前で定義したポリモーフィズムの型判別プロパティ名に、エスケープが必要な文字(例えば日本語などの非ASCII文字)が含まれていると、シリアライズ時とデシリアライズ時でプロパティ名の見た目が一致せず、型を正しく復元できない(ポリモーフィックデシリアライズに失敗する)という不具合があった。

.NET9では、メタデータプロパティ名を比較する前にきちんとエスケープを解除(デコード)してから判定するように修正された。

【放置したときの影響】
ほとんどの一般的なJSON処理(単純なDTOのシリアライズ/デシリアライズ)には影響しない。影響するのは、(a)`ReferenceHandler.Preserve`を使って参照保持シリアライズをしており、かつUnicodeエスケープを使って`$`から始まるメタデータプロパティ名を偽装するような特殊なJSONを読み込む可能性がある場合(この場合.NET9では正しく例外になる=セキュリティ上は望ましい方向の変更)、または(b)`[JsonPolymorphic]`属性の型判別プロパティ名に非ASCII文字などエスケープが必要な文字を使っている場合(こちらはむしろ.NET8まで正しく動いていなかったポリモーフィックデシリアライズが.NET9で直る、という話)。

エラーメッセージの例:
```
System.Text.Json.JsonException: Properties that start with '$' are not allowed in types that support metadata.
```

【プロジェクトでの調べ方】
`ReferenceHandler.Preserve`、`JsonPolymorphic`、`JsonDerivedType`という属性/APIの利用有無をgrepで確認する。dicom-tool-3ではこれらのキーワードはいずれもヒットせず、参照保持シリアライズやポリモーフィックシリアライズは使用されていない(通常のDTOをそのまま`System.Text.Json`でシリアライズ/デシリアライズする構成と推測される)。したがって現時点では影響しない。

【改修方法】
通常は改修不要。もし独自にエスケープを使ってメタデータプロパティ名の検証をすり抜けるようなコードを書いていた場合はそれをやめ、`$`から始まる名前(メタデータプロパティ)と衝突しない、通常のプロパティ名を選ぶようにする。

【参考記事】
- (公式ドキュメント以外に参考にした記事は特になし)
