# .NET 10 での破壊的変更（暗号 / Windows フォーム）

## 暗号

### CompositeMLDsa が draft-08 に更新されました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/cryptography/10.0/composite-mldsa-draft-08

【前提知識】

- **デジタル署名とは**
  「このデータは確かに本人が作った／改ざんされていない」ということを、暗号技術を使って証明する仕組み。代表的なものにRSAやECDSAがある。
- **耐量子暗号（PQC: Post-Quantum Cryptography）とは**
  将来「量子コンピューター」が実用化すると、現在広く使われているRSAやECDSAなどの暗号は理論上、量子コンピューターによって効率的に解読されてしまうことが分かっている。そこで、量子コンピューターでも解読が困難な新しい暗号アルゴリズムの研究・標準化が世界的に進められており、これを耐量子暗号（PQC）と呼ぶ。
- **ML-DSA とは**
  PQCの中でも、米国NIST（アメリカ国立標準技術研究所）が標準化した「デジタル署名」アルゴリズムの1つ。「ML」は格子（Lattice）ベースの数学的な問題の難しさを利用していることに由来する。
- **CompositeMLDsa とは**
  「今すぐ量子コンピューター対応の署名だけに全面移行するのはまだリスクが高い（新しい暗号なのでまだ弱点が見つかるかもしれない）」という懸念から、「昔からあるRSA/ECDSAなどの署名」と「新しいML-DSA署名」を1つの証明書・1つの署名の中に**両方同時に**組み込んでおく、という考え方（複合署名 / Composite Signature）。片方が万が一将来破られても、もう片方が守ってくれるという「保険を二重にかける」発想。.NETではこれを`System.Security.Cryptography.CompositeMLDsa`クラスで扱う。
- **draft-07 / draft-08 とは**
  この複合ML-DSAの仕様は、IETF（インターネット関連の技術標準を決める団体）でまだ「ドラフト（草案）」段階として議論が続いている最中のもの。ドラフトはバージョンが上がるたびに細かい仕様（バイト列の並び順など）が変わることがあり、draft-07とdraft-08は互換性がない（draft-07で作った署名やキーはdraft-08の実装では読めない）。

【説明】

.NET 10のプレビュー版・RC版では、`CompositeMLDsa`クラスが複合ML-DSA仕様の「draft-07」に従って署名の生成・検証、キーのエクスポート/インポートを行っていた。しかし.NET 10の正式リリース（GA）版では、この実装が仕様の最新版である「draft-08」に更新された。

これにより、draft-07形式で作られた署名やエクスポート済みのキーは、GA版の.NETでは正しく検証・インポートできなくなる（draft-08とdraft-09の間には互換性があるとのことなので、今後さらに仕様が進んでも当面はGA版がそのまま追随できる見込み）。

変更理由は単純で、「まだ確定していない仕様の、その時点での最新ドラフトに追随し続けるため」。`CompositeMLDsa`クラス自体が`[Experimental]`（実験的機能。将来仕様や挙動が変わりうる、本番利用非推奨という意味のマーカー）としてマークされている点からも分かる通り、そもそも「まだ枯れていない最先端機能」という位置づけの変更。

【放置したときの影響】

- 本番運用のデータ（実際の証明書や署名済みデータ）に`CompositeMLDsa`を使っていた場合、draft-07時代に生成した署名や秘密鍵/公開鍵は、GA版の.NET 10ではそのまま使えなくなる（検証エラーやインポートエラーになる）。
- ただし公式ドキュメントも「本番環境ではこのクラスに依存すべきではない」と明言しており、そもそも実験的機能を本番で使っていなければ影響はない。

【プロジェクトでの調べ方】

`CompositeMLDsa`という文字列でリポジトリ全体をGrep検索したが、`dicom-tool-3`のコードには1件もヒットしなかった。DICOM通信・REST APIまわりで独自にPQC署名を実装している箇所はなく、**この変更は現時点のdicom-tool-3には影響しない**。

【改修方法】

このプロジェクトでは対応不要。もし将来、実験的機能として`CompositeMLDsa`を試験導入していた場合は、以前に生成したキー・署名をすべて破棄し、GA版の.NET 10（draft-08準拠）で作り直す必要がある。

```csharp
// draft-08環境で作り直す（以前のキー・署名は再利用できない）
using var mldsa = CompositeMLDsa.GenerateKey(CompositeMLDsaAlgorithm.MLDsa44WithRSA2048Pkcs1Sha256);
byte[] newSignature = mldsa.SignData(data);
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### CoseSigner.Key は null にすることができます
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/cryptography/10.0/cosesigner-key-null

【前提知識】

- **COSE とは**
  「CBOR Object Signing and Encryption」の略。JSON版の署名フォーマットである「JWS/JOSE」のCBOR（バイナリ形式のJSONのようなもの）版、とイメージすると分かりやすい。IoT機器や.NETの一部の署名機能（例: SBOM署名など）で使われている、データに署名を付与するための標準フォーマット。
- **CoseSigner クラスとは**
  .NETで「このキーとこのアルゴリズムを使ってCOSE署名を作ってください」と指定するためのクラス。コンストラクターにキー（例えばRSAやECDSAの鍵オブジェクト）を渡して使う。
- **AsymmetricAlgorithm とは**
  RSAやECDSAなど、「公開鍵と秘密鍵のペアを使う暗号アルゴリズム」を表す.NETの共通の抽象基底クラス。長年、.NETで非対称鍵を扱うAPIは基本的に「このAsymmetricAlgorithmを継承したクラスを受け取る/返す」という設計になっていた。
- **`?`（Nullable参照型の`?`）とは**
  C#8以降の機能で、「この変数・プロパティはnullが入りうる」ということを型に明示する記法。C#初心者向けに言うと、`AsymmetricAlgorithm Key`は「Keyは絶対にnullではない」という約束だったが、`AsymmetricAlgorithm? Key`は「Keyはnullのこともある、呼び出し側でnullチェックしてね」という約束に変わったことを表す。

【説明】

以前の`CoseSigner.Key`プロパティは、型が`AsymmetricAlgorithm`（null非許容）であり、`null`を返すことは想定されていなかった。

.NET 10からは、`MLDsa`（前述のML-DSA署名）のような新しいPQC（耐量子暗号）アルゴリズムが`AsymmetricAlgorithm`クラスを継承しない設計で追加された。`CoseSigner`はこうした新しいキー型でも構築できるようになったが、その場合`CoseSigner.Key`は「AsymmetricAlgorithmとして返せるキー」を持っていないため、代わりに`null`を返すよう変更された。型も`AsymmetricAlgorithm?`に変わっている。従来通りRSA鍵やECDSA鍵で`CoseSigner`を構築した場合は、今まで通り`Key`にそのインスタンスが入る。

```csharp
using MLDsa mldsa = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa44);
CoseKey coseKey = new CoseKey(mldsa);
var signer = new CoseSigner(coseKey);
// signer.Key はここでは null になる
```

【放置したときの影響】

`CoseSigner.Key`にアクセスして、それを前提なく別のAPIに渡していたり、メンバーアクセス（`signer.Key.KeySize`など）をしていたりするコードがあると、`MLDsa`などの新しいアルゴリズムでCoseSignerを構築した場合に`NullReferenceException`が発生しうる。もっとも、RSA/ECDSAだけしか使っていない既存コードであれば、`Key`が実際に`null`になることはなく、実害は出ない（コンパイル時にNullable警告が出る可能性はある）。

【プロジェクトでの調べ方】

`CoseSigner`という文字列でリポジトリ全体をGrep検索したが、dicom-tool-3のコードには1件もヒットしなかった。COSE署名を扱っている箇所は存在せず、**この変更は現時点のdicom-tool-3には影響しない**。

【改修方法】

このプロジェクトでは対応不要。もし将来COSE署名（例えばSBOM署名や何らかのAPIレスポンスの完全性検証など）を導入する場合は、`Key`を使うコードで必ずnullチェックを入れる。

```csharp
// before（nullを想定していない）
AsymmetricAlgorithm key = signer.Key; // .NET 10ではコンパイルエラー(型不一致)になりうる
int size = key.KeySize;

// after（nullを許容して処理を分岐）
if (signer.Key is AsymmetricAlgorithm key)
{
    int size = key.KeySize;
}
else
{
    // MLDsaなど、AsymmetricAlgorithmで表現できないキーを使っている
}
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### MLDsa および SlhDsa 'SecretKey' メンバーの名前が変更されました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/cryptography/10.0/mldsa-slhdsa-secretkey-to-privatekey

【前提知識】

- **SlhDsa とは**
  ML-DSAと同じくNISTが標準化した耐量子暗号の署名アルゴリズムの1つ（「ステートレス・ハッシュベース署名」というハッシュ関数の安全性だけに依存する、別系統の設計方式）。.NETでは`System.Security.Cryptography.SlhDsa`クラスで扱う。
- **秘密鍵の呼び方「SecretKey」と「PrivateKey」の違いについて**
  暗号理論の仕様書（NISTの文書など）では、非対称鍵暗号の「秘密の方の鍵」を英語で「secret key」（略して`sk`）と表記する慣習がある。一方、.NETの既存API（RSA/ECDSAなど）では昔から「private key（PrivateKey）」という呼び方で統一されてきた。仕様書の言葉をそのままAPI名にすると、既存の.NET APIの命名規則と食い違ってしまう、という話。
- **`[Experimental]`属性とは**
  そのAPIがまだ実験段階であり、将来の.NETバージョンで名前や挙動が変わる可能性があることを示す印。コンパイラが警告を出し、明示的に使う意思表示（`#pragma warning disable`など）をしないと使えないようになっている。

【説明】

.NET 10で追加された`MLDsa`・`SlhDsa`クラス（いずれも`[Experimental]`扱いのPQCクラス）には当初、`ImportMLDsaSecretKey`・`ExportMLDsaSecretKey`・`SecretKeySizeInBytes`のように、仕様書の用語である「SecretKey」を使ったメンバー名が付いていた。

.NET 10の開発が進む中で、既存の.NET暗号APIの命名規則（RSA/ECDSAなどは一貫して「PrivateKey」を使う）に合わせるため、これらのメンバー名が`ImportMLDsaPrivateKey`・`ExportMLDsaPrivateKey`・`PrivateKeySizeInBytes`のように「SecretKey」から「PrivateKey」に一括で改名された。機能や挙動自体は変わらず、名前だけの変更。

【放置したときの影響】

該当クラスをまだ使っていなければ影響なし。使っていた場合は、旧名（`SecretKey`を含むメンバー）を呼び出している箇所がすべてコンパイルエラーになる（ソース互換性の破壊）。実行時エラーではなくビルドが通らなくなるタイプの変更なので、気づかずに放置するということ自体が起きにくい（ビルドが失敗するため）。

```csharp
// 旧名を使ったコード（.NET 10では存在しないメンバーとしてコンパイルエラー）
int size = key.Algorithm.SecretKeySizeInBytes;
key.ExportMLDsaSecretKey(buffer);
```

【プロジェクトでの調べ方】

`MLDsa`・`SlhDsa`・`SecretKey`という文字列でリポジトリ全体をGrep検索したが、dicom-tool-3のコードには1件もヒットしなかった。**この変更は現時点のdicom-tool-3には影響しない**（そもそも耐量子暗号の実験的APIを使っている箇所がない）。

【改修方法】

このプロジェクトでは対応不要。もし将来利用する場合は、単純な名前の読み替えで済む。

```diff
-int targetSize = key.Algorithm.SecretKeySizeInBytes;
+int targetSize = key.Algorithm.PrivateKeySizeInBytes;
 byte[] output = new byte[targetSize];
-key.ExportMLDsaSecretKey(output);
+key.ExportMLDsaPrivateKey(output);
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### OpenSSL 暗号化プリミティブは macOS ではサポートされていません
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/cryptography/10.0/openssl-macos-unsupported

【前提知識】

- **OpenSSL とは**
  暗号化・復号・署名・証明書処理など、暗号関連の処理をまとめて提供する、世界で最も広く使われているオープンソースのCライブラリ。多くのLinuxディストリビューションにはOS標準で搭載されている。
- **プラットフォームごとの暗号化の実装の違い**
  .NET(Core)は、OSごとに「そのOS上で標準的・ネイティブな暗号化APIの実装」に処理を委譲する設計になっている。具体的には、Windowsでは`CNG`（Windows標準の暗号化API）、Linux/UnixではOpenSSL、macOSではAppleが提供する`Apple CryptoKit`/`Security.framework`（Appleの暗号化API）を裏側で呼び出す。同じ`RSA.Create()`のようなコードを書いても、動いているOSによって内部の実装が切り替わる、という理解が重要。
- **`RSAOpenSsl`のような「OpenSsl」接尾辞クラスとは**
  .NETには、OSに依存する標準ファクトリ（`RSA.Create()`など）とは別に、「OpenSSLの実装を明示的に使う」という意図が名前から分かるクラス（`RSAOpenSsl`、`ECDsaOpenSsl`など）も歴史的に用意されている。通常はこれらを直接使う必要はなく、`RSA.Create()`のような抽象的なファクトリメソッドを使うのが推奨されるパターン。
- **AesCcm とは**
  AES暗号を使った認証付き暗号化方式の1つ（CCMモード）。よく使われる`AesGcm`（GCMモード）と似た用途だが、モードが異なる。

【説明】

以前は、macOS上でも「OpenSSLが（Homebrewなどで）インストールされていれば」、`RSAOpenSsl`や`ECDsaOpenSsl`のようなOpenSSL専用クラス、および`AesCcm`が動作していた。

.NET 10からは、これらのOpenSSL依存クラスはmacOS上では一切サポートされなくなり、使おうとすると`PlatformNotSupportedException`（そのプラットフォームではサポートされていない機能を使おうとしたことを示す例外）が必ずスローされるようになった。

変更理由として、macOSの暗号化はすでに.NET Core 2.0の頃からApple標準の暗号化ライブラリに移行しており、OpenSSLへの依存は「昔の名残」として残っていただけだった。さらに近年のmacOSでは、Appleがセキュリティ強化のためにシステム外のライブラリ（Homebrewで入れたOpenSSLなど）を特定パスから読み込むことを難しくしており、動作の安定性・配布のしやすさの両面でOpenSSL依存を維持するコストが増していた、という背景がある。

【放置したときの影響】

Windows/Linux上でしか動かさないアプリであれば影響はない。macOS上で`new RSAOpenSsl(...)`のようにOpenSSL専用クラスを直接インスタンス化しているコードや、`AesCcm`を使っているコードがあると、.NET 10 + macOSの組み合わせでは必ず例外で落ちるようになる。

```csharp
// macOS + .NET 10ではPlatformNotSupportedExceptionが発生する
var rsa = new RSAOpenSsl();
```

【プロジェクトでの調べ方】

`RSAOpenSsl`・`ECDsaOpenSsl`・`ECDiffieHellmanOpenSsl`・`DSAOpenSsl`・`AesCcm`という文字列でリポジトリ全体をGrep検索したが、dicom-tool-3のコードには1件もヒットしなかった。また、そもそもこのプロジェクトの各C#サービス（`DicomTool.Api`、`DicomTool.Worker`、`DicomTool.DicomScp`、`DicomTool.TrayApp`）は主にWindows環境（ホストPCおよびWindows系VM）で動かす前提で作られており（`DicomTool.TrayApp`は`net10.0-windows`）、macOS向けにビルド・実行する運用は現状想定されていない。**この変更は現時点のdicom-tool-3には影響しない**。

【改修方法】

このプロジェクトでは対応不要。もし将来macOS対応やクロスプラットフォーム実行を検討し、かつOpenSSL専用クラスを直接使っていた場合は、OS非依存のファクトリメソッドに置き換える。

```csharp
// before（OpenSSL専用クラスを直接使用。macOSでは.NET 10から動かない）
using var rsa = new RSAOpenSsl();

// after（OSごとに適切な実装へ自動的に振り分けられるファクトリメソッドを使う）
using var rsa = RSA.Create();
```

`AesCcm`はmacOSに同等の代替がないため、`AesGcm`など別の暗号化アルゴリズムへの置き換えを検討する。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### Unix で必要な OpenSSL 1.1.1 以降
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/cryptography/10.0/openssl-version-requirement

【前提知識】

- **Unix系OS（Linuxなど）と.NETの暗号化の関係**
  前項の通り、LinuxなどのUnix系OSでは、.NETの暗号化処理は基本的にOS側にインストールされているOpenSSLライブラリを呼び出すことで実現されている（macOSは対象外。macOSはApple標準の暗号化APIを使うため、この変更の影響を受けない）。
- **OpenSSLのバージョン系列について**
  OpenSSLには`1.0.2`、`1.1.0`、`1.1.1`、`3.0`系…といった複数の世代がある。バージョンが古いものほど、開発元によるセキュリティサポート（脆弱性が見つかったときの修正）がすでに終了していることが多い。多くのメジャーなLinuxディストリビューション（Ubuntuの比較的新しいLTS版など）は、すでにOpenSSL 1.1.1以降を標準搭載している。

【説明】

以前の.NETは、Linux/Unix環境において`1.0.2`や`1.1.0`のような、`1.1.1`より古いバージョンのOpenSSLでも動作していた。

.NET 10からは、Unix系OS上で.NETアプリを起動するために、OpenSSL 1.1.1以降が必須になった。もし実行環境に1.1.1より前のOpenSSLしか入っていない（かつ1.1.1以降が別途インストールされていない）場合、.NETアプリ自体が**起動できなくなる**（実行時のどこかで例外、ではなく、そもそもプロセスが立ち上がらない）。

変更理由は、`1.1.1`より古いOpenSSLはすでに開発元のサポートが終了しており、最近の主要なLinux/Unixディストリビューションでも標準搭載されなくなっているため。.NET側が古いバージョンへの対応を続けることは保守コストの増加につながるだけで実益が薄い、という判断による。

【放置したときの影響】

Windows上で開発・実行している分には無関係。もし将来、このリポジトリのサービス（`DicomTool.Api`や`DicomTool.Worker`など）を非常に古いLinuxディストリビューション（例えば長期間パッチを当てていないCentOS 7系など）にデプロイしようとすると、.NET 10ランタイムがそもそも起動できず、原因が分かりにくい形でサービスが動かない、という事態になりうる。

【プロジェクトでの調べ方】

このリポジトリ直下および`services/`配下を`Dockerfile`という名前でGlob検索したが、C#各サービス向けのDockerfileは存在しなかった（存在する`docker-compose.yml`はPostgreSQLとTemporalサーバーのみを起動するためのもので、いずれも.NETとは無関係の既製イメージを使っている）。各C#サービス（`DicomTool.Api`、`DicomTool.Worker`、`DicomTool.DicomScp`、`DicomTool.TrayApp`）は`dotnet run`でホストPC上に直接起動する運用であり、現状Linux上でこれらのサービスを動かす構成にはなっていない。**この変更は現時点のdicom-tool-3には影響しない**。ただし、CLAUDE.mdに登場する`dicom-pacs-vm`（DICOM通信テスト相手のVM）が仮にLinuxベースであり、将来そちらにこのリポジトリのC#サービスをデプロイする場合は、VM側のOpenSSLバージョンを確認する必要がある。

【改修方法】

このプロジェクトでは対応不要。もし将来Linux環境にデプロイする場合は、対象ディストリビューションが`openssl version`コマンドなどで1.1.1以降であることを事前に確認する。

```bash
# デプロイ先のLinuxでOpenSSLバージョンを確認する例
openssl version
# OpenSSL 1.1.1... のように 1.1.1 系以降であればOK
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### X500DistinguishedName の検証がより厳密に
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/cryptography/10.0/x500distinguishedname-validation

【前提知識】

- **X.509証明書と識別名（Distinguished Name）とは**
  HTTPSなどで使われるデジタル証明書（X.509証明書）には、「誰の証明書か」を表す情報として「識別名（Distinguished Name、略してDN）」という項目がある。`CN=example.com, O=Example Corp, C=JP`のような、複数の属性（Common Name、Organizationなど）を組み合わせた文字列がその実体。.NETではこれを`X500DistinguishedName`クラスで表す。
- **ASN.1エンコーディングとPrintableString/UTF8Stringとは**
  証明書の中身は「ASN.1」という抽象的なデータ形式のルールに従ってバイナリにエンコードされる。DNの各属性（例えば電話番号を表す`id-at-telephoneNumber`）ごとに、「この属性はこの文字種でエンコードしなければならない」という仕様上の制約がある。例えば電話番号は`PrintableString`（英数字とごく一部の記号のみ許可する文字列型）でエンコードするよう定められており、`!`のような記号は本来使えない。
- **X500DistinguishedNameFlags.ForceUTF8Encoding とは**
  DNをエンコードする際に、通常の属性ごとのルールを無視して「強制的にUTF8String形式でエンコードする」という指定をするためのオプション。

【説明】

以前のWindows以外の環境（Linux/macOSなど）では、.NETの`X500DistinguishedName`コンストラクター（文字列からDNを組み立てるもの）が、本来のエンコード規則に違反する入力（例: 電話番号に感嘆符`!`を含めるなど）でも例外を出さずに受け入れてしまっていた。一方Windows上では、同じ入力に対してすでに例外が発生していた、という「OSによって挙動が違う」不整合な状態だった。また`ForceUTF8Encoding`フラグも、本来UTF8Stringとして許可されない場面でまで強制適用されてしまっていた。

.NET 10からは、Windows以外の環境でも、エンコード規則に違反する入力に対してWindowsと同様に`CryptographicException`（暗号処理関連のエラーを表す例外）がスローされるようになった。`ForceUTF8Encoding`フラグも、「UTF-8でのエンコードが許容される場合のみ」適用されるよう仕様通りに修正された。

変更理由は、OS間の挙動の不整合を解消し、仕様（X.520のエンコード規則）とWindowsの実際の挙動に合わせるため。

```csharp
// Windowsではもともと例外、Windows以外では以前は素通りしていた入力
new X500DistinguishedName("Phone=!!");
// .NET 10からはWindows以外でもCryptographicExceptionがスローされる
```

【放置したときの影響】

証明書のDNを、ユーザー入力や外部データから動的に組み立てて`X500DistinguishedName`コンストラクターに渡しているコードがある場合、以前は（Windows以外の環境で）通っていた不正な文字列が、.NET 10からは`CryptographicException`で例外になる可能性がある。特にLinuxコンテナー上でCI/CDや証明書発行処理を動かしている場合、突然そのステップが失敗するようになる、という形で表面化しうる。固定の正しい文字列しか使っていない場合は影響を受けない。

【プロジェクトでの調べ方】

`X500DistinguishedName`という文字列でリポジトリ全体をGrep検索したが、dicom-tool-3のコードには1件もヒットしなかった。証明書のDNを自前で組み立てている箇所は現状存在せず、**この変更は現時点のdicom-tool-3には影響しない**（ASP.NET CoreのHTTPS開発証明書（dotnet dev-certs）などは.NET SDK内部の処理であり、このリポジトリのアプリコードが直接DN文字列を組み立てているわけではない）。

【改修方法】

このプロジェクトでは対応不要。もし将来、証明書発行などでDNを動的に組み立てる場合は、文字列連結ではなく`X500DistinguishedNameBuilder`を使い、属性ごとに正しいASN.1型を明示する方が安全。

```csharp
// before（文字列を直接組み立てる。不正なエンコードに気づきにくい）
var dn = new X500DistinguishedName("Phone=000-555-1234", X500DistinguishedNameFlags.ForceUTF8Encoding);

// after（属性ごとにUniversalTagNumberを明示してビルドする）
using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;

var builder = new X500DistinguishedNameBuilder();
builder.Add("2.5.4.20", "000-555-1234", UniversalTagNumber.UTF8String);
X500DistinguishedName dn = builder.Build();
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### X509Certificate および PublicKey のキー パラメーターが null になることがある
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/cryptography/10.0/x509-publickey-null

【前提知識】

- **サブジェクト公開キー情報（Subject Public Key Info）とは**
  X.509証明書の中に含まれる、「この証明書の持ち主の公開鍵」に関する情報のかたまり。「どの暗号アルゴリズムの鍵か」「そのアルゴリズム固有の追加パラメーター」「実際の鍵の値」などから構成される。.NETでは`X509Certificate`クラスや`PublicKey`クラスのプロパティ・メソッド経由でこの情報にアクセスできる。
- **「アルゴリズムパラメーター」とは**
  例えばRSAの鍵にはアルゴリズムパラメーターが存在しない（鍵の値だけで完結する）が、DSAやECDSAなど一部のアルゴリズムでは、鍵本体とは別に追加のパラメーター情報が付随することがある。この追加パラメーターが「存在しない」ケースがあり、それをどう表現するかという話。
- **`byte[]`の空配列とnullの違い**
  ASN.1（証明書のバイナリ形式のルール）の世界では、「値が存在しない」ことと「長さ0の値が存在する」ことは意味が異なる。空の`byte[]`（要素数0の配列）を返すと、「長さ0のデータが実際に存在する」ように見えてしまい、そのままエンコードしようとすると仕様違反（無効なASN.1）になる、という問題があった。

【説明】

以前は、アルゴリズムパラメーターを持たない鍵に対して`certificate.GetKeyAlgorithmParameters()`のようなメソッドを呼び出すと、「空の`byte[]`配列」が返されていた。これは「パラメーターがない」ことを表現するつもりだったが、ASN.1的には正しい表現ではなく、これをそのまま再エンコードしようとすると例外が起きる、というバグの温床になっていた。

.NET 10からは、アルゴリズムパラメーターが存在しない場合、空配列ではなく`null`が返されるようになった。対応するメンバーの戻り値の型も、null許容(`byte[]?`)を明示するようアノテーションが更新されている。

```csharp
// .NET 9以前: パラメーターがなければ空配列
byte[] parameters = certificate.GetKeyAlgorithmParameters();

// .NET 10以降: パラメーターがなければnull
byte[]? parameters = certificate.GetKeyAlgorithmParameters();
```

変更理由は、「パラメーターが存在しない」という状態をより正確に表現するため。空配列は有効なASN.1表現ではなく誤解を招くため、`null`によって明示的に「ない」ことを表すよう改善された。

【放置したときの影響】

証明書のアルゴリズムパラメーターを取得して、`parameters.Length`のように配列であることを前提としたコードを書いていると、パラメーターがない鍵（多くのRSA証明書など）に対して.NET 10では`NullReferenceException`が発生する可能性がある。

```csharp
// このコードは.NET 10でパラメーターがない場合にNullReferenceExceptionになりうる
byte[] parameters = certificate.GetKeyAlgorithmParameters();
Console.WriteLine(parameters.Length);
```

【プロジェクトでの調べ方】

`X509Certificate`・`X509Store`・`GetKeyAlgorithmParameters`という文字列でリポジトリ全体をGrep検索したが、dicom-tool-3のコードには1件もヒットしなかった。証明書処理を自前で行っている箇所は存在せず（DICOM通信やASP.NET CoreのHTTPSまわりも、証明書の詳細プロパティに直接アクセスするコードは書かれていない）、**この変更は現時点のdicom-tool-3には影響しない**。

【改修方法】

このプロジェクトでは対応不要。もし将来、証明書のアルゴリズムパラメーターを扱うコードを書く場合は、nullチェックを入れる。

```csharp
// before
byte[] parameters = certificate.GetKeyAlgorithmParameters();
if (parameters.Length > 0) { /* ... */ }

// after
byte[]? parameters = certificate.GetKeyAlgorithmParameters();
if (parameters is { Length: > 0 })
{
    // アルゴリズムパラメーターが存在する場合の処理
}
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### 環境変数の名前が DOTNET_OPENSSL_VERSION_OVERRIDE に変更されました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/cryptography/10.0/version-override

【前提知識】

- **環境変数による.NETの構成スイッチとは**
  .NETランタイムには、OSの「環境変数」を使って実行時の挙動を切り替える仕組みが多数用意されている。例えば`DOTNET_ENVIRONMENT`（ASP.NET Coreの実行環境切り替え）などがよく知られている。多くは`DOTNET_`という接頭辞で統一されている。
- **この変更で扱う環境変数の役割**
  Linux上で.NETが使うOpenSSLのバージョンは通常、実行環境に入っているものが自動的に選ばれる。しかし複数バージョンのOpenSSLが共存する環境などで、「明示的にこのバージョンを優先的に使ってほしい」と.NETに指示したいケースがあり、そのための環境変数が用意されている。

【説明】

以前はこの環境変数の名前が`CLR_OPENSSL_VERSION_OVERRIDE`だった（`CLR`は.NETの実行エンジン=共通言語ランタイムの略称由来の、やや古い命名規則）。

.NET 10からは、この環境変数名が`DOTNET_OPENSSL_VERSION_OVERRIDE`に変更された。機能自体（優先して使わせたいOpenSSLのバージョンを指定する）は変わらない。

変更理由は、.NETの他の構成用環境変数がほぼすべて`DOTNET_`という接頭辞で統一されている中で、この変数だけ古い`CLR_`接頭辞のまま取り残されていたため、命名規則を揃えるために改名された。

【放置したときの影響】

CI/CDのビルドスクリプトやDockerfile、サーバーの起動スクリプトなどで`CLR_OPENSSL_VERSION_OVERRIDE`という環境変数名を明示的に設定していた場合、.NET 10ではその環境変数名が認識されなくなり、指定したつもりのOpenSSLバージョンが無視される（＝環境にインストールされているデフォルトのOpenSSLが選ばれる）。設定自体をそもそも使っていない場合は無関係。

【プロジェクトでの調べ方】

`CLR_OPENSSL_VERSION_OVERRIDE`・`DOTNET_OPENSSL_VERSION_OVERRIDE`という文字列でリポジトリ全体をGrep検索したが、dicom-tool-3のコード・設定ファイルには1件もヒットしなかった。**この変更は現時点のdicom-tool-3には影響しない**。

【改修方法】

このプロジェクトでは対応不要。もし将来この環境変数を利用する場合は、名前を読み替えるだけでよい。

```bash
# before
export CLR_OPENSSL_VERSION_OVERRIDE=3.0

# after
export DOTNET_OPENSSL_VERSION_OVERRIDE=3.0
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

## Windows フォーム

### API の廃止
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/10.0/obsolete-apis

【前提知識】

- **Obsolete（廃止予定）属性とは**
  C#には、あるクラスやメソッドに`[Obsolete]`という印を付けることで、「このAPIはもう推奨されていません、代わりにこちらを使ってください」とコンパイラに警告させる仕組みがある。実行はできるが、ビルド時に警告（場合によってはエラー）が出るようになる。
- **診断ID（Diagnostic ID）とは**
  コンパイラが出す警告・エラーには、それぞれ`CS0104`や`WFDEV004`のような固有の識別子（診断ID）が振られている。この診断IDを使うと、`.editorconfig`やプロジェクトファイルで「この警告だけをピンポイントで抑制する／エラー扱いにする」といった細かい制御ができる。WinForms用の警告は`WFDEV`という接頭辞で管理されている。
- **今回廃止対象になっている具体的なAPI**
  - `Form.OnClosing`/`Form.OnClosed`とその対応イベント：フォームが閉じる際に呼ばれる古いメソッド。より新しい`OnFormClosing`/`OnFormClosed`（`FormClosingEventArgs`/`FormClosedEventArgs`という、閉じる理由などの詳細情報を持つ引数を受け取れる）に統合されている。
  - `Clipboard.GetData(string)`：クリップボードから指定した形式のデータを取得する古いメソッド。取得失敗時に例外が起きたり型キャストで問題が起きたりしやすく、より安全な`TryGetData`が推奨されている。
  - `ContextMenu`、`DataGrid`、`MainMenu`、`Menu`、`StatusBar`、`ToolBar`：.NET Framework時代の古いコントロール群。現在ではそれぞれ`ContextMenuStrip`、`DataGridView`、`MenuStrip`、`StatusStrip`、`ToolStrip`という、より新しく高機能な後継コントロールに置き換わっている。旧コントロールは.NET Frameworkとのバイナリ互換性のためだけに残されている。

【説明】

以前は、上記のような古いWinForms APIを使ってもビルド警告は一切出なかった。.NET 10からは、これらのAPIを使用すると、`WFDEV004`（`OnClosing`/`OnClosed`系）、`WFDEV005`（`Clipboard.GetData`）、`WFDEV006`（`ContextMenu`等の旧コントロール群）というカスタム診断IDを伴うコンパイル時警告が出るようになった。診断IDが個別に振られたことで、「この警告だけ意図的に抑制する」といった細やかな制御が可能になっている。

【放置したときの影響】

放置しても即座に動かなくなるわけではない（あくまで警告であり、ビルド自体は通る）。ただし、これらのAPI自体は将来的なメンテナンス対象から外れていく可能性が高く、警告を無視し続けると、後継APIへの移行タイミングを逃したまま技術的負債が積み上がる。特に`ContextMenu`（`WFDEV006`）は、後述の「MenuItem/ContextMenu型のあいまいさ」問題とも関連するため、放置していると別の破壊的変更（コンパイルエラー）を誘発するケースもある。

【プロジェクトでの調べ方】

`OnClosing`・`OnClosed`・`Clipboard.GetData`・`ContextMenu`・`DataGrid`・`MainMenu`・`StatusBar`・`ToolBar`という文字列でリポジトリ全体をGrep検索した。ヒットしたのは`frontend/worklist/app/features/study/components/StudyTable.vue`（Vue.jsのフロントエンドで、C#/WinFormsとは無関係の別ファイル）のみで、WinFormsアプリである`services/DicomTool.TrayApp`のコードには1件もヒットしなかった。実際に`TrayApplicationContext.cs`を確認したところ、右クリックメニューは廃止予定の`ContextMenu`ではなく、後継の`ContextMenuStrip`（`new ContextMenuStrip()`）が最初から使われている。**この変更は現時点のdicom-tool-3には影響しない**。

【改修方法】

このプロジェクトでは対応不要。もし将来、他のWinFormsコードでこれらの古いAPIを新規に使いたくなった場合は、最初から後継APIを使う。

```csharp
// before（廃止予定のContextMenu）
var menu = new ContextMenu();
menu.MenuItems.Add("項目1", OnClick);

// after（このプロジェクトで実際に使われているContextMenuStrip）
var menu = new ContextMenuStrip();
menu.Items.Add("項目1", image: null, OnClick);
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### WPF と WinForms の両方を参照するアプリケーションでは、MenuItem 型と ContextMenu 型を明確にする必要
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/10.0/menuitem-contextmenu

【前提知識】

- **WPF と WinForms の違い**
  どちらもWindows向けのデスクトップUIフレームワークだが別物。WPF (`System.Windows.Controls`名前空間) はXAMLベースの比較的新しいフレームワーク、WinForms (`System.Windows.Forms`名前空間) はより古くからあるフレームワーク。1つのプロジェクトで両方を有効化する（`UseWPF`と`UseWindowsForms`を両方`true`にする）ことも技術的には可能で、片方の画面はWPF、別の画面はWinFormsで作る、といった移行期のアプリなどで使われることがある。
- **名前空間の「あいまいな参照（ambiguous reference）」とは**
  `ContextMenu`という同じクラス名が、`System.Windows.Controls.ContextMenu`（WPF側）と`System.Windows.Forms.ContextMenu`（WinForms側）の両方に存在する場合、コード中でただ`ContextMenu`とだけ書くと、コンパイラは「どちらのContextMenuを指しているのか判断できない」というエラー（`CS0104`）を出す。これを解消するには、`using エイリアス名 = 完全な名前空間.クラス名;`のように「別名（エイリアス）」を付けてどちらを指すか明示する必要がある。

【説明】

以前（.NET Core 3.1〜.NET 9）は、`ContextMenu`、`DataGrid`、`DataGridCell`、`Menu`、`MenuItem`、`ToolBar`、`StatusBar`という型は、当時まだWPF側の`System.Windows.Controls`名前空間には存在しなかった（WPFの`System.Windows.Controls`にはこれらの型が後から追加された）。そのため、WPFとWinForms両方を参照するプロジェクトでも、これらの型名を単独で書くとWinForms側の型として自動的に解決されており、コンパイルエラーにはならなかった。

.NET 10では、`System.Windows.Controls`名前空間（WPF側）にこれらと同名の型が追加されたため、WPFとWinFormsの両方を参照している（`UseWPF`と`UseWindowsForms`が両方`true`の）プロジェクトでは、これらの型名を単独で書くと「どちらを指しているか判断できない」というコンパイルエラー（`CS0104`）が発生するようになった。

変更理由は、.NET FrameworkからWPFアプリを.NET(Core)へ段階的に移行するプロジェクトの助けになるようにするため。移行中の一時的な状態としてWPFとWinFormsが混在していても、WPFの新しい型を使いつつ.NET Framework時代のライブラリ依存も残せるようにする、という意図がある。

【放置したときの影響】

この変更は、**WPF (`UseWPF=true`) とWinForms (`UseWindowsForms=true`) の両方を同一プロジェクトで有効化している場合にのみ**発生する、コンパイルエラー（`CS0104`）。単純にビルドが通らなくなるため「放置」自体ができないタイプの変更ではあるが、原因を知らないと「なぜ急にビルドが壊れたのか」の調査に時間がかかる。

【プロジェクトでの調べ方】

各csprojファイルを`UseWPF`・`UseWindowsForms`という文字列でGrep検索したところ、`UseWindowsForms`を`true`にしているのは`services/DicomTool.TrayApp/DicomTool.TrayApp.csproj`のみで、この1件も`UseWPF`は指定されていない（WinFormsのみを使用し、WPFは参照していない）。dicom-tool-3全体を見渡しても`UseWPF`を`true`にしているプロジェクトファイルは存在しない。**この変更は現時点のdicom-tool-3には影響しない**（WPFとWinFormsが同居しているプロジェクトが存在しないため、あいまいな参照はそもそも起こりえない）。

【改修方法】

このプロジェクトでは対応不要。もし将来`DicomTool.TrayApp`にWPF画面を追加し、`UseWPF`も`true`にする場合は、`ContextMenu`等の型を使っている箇所でエイリアスを使って明示する必要がある。

```csharp
// エイリアスでどちらの名前空間のContextMenuか明示する
using ContextMenu = System.Windows.Forms.ContextMenu;
// あるいは
using ContextMenu = System.Windows.Controls.ContextMenu;
```

なお、このプロジェクトの`TrayApplicationContext.cs`では元々`ContextMenuStrip`（WinFormsの後継コントロールで、この一覧に含まれない型）を使っているため、その点でも今回のあいまいさ問題には該当しない。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### HtmlElement.InsertAdjacentElement でパラメーターの名前を変更
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/10.0/insertadjacentelement-orientation

【前提知識】

- **HtmlElement クラスとは**
  WinFormsに搭載されている`WebBrowser`コントロール（アプリ内にInternet Explorer相当のHTML表示エンジンを埋め込む機能）を通じて、埋め込んだHTMLページ内のDOM要素をC#から操作するためのクラス。今どきのアプリでは`WebView2`（Chromiumベース）を使うのが一般的で、`WebBrowser`/`HtmlElement`は比較的レガシーな部類のAPI。
- **名前付き引数（named argument）とは**
  C#では、メソッド呼び出し時に`メソッド名(引数名: 値)`のようにパラメーター名を明示して渡すことができる。可読性が上がる一方で、パラメーター名そのものが変わると、名前付き引数で呼び出しているコードはコンパイルエラーになる（位置だけで渡している場合は影響を受けない）。

【説明】

以前、`HtmlElement.InsertAdjacentElement(HtmlElementInsertionOrientation, HtmlElement)`メソッドの第1引数のパラメーター名は`orient`だった。

.NET 10からは、このパラメーター名が`orientation`という、より分かりやすい名前に変更された。メソッドの機能・引数の型・順番はまったく変わらず、名前だけの変更。

```csharp
// 以前: orient という引数名
element.InsertAdjacentElement(orient: HtmlElementInsertionOrientation.AfterEnd, newElement);

// .NET 10以降: orientation という引数名
element.InsertAdjacentElement(orientation: HtmlElementInsertionOrientation.AfterEnd, newElement);
```

【放置したときの影響】

このメソッドを名前付き引数（`orient:`）を使わずに、単に位置だけで`element.InsertAdjacentElement(HtmlElementInsertionOrientation.AfterEnd, newElement)`のように呼び出しているコードには一切影響がない。名前付き引数として`orient:`を明示していた場合のみ、.NET 10でコンパイルエラーになる。

【プロジェクトでの調べ方】

`InsertAdjacentElement`・`HtmlElement`という文字列でリポジトリ全体をGrep検索したが、dicom-tool-3のコードには1件もヒットしなかった。`DicomTool.TrayApp`は`NotifyIcon`（タスクトレイアイコン）とHTTP APIのみで構成されており、`WebBrowser`コントロールやHTML埋め込み表示の機能自体を使っていない。**この変更は現時点のdicom-tool-3には影響しない**。

【改修方法】

このプロジェクトでは対応不要。もし将来`WebBrowser`コントロールを使い、かつ`orient:`という名前付き引数でこのメソッドを呼んでいた場合は、パラメーター名を書き換えるか、名前付き引数自体を外す。

```csharp
// before
element.InsertAdjacentElement(orient: HtmlElementInsertionOrientation.AfterEnd, newElement);

// after（名前付き引数の名前を変更）
element.InsertAdjacentElement(orientation: HtmlElementInsertionOrientation.AfterEnd, newElement);

// after（名前付き引数をやめて位置引数にする、という選択肢もある）
element.InsertAdjacentElement(HtmlElementInsertionOrientation.AfterEnd, newElement);
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### TreeView チェックボックスの画像の切り捨て
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/10.0/treeview-text-location

【前提知識】

- **TreeView / TreeNode とは**
  WinFormsで階層構造（フォルダツリーのような入れ子構造）を表示するためのコントロールが`TreeView`で、その中の1つ1つの項目（ノード）を表すのが`TreeNode`。`CheckBoxes`プロパティを`true`にすると、各ノードの先頭にチェックボックスが表示されるようになる。
- **DrawMode（描画モード）とは**
  リストやツリーのようなコントロールには、「見た目をすべてOS標準に任せる」モードと、「開発者が独自に絵を描く（オーナードロー、Owner-Draw）」モードがある。`DrawMode = OwnerDrawText`は「テキスト部分だけは自分で描画する」という設定で、これを使うと`OnDrawNode`というイベント（1ノードずつ描画するタイミングで呼ばれる）の中で自前の描画コードを書けるようになる。
- **DrawDefault とは**
  `OnDrawNode`イベントの引数にある`DrawDefault`プロパティを`true`にすると、「自分で描いた後、残りの部分は標準の描画処理に任せる」という意味になる。
- **AppContext スイッチとは**
  .NETには、新しい挙動と昔からの挙動を切り替えるための「AppContextスイッチ」という仕組みがある。破壊的変更が入った際、いきなり全員に新しい挙動を強制するのではなく、プロジェクトの`runtimeconfig.json`にスイッチ名を書くことでオプトイン（自分から望んで有効化）できるようにする、という緩やかな移行手段としてよく使われる。

【説明】

`CheckBoxes = true`かつ`DrawMode = OwnerDrawText`かつ`OnDrawNode`イベント内で`DrawDefault = true`にする、という3条件がすべて揃った特殊な組み合わせにおいて、以前はTreeNodeのテキスト描画位置の都合で、チェックボックスの画像が右端で見切れて表示されてしまうという表示崩れがあった。

.NET 10では、プロジェクトの`runtimeconfig.json`に`"System.Windows.Forms.TreeView.MoveTreeViewTextLocationOnePixel": true`というAppContextスイッチを追加することで、チェックボックス画像がテキストの位置を1ピクセルずらすことで完全に表示されるよう修正できるようになった。ただし、これは既定でオンになる変更ではなく、明示的にスイッチをオンにして初めて新しい挙動が有効になる（オプトイン方式）。

【放置したときの影響】

TreeViewでチェックボックスを使っておらず、あるいはオーナードロー（`DrawMode = OwnerDrawText`）をカスタマイズしていないアプリには一切関係ない。3条件すべてに該当するアプリでは、このスイッチを有効化しない限り、従来通りチェックボックス画像が見切れたままになる（見た目の問題であり、機能自体が使えなくなるわけではない）。

【プロジェクトでの調べ方】

`TreeView`・`CheckBoxes`・`DrawMode`・`OnDrawNode`という文字列でリポジトリ全体をGrep検索したが、dicom-tool-3のコードには1件もヒットしなかった。`DicomTool.TrayApp`が使っているWinFormsコントロールは`NotifyIcon`と`ContextMenuStrip`のみで、`TreeView`コントロール自体を使っていない。**この変更は現時点のdicom-tool-3には影響しない**。

【改修方法】

このプロジェクトでは対応不要。もし将来チェックボックス付きのオーナードローTreeViewを実装し、チェックボックスの見切れに気づいた場合は、`runtimeconfig.json`にスイッチを追加する。

```json
{
    "runtimeOptions": {
        "configProperties": {
            "System.Windows.Forms.TreeView.MoveTreeViewTextLocationOnePixel": true
        }
    }
}
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### StatusStrip では、既定で System RenderMode が使用
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/10.0/statusstrip-renderer

【前提知識】

- **StatusStrip とは**
  WinFormsでウィンドウの下部などに表示する「ステータスバー」（進捗状況やメッセージを表示する細長い帯）を作るためのコントロール。`ToolStrip`系コントロール（`MenuStrip`、`ToolStrip`なども同じファミリー）の一種。
- **RenderMode（レンダリングモード）とは**
  `ToolStrip`系コントロールの「見た目の描画方法」を切り替える設定。代表的な値に、Windowsのテーマ（OS標準の見た目）に合わせて描画する`System`モードと、.NET独自のより現代的な配色・見た目で描画する`Professional`モード（既定値として長らく使われてきた）がある。
- **今回の経緯（.NET 9 → .NET 10）**
  実はこの`StatusStrip`の既定レンダラーは、.NET 9で一度「変更」されていた。今回の.NET 10の変更は、その.NET 9での変更を**元に戻す**（.NET 8以前の挙動に戻す）というもの。

【説明】

.NET 9では`StatusStrip`の既定のレンダラーが変更されていたが、.NET 10ではこれが撤回され、`StatusStrip.RenderMode`プロパティの既定値が`ToolStripRenderMode.System`（Windowsのテーマに沿った見た目で描画する、.NET 8以前と同じ挙動）に戻された。

変更理由は、公式ドキュメント上でも「.NET 9での変更を元の既定の動作に戻すため」とだけ説明されており、.NET 9での変更が意図しない見た目の変化を招いた（あるいはユーザーからのフィードバックがあった）ことを受けての「巻き戻し」と考えられる。

推奨されるアクションは「なし」と明記されている。

【放置したときの影響】

`StatusStrip`の外観（背景色やグラデーションのかかり方など）に若干の見た目の変化が生じる可能性がある。ただし機能面（クリックやイベント処理）には影響しない、純粋な見た目だけの話。.NET 9からアップグレードする場合は「.NET 8以前の見慣れた見た目に戻る」方向の変化であり、.NET 9を経由せず.NET 8→10へ直接アップグレードする場合はそもそも実質的な変化を感じない可能性が高い。

【プロジェクトでの調べ方】

`StatusStrip`という文字列でリポジトリ全体をGrep検索したが、dicom-tool-3のコードには1件もヒットしなかった。`DicomTool.TrayApp`にはステータスバーを表示するウィンドウ自体が存在しない（タスクトレイアイコンとその右クリックメニューのみで構成される常駐アプリのため）。また、このプロジェクトは.NET 9を経由せず.NET 8ベースからの更新であるため、そもそも.NET 9での変更の影響を一度も受けていない。**この変更は現時点のdicom-tool-3には影響しない**。

【改修方法】

このプロジェクトでは対応不要。公式の推奨アクションも「なし」。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### System.Drawing OutOfMemoryException が ExternalException に変更されました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/windows-forms/10.0/system-drawing-outofmemory-externalexception

【前提知識】

- **System.Drawing と GDI+ とは**
  `System.Drawing`名前空間（`Bitmap`、`Graphics`、`Icon`、`Image`など）は、.NETから画像描画・画像処理を行うためのAPI群。Windows上では内部的に「GDI+」というWindows標準の描画エンジン(C++で書かれたOSコンポーネント)を呼び出すことで実装されている。WinFormsアプリのアイコン表示・図形描画などは、ほぼ確実にこのSystem.Drawing/GDI+を経由する。
- **GDI+のエラーコードと.NET例外の対応関係**
  GDI+は内部的に処理の成否を`Status`という列挙型のエラーコードで返してくる。.NETはこのエラーコードを、対応する.NET例外に変換してから呼び出し元に投げる、という設計になっている。例えば`Status.InvalidParameter`は`ArgumentException`に、といった具合。
- **OutOfMemoryException と ExternalException の違い**
  `OutOfMemoryException`は、本来「実際にメモリが枯渇して確保に失敗した」ことを表す、.NETの中でもかなり深刻な例外（通常のtry-catchでの復旧を前提としないことが多い）。一方`ExternalException`（`System.Runtime.InteropServices`名前空間）は、「.NET外部のアンマネージドコード（今回はGDI+）側で何らかのエラーが起きた」ことを表す、より汎用的な例外。GDI+由来の他の多くのエラーは、すでにこの`ExternalException`（またはそのサブクラス）に変換されている。

【説明】

以前は、GDI+が`Status.OutOfMemory`というエラーコードを返してきた場合、.NETはこれを額面通りに受け取って`OutOfMemoryException`をスローしていた。しかし実際には、この`Status.OutOfMemory`は「本当にメモリが足りない」場合だけでなく、無効な入力（サイズ0のBitmapを作ろうとした、壊れた画像データを読み込もうとした等）が原因で内部オブジェクトの作成に失敗した場合にも、GDI+側の都合で返されてくることが多かった。その結果、実際にはメモリ不足でも何でもないのに、「メモリ不足」という誤解を招く深刻な例外が飛んでくる、という分かりにくい状況が起きていた。

.NET 10からは、GDI+から`Status.OutOfMemory`が返ってきた場合、`OutOfMemoryException`ではなく`ExternalException`がスローされるように変更された。これにより、他の多くのGDI+由来のエラーと同じ例外系統に統一され、「本当のメモリ不足（.NET自体のメモリ確保の失敗）」と「GDI+内部のエラー」がより正確に区別できるようになった。

【放置したときの影響】

`System.Drawing`（`Bitmap`のコンストラクター、`Graphics`のメソッド、`Image`のメソッド、`Icon`のコンストラクターなど）を使う処理を`try { ... } catch (OutOfMemoryException) { ... }`のように、この例外だけを個別にキャッチしているコードがあると、.NET 10ではGDI+由来のエラーがこのcatchブロックに引っかからなくなり、代わりにキャッチされずにアプリ全体がクラッシュする（未処理例外になる）可能性がある。

```csharp
// このコードは.NET 10ではExternalExceptionを捕捉できず、意図した通りに動かない
try
{
    using var bmp = new Bitmap(invalidWidth, invalidHeight);
}
catch (OutOfMemoryException)
{
    // .NET 10ではここに来ず、未処理例外としてアプリが落ちる可能性がある
}
```

【プロジェクトでの調べ方】

`System.Drawing`・`new Bitmap`・`new Icon`・`Graphics.`という文字列でリポジトリ全体をGrep検索したが、dicom-tool-3のコードには1件もヒットしなかった。`DicomTool.TrayApp`でSystem.Drawingが登場する箇所は、`TrayApplicationContext.cs`内で`Icon = SystemIcons.Application`（アイコンオブジェクトへの参照代入のみで、`Bitmap`や`Graphics`を自前で生成・描画する処理ではない）の1箇所のみであり、`try/catch(OutOfMemoryException)`のような例外処理も存在しない。**この変更は現時点のdicom-tool-3には影響しない**。

【改修方法】

このプロジェクトでは対応不要。もし将来、画像処理機能（例えばDICOM画像のサムネイル生成など）を`System.Drawing`で実装し、`OutOfMemoryException`を個別にキャッチしていた場合は、`ExternalException`も併せてキャッチするよう修正する。

```csharp
// before
try
{
    using var bmp = new Bitmap(width, height);
}
catch (OutOfMemoryException ex)
{
    // GDI+由来のエラーもここで拾えていた
}

// after（GDI+由来のエラーはExternalExceptionとして拾う）
using System.Runtime.InteropServices;

try
{
    using var bmp = new Bitmap(width, height);
}
catch (ExternalException ex)
{
    // GDI+エラー（従来のOutOfMemoryExceptionケースを含む）
}
catch (OutOfMemoryException ex)
{
    // 本当のメモリ不足
}
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）
