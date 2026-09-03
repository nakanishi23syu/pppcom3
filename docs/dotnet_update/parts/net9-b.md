## Core .NET ライブラリ

### CompressionLevel を使用して ZipArchiveEntry を追加すると、ZIP 中央ディレクトリ ヘッダーの汎用ビット フラグが設定される
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/compressionlevel-bits

【前提知識】

- **ZIPファイルとバイナリフォーマットとは**
  `.zip`ファイルは、複数のファイルを1つにまとめて圧縮するための、決まったバイト列のルール(仕様)に従って作られたファイル。ZIPファイルの中には、各ファイル(エントリと呼ぶ)ごとに「中央ディレクトリヘッダー」という、ファイル名や圧縮方式などのメタ情報を記録した部分がある。
- **汎用ビットフラグ(general purpose bit flag)とは**
  中央ディレクトリヘッダーの中にある、0か1のスイッチが並んだ小さな領域。ビットの位置ごとに意味が決まっていて、たとえば「このエントリは暗号化されているか」「ファイル名はUTF-8でエンコードされているか」などをON/OFFで表す。今回の変更で扱う「ビット1」「ビット2」は、そのエントリがどのくらい強く圧縮されたか(圧縮レベル)を表すためのビットとして仕様上定義されている。
- **CompressionLevelとCompressionOptionとは**
  .NETの`System.IO.Compression.CompressionLevel`は、ZIPにファイルを追加するときに「どれくらい圧縮を頑張るか」を指定するenum(`Optimal`=バランス重視、`Fastest`=速度重視、`SmallestSize`=サイズ重視、`NoCompression`=圧縮しない)。`System.IO.Packaging.CompressionOption`はこれの.NET Framework時代からある古い版で、Officeの`.docx`/`.xlsx`のようなOPCパッケージ(ZIPをベースにしたファイル形式)で使われる。
- **.NET FrameworkからDNETへの「移植」で機能が抜け落ちることがある**
  .NET Framework(Windows専用の古い.NET)にあった機能を、クロスプラットフォームな.NET(.NET Core以降)に移植する際、開発者が意図せず一部の細かい挙動を実装し忘れることがある。今回はまさにその「移植漏れ」を後から復元したケース。

【説明】

以前(.NET Framework)は、ZIPファイルを作るときに「圧縮レベル」を汎用ビットフラグのビット1・ビット2に反映させていた。ところが.NET Framptcorkから.NET(Core)への移植の過程でこの処理が抜け落ち、.NET 8以前では`ZipArchive.CreateEntry(string, CompressionLevel)`でどんな`CompressionLevel`を指定しても、これらのビットは常に`0`(既定値`Optimal`扱い)のままだった。

.NET 9では、この機能が復元され、指定した`CompressionLevel`に応じて実際にビット1・ビット2が次のように設定されるようになった。

| CompressionLevel | ビット1 | ビット2 |
|---|---|---|
| NoCompression | 0 | 0 |
| Optimal | 0 | 0 |
| SmallestSize | 1 | 0 |
| Fastest | 1 | 1 |

また、`System.IO.Packaging.Package.CreatePart(Uri, string, CompressionOption)`(OPCパッケージへのファイル追加)を使う場合も、指定した`CompressionOption`が対応する`CompressionLevel`にマップされ、同様にビットへ反映されるようになった。

変更理由は、「.NET Frameworkにあった正しい挙動を復元し、System.IO.Packagingのようなダウンストリームの利用者がこれらのビットをきちんとコントロールできるようにするため」。

【放置したときの影響】

影響は限定的。生成されるZIPファイル自体は問題なく開けるし、Windowsのエクスプローラーや一般的な解凍ソフトで壊れて見えることはない。影響が出るのは、**ZIPファイルの中央ディレクトリヘッダーの汎用ビットフラグを自前でバイト単位でチェック・比較しているような特殊なコード**がある場合のみ。

```csharp
// 例: 圧縮レベルを指定してZIPエントリを作成
using var archive = ZipFile.Open("out.zip", ZipArchiveMode.Create);
archive.CreateEntry("data.txt", CompressionLevel.SmallestSize);
// .NET 8以前: 汎用ビットフラグは常に0,0
// .NET 9以降: ビット1=1, ビット2=0 になる
```

このZIPファイルをバイト単位で比較する自動テスト(ハッシュ値比較やバイナリdiff)があると、.NET 9移行後にテストが落ちる可能性がある。それ以外の通常のZIP読み書きには実質影響しない。

【プロジェクトでの調べ方】

`ZipArchive`/`ZipFile`/`CompressionLevel`/`System.IO.Packaging`でリポジトリ全体をGrepして、ZIP作成コードがあるか確認する。実際にGrep/Globで調べたところ、`ZipArchive`、`CompressionLevel`、`ZipFile.`、`System.IO.Packaging`のいずれもdicom-tool-3のC#コード(`*.cs`)からは1件もヒットしなかった。DICOMファイルの保存やTemporalワークフロー、GraphQL API等はいずれもファイルシステムへの直接コピー(`Path.Combine`など)やDICOM独自形式の読み書きが中心で、ZIP圧縮機能は現時点で使われていない。したがって**この変更は現時点のdicom-tool-3には影響しない**。

【改修方法】

対応不要(現状ZIP機能を使っていないため)。将来ZIPエクスポート機能などを追加する場合は、生成物をバイナリdiffするテストを書かないよう注意するか、必要であれば`CompressionLevel.Optimal`または`NoCompression`を明示的に指定してビットを`0`に固定する。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### 非オープン ジェネリックに対する UnsafeAccessor のサポートが変更された
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/unsafeaccessor-generics

【前提知識】

- **リフレクション(Reflection)とは**
  実行時にクラスやメソッドの情報を調べたり、通常は呼び出せないはずのメンバーを呼び出したりする仕組み。たとえば`private`なメソッドでも、リフレクションを使えば外部から無理やり呼び出すことができる。ただし通常のリフレクション(`MethodInfo.Invoke`など)は、メソッド探索や引数チェックのオーバーヘッドがあるため遅い。
- **UnsafeAccessorAttributeとは**
  .NET 8で追加された属性。「高速なprivateリフレクション」とも呼ばれ、`extern static`な空のメソッド(自分では中身を書かない)に付けておくと、コンパイル時ではなく実行時にランタイムが「本物のprivateメンバーを直接呼び出すコード」を裏で生成してくれる。通常のリフレクションよりずっと高速。
  ```csharp
  [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "SomePrivateMethod")]
  extern static void CallPrivate(SomeClass instance);
  ```
- **ジェネリック型・クローズドジェネリックとは**
  `List<T>`のように型引数`T`を持つ型がジェネリック型。`T`に`int`のような具体的な型を当てはめた`List<int>`のような状態を「クローズドジェネリック(閉じたジェネリック)」と呼ぶ。
- **メタデータ署名の一致とは**
  .NETの実行ファイル内では、メソッドは名前だけでなく「どんな型のパラメーターを持つか」という詳細な情報(シグネチャ)込みで管理されている。ランタイムが`UnsafeAccessor`で「本物のメソッド」を探すとき、型パラメーターも含めてこの情報が一致するかどうかを厳密にチェックする。

【説明】

.NET 8で`UnsafeAccessorAttribute`が導入された際、時間の制約からジェネリック型への対応は正式にはサポート外だった。しかし実際には、CoreCLRとNative AOTの実装上のバグにより、一部のジェネリック型を使うシナリオが「たまたま動いてしまう」状態になっていた。たとえば次のようなコードが、正式サポートではないのに.NET 8では動いてしまっていた。

```csharp
[UnsafeAccessor(UnsafeAccessorKind.Method, Name = ".ctor")]
extern static void CtorAsMethod(List<int> c);
```

.NET 9では、この「意図しない抜け道」がふさがれ、正式にサポートされる書き方が明文化された。正式な書き方は、`extern static`メソッド側の型パラメーターとメソッドパラメーターが、アクセスしたいprivateメソッド側の型パラメーター・メソッドパラメーターときちんと一致している必要がある、というもの。

```csharp
class Accessor<T>
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = ".ctor")]
    public extern static void CtorAsMethod(List<T> c);
}
```

変更理由は単純明快で、「元々サポートする予定ではなかった機能が、バグによってたまたま動いていただけなので、意図通りに修正した」というもの。

【放置したときの影響】

**dicom-tool-3のような通常のアプリケーション開発では、`UnsafeAccessorAttribute`自体を使うこと自体がかなり稀。** この属性は主にライブラリ作者が、外部に公開されていないBCL(基本クラスライブラリ)の内部実装にリフレクションなしで高速アクセスしたい場合などに使う、かなり低レベルなテクニック。もし使っていれば、.NET 9へ移行してビルドし直した際にコンパイルエラーまたは実行時エラーになる可能性があるが、使っていなければ影響はゼロ。

【プロジェクトでの調べ方】

`UnsafeAccessor`でリポジトリ全体をGrepして確認する。実際に調べたところ、dicom-tool-3のC#コードから`UnsafeAccessor`は1件もヒットしなかった。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

対応不要。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### カスタム診断 ID を使用した API の旧型式化
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/obsolete-apis-with-custom-diagnostics

【前提知識】

- **Obsolete(旧型式・非推奨)属性とは**
  C#には「このAPIはもう使わないでください」とマークする`[Obsolete]`という属性がある。この属性が付いたAPIを使うと、コンパイル時に警告(場合によってはエラー)が出る。
- **診断ID(コンパイラ警告の番号)とは**
  C#コンパイラが出す警告やエラーには、`CS0618`のような番号(診断ID)が振られている。`[Obsolete]`によるおなじみの警告は基本的に`CS0618`という共通の番号を使う。
- **`#pragma warning disable`による警告の抑制とは**
  特定の診断IDの警告を「知っていてあえて使っている」場合、コード中に`#pragma warning disable CS0618`と書くことで、その警告だけをまとめて黙らせることができる。しかし`CS0618`を丸ごと抑制すると、「本当に見たい旧型式警告」まで一緒に消えてしまう問題があった。
- **SYSLIBxxxxというカスタム診断IDとは**
  .NETチームは、特に重要な非推奨API(セキュリティ上の理由など)に対して、`CS0618`とは別の専用の診断ID(`SYSLIB0009`など)を割り当てることがある。これを使うと、「`CS0618`は丸ごと抑制しつつ、この特定のAPIの警告だけは残す(逆に、この警告だけをピンポイントで抑制する)」といった細かい制御ができる。

【説明】

以前のバージョンの.NETでは、今回対象になった一部のAPI(下記参照)は、ビルド時に警告なく普通に使うことができた。.NET 9以降では、これらのAPIに新たに`SYSLIBxxxx`というカスタム診断IDが割り当てられ、使用するとコンパイル時に警告(将来的にはエラーになる可能性もある)が出るようになった。

対象は以下の6つの診断ID・APIグループ。

| 診断ID | 内容 |
|---|---|
| SYSLIB0009 | `AuthenticationManager`は非サポート(呼んでも何もしないか例外) |
| SYSLIB0014 | `ServicePointManager`は完全に非推奨(TLS設定等に効果なし) |
| SYSLIB0054 | `Thread.VolatileRead`/`VolatileWrite`は非推奨、`Volatile.Read`/`Write`を使う |
| SYSLIB0055 | 符号付き引数版の`AdvSimd.ShiftRightLogicalRoundedNarrowingSaturate*`(ARM向けSIMD命令)が非推奨 |
| SYSLIB0056 | `Assembly.LoadFrom`の`AssemblyHashAlgorithm`引数付きオーバーロードが非推奨 |
| SYSLIB0057 | バイト配列/ファイルパスから直接`X509Certificate2`/`X509Certificate`を作るコンストラクターが非推奨(暗号化されていない秘密鍵の扱いに関するセキュリティ上の理由) |

変更理由は、「これらのカスタム診断IDを使う警告は、標準の`CS0618`抑制では消せないようにすることで、開発者が誤って重要な警告まで見落とさないようにするため」。

【放置したときの影響】

診断IDごとに温度感が異なる。

- **SYSLIB0009/SYSLIB0014**: 元々.NET Core移行時から機能していないAPI(`AuthenticationManager`/`ServicePointManager`)なので、実質「使っていても動いていない」状態。警告が増えるだけで挙動は変わらない。
- **SYSLIB0054/SYSLIB0055/SYSLIB0056**: 代替APIへの単純な置き換えで済むケースがほとんど。放置してもすぐ動かなくなるわけではないが、警告は増える。
- **SYSLIB0057(X509Certificate2のバイト配列/パス直接コンストラクター)**: 特に注意。証明書を扱うASP.NET Core Web API(dicom-tool-3の`backend/DicomTool.Api`)でJWT認証やHTTPS証明書の読み込みに古いコンストラクターを使っていると、この警告が出る。放置してもすぐ動かなくなるわけではないが、将来の.NETバージョンで完全に削除される可能性があるAPIなので、早めの対応が推奨される。

```csharp
// 警告が出るようになる書き方(SYSLIB0057)
var cert = new X509Certificate2(pfxBytes, password);

// 推奨される書き方
var cert = X509CertificateLoader.LoadPkcs12(pfxBytes, password);
```

【プロジェクトでの調べ方】

`AuthenticationManager`、`ServicePointManager`、`VolatileRead`、`VolatileWrite`、`AssemblyHashAlgorithm`、`X509Certificate2(`、`X509Certificate(`でGrepする。実際に調べたところ、これらのいずれもdicom-tool-3のC#コードから1件もヒットしなかった。JWT認証(`backend/DicomTool.Api`)まわりも確認したが、証明書の読み込みには関与していない構成だった。**この変更は現時点のdicom-tool-3には影響しない。**

なお、ビルドを.NET 9/10に切り替えた際に上記APIを使っているNuGetパッケージ経由で間接的に警告が出ることは論理上あり得るが、その場合は自分のコードではなく参照パッケージ側の更新を待つ必要がある。

【改修方法】

対応不要(現状該当なし)。将来これらのAPIを使うコードを追加する場合は、警告メッセージ中のリンク先ガイダンス(各`SYSLIBxxxx`のドキュメント)に従って代替APIに置き換える。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### StringValues の暗黙的な演算子に影響するあいまいなオーバーロードの解決
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/ambiguous-overload

【前提知識】

- **オーバーロード解決とは**
  同じ名前のメソッドが引数の型違いで複数定義されている(オーバーロードされている)とき、コンパイラが「実際にどのメソッドを呼ぶべきか」を引数の型から自動的に選び出すこと。通常はどれか1つに一意に決まるが、複数の候補が同じくらい「もっともらしい」場合、コンパイラは選びきれず`CS0121`(あいまいな呼び出し)というコンパイルエラーを出す。
- **params引数とは**
  `void M(params string[] args)`のように書くと、`M("a", "b", "c")`のように可変個の引数を配列としてまとめて渡せる機能。C# 13からは配列だけでなく`params ReadOnlySpan<T>`も書けるようになった(項目「C#のオーバーロード解決ではparams span型のオーバーロードが優先される」を参照)。
- **暗黙的な変換演算子(implicit operator)とは**
  `public static implicit operator T(U u)`のように定義しておくと、`U`型の値を明示的なキャストなしに自動的に`T`型として扱えるようになる仕組み。
- **StringValuesとは**
  `Microsoft.Extensions.Primitives.StringValues`は、ASP.NET Coreでクエリ文字列やHTTPヘッダーの値(1個の場合も複数個の場合もある)を表すための型。使い勝手のため、`string`や`string[]`からの暗黙変換演算子を持っている。

【説明】

.NET 9で、`string.Join`や`Path.Combine`など多数のコアAPIに、`params string[]`版に加えて`params ReadOnlySpan<string>`版のオーバーロードが新設された(詳細は次項「params-overloads」参照)。

このとき、もし呼び出しコードが`StringValues`型の値を絡めて使っていると、`StringValues`が持つ「`string[]`への暗黙変換」と「`string`への暗黙変換」の両方が候補になりうるため、コンパイラがどちらの新しいオーバーロード(配列版orスパン版)を選ぶべきか一意に決められなくなり、`CS0121`エラーが発生するケースが生まれた。

```
CS0121: The call is ambiguous between the following methods or properties:
'Program.Join(string, params string[])' and 'Program.Join(string, params ReadOnlySpan<string>)'
```

これは.NET 9で追加された`params ReadOnlySpan<T>`オーバーロードの副作用として発生する、ソースコードレベルの互換性の問題であり、`StringValues`を引数として`string.Join`等のAPIに渡している場合にのみ発生する。

【放置したときの影響】

「放置する」というより、**この変更は.NET 9でコンパイルし直した瞬間にビルドエラーとして即座に検知される**性質のもの。実行時に静かに動作が変わるタイプの変更ではなく、コンパイルが通らなくなるので見逃しようがない。つまり影響が発生する場合は必ずビルドが失敗し、対応するまで先に進めない(逆に言えば、危険が少ないタイプの破壊的変更)。

```csharp
// 影響を受けるパターンの例(架空)
StringValues values = Request.Headers["X-Custom"];
// values が string または string[] に暗黙変換されうるため、
// string.Join(",", values) のような呼び出しがあいまいになりうる
```

【プロジェクトでの調べ方】

`StringValues`でGrepする。実際に調べたところ、dicom-tool-3のC#コードから`StringValues`は1件もヒットしなかった。`backend/DicomTool.Api`はASP.NET Core Web APIだが、`Request.Query`や`Request.Headers`の値を直接`string.Join`等のparams付きAPIに渡しているコードは見当たらない(`.Query[`/`.Headers[`もGrepでヒットなし)。**この変更は現時点のdicom-tool-3には影響しない。**

ただし、この項目は「実行して初めて気づく」類ではなく「.NET 9/10へのターゲットフレームワーク更新後、最初の`dotnet build`で即座に気づく」類のものなので、実際のアップデート作業時にはこのGrepの結果に関わらず、単純にビルドを一度通してみてエラーが出ないか確認するのが最も確実。

【改修方法】

もし該当箇所が見つかった場合は、引数を明示的にキャストするか、`values.ToArray()`のように`string[]`へ明示変換してから渡す。

```csharp
// Before(あいまいでコンパイルエラーになる可能性)
string joined = string.Join(",", values);

// After(明示的にキャストして意図を明確にする)
string joined = string.Join(",", (string[])values);
```

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### BigInteger の最大長
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/biginteger-limit

【前提知識】

- **BigIntegerとは**
  `System.Numerics.BigInteger`は、`int`(32ビット)や`long`(64ビット)のような固定サイズの整数型では表現しきれない、桁数の非常に大きい整数を扱うための型。理論上はメモリが許す限りいくらでも大きい数を表現できる。暗号処理や、桁数無制限の電卓アプリなどで使われる。
- **OverflowExceptionとは**
  数値がその型で表現できる範囲を超えたときにスローされる例外。通常は`int`や`long`のような固定長の型でオーバーフローが起きたときに使われるが、今回`BigInteger`でも上限を超えた場合にこの例外が使われるようになった。
- **ビット数の話**
  コンピューターの数値は2進数(ビット)で表現される。`(2^31) - 1`ビットというのは、約21億ビット、バイト換算で約256メビバイト(MiB)に相当する、途方もなく巨大な数値のサイズ。

【説明】

以前の`BigInteger`には、明確な上限が実質存在しなかった(`Array.MaxLength * 32`ビットまで理論上は割り当て可能)。もっとも、実際にそこまで巨大な値を作ろうとするはるか手前で、通常のPCは`OutOfMemoryException`(メモリ不足例外)を起こしてクラッシュしていた。

.NET 9からは、`BigInteger`の長さに`(2^31) - 1`ビット(約21億4000万ビット、約256MB、約6億4650万桁)という明確な上限が設定された。これを超える値を作ろうとすると、`OutOfMemoryException`ではなく`OverflowException`が一貫してスローされるようになる。

```csharp
BigInteger bigInt = new BigInteger(-1) << int.MaxValue; // OverflowExceptionがスローされる
```

変更理由は、「実際問題として使用可能メモリという物理的な制限が元々あったのに加え、一部のAPIでは特定の入力によって想定外に巨大な値を計算してしまうケースがあったため、意図的に上限を設けて、公開APIが一貫して安全に動作するようにするため」。

【放置したときの影響】

**通常のアプリケーションではほぼ確実に影響はない。** 約6億4650万桁という上限は、実務でBigIntegerを使う一般的なシナリオ(大きな整数のID生成、暗号鍵計算、桁数の多い財務計算など)をはるかに超える大きさ。この上限に達する前に、多くの環境ではメモリ不足で先に落ちる。影響があるとすれば、意図的に極端に巨大な数値を生成するようなテストコードや、ビット演算(シフト演算等)を暴走させるようなバグが元々あったコードで、例外の種類が`OutOfMemoryException`から`OverflowException`に変わる程度。

【プロジェクトでの調べ方】

`BigInteger`でGrepする。実際に調べたところ、dicom-tool-3のC#コードから`BigInteger`は1件もヒットしなかった。DICOMのUID(識別子)は文字列として扱われ、数値としてBigIntegerを使うような処理は存在しない。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

対応不要。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### BinaryReader.ReadString() は、形式が正しくないシーケンスに対して "�" を返します
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/binaryreader

【前提知識】

- **BinaryReaderとは**
  `System.IO.BinaryReader`は、バイナリ形式(生のバイト列)のストリームから、`int`や`string`などの値を読み取るためのクラス。`.ReadString()`は、事前に書き込まれた長さ情報付きの文字列を読み取るメソッド。
- **文字エンコーディングと「形式が正しくないバイト列(malformed sequence)」とは**
  文字列はバイト列としてファイルやストリームに保存される際、UTF-8などのルール(エンコーディング)に従って変換される。しかし何らかの理由(データ破損、バグ、意図的な不正入力など)で、UTF-8のルールに従っていない中途半端なバイト列が混じっていることがある。これを「形式が正しくない(malformed)」バイト列と呼ぶ。
- **U+FFFD(REPLACEMENT CHARACTER、置換文字)とは**
  Unicode標準で定義されている特殊な文字で、「本来ここにあるべき文字をデコードできなかった」ことを示すための、いわば「文字化けを表す正式な記号」(よく「�」の記号で表示される)。多くのソフトウェアやブラウザは、不正なバイト列を検出するとこの文字で置き換えて表示する。

【説明】

以前(.NET 9より前)、`BinaryReader.ReadString()`が`[0x01, 0xC2]`のような形式の正しくないUTF-8バイト列を読み込んだ場合、デコードできなかった部分を単に無視し、空文字列(`""`)を返していた。

.NET 9以降では、Unicode標準に沿って、デコードできなかった部分をU+FFFD(置換文字)に置き換えて返すようになった。つまり、空文字列ではなく、長さ1の「�」という文字列が返るようになる。

```csharp
var ms = new MemoryStream(new byte[] { 0x01, 0xC2 });
using var br = new BinaryReader(ms);
string s = br.ReadString();

// .NET 8以前: s == ""(長さ0)
// .NET 9以降: s == "�"(長さ1)
```

変更理由は、実装上の稀なシナリオに対するパフォーマンス改善の一環として、Unicode標準に合わせて挙動を統一するため。

【放置したときの影響】

**影響は小さい。** そもそもこの変更は「正しくエンコードされた通常の文字列」には一切影響しない。影響を受けるのは、**壊れたデータや不正なバイト列を`BinaryReader.ReadString()`で読み込んだ場合**のみ。通常運用でDICOMファイルやJSON等、正しくエンコードされたデータしか扱っていなければ実質影響はない。ただし、「読み込み失敗時に空文字列が返ることを前提に`if (s == "")`のような分岐を書いているコード」があると、その分岐が動かなくなる可能性がある。

【プロジェクトでの調べ方】

`BinaryReader`でGrepする。実際に調べたところ、dicom-tool-3のC#コードから`BinaryReader`は1件もヒットしなかった。DICOMファイルの解析にはfo-dicom等の専用ライブラリを使っており、`BinaryReader.ReadString()`を直接使うコードはない。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

対応不要。もし将来`BinaryReader.ReadString()`を使う場面があり、以前の「末尾の不完全なバイト列を黙って捨てる」挙動を再現したい場合は、結果に対して`TrimEnd('�')`を呼び出す。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### C# のオーバーロード解決では、params span 型のオーバーロードが優先される
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/params-overloads

【前提知識】

- **Span<T>/ReadOnlySpan<T>とは**
  「配列などのメモリ上の連続領域への参照」を、ヒープへの新たな割り当て(GCの負荷)なしに軽量に扱うための構造体。パフォーマンスが重要な場面で、配列を新しく作らずに一部分だけを扱いたいときによく使われる。
  `Span<T>`は書き込み可能、`ReadOnlySpan<T>`は読み取り専用。実装上「ref struct」というスタック上にしか置けない特殊な型になっている。
- **params引数と「展開形式」の呼び出しとは**
  `void M(params string[] xs)`があるとき、`M("a", "b")`のように直接複数の引数を並べて呼ぶ書き方を「展開形式(expanded form)」と呼ぶ。この場合コンパイラが裏で`new string[] { "a", "b" }`を作ってから渡している。
- **式ツリー(Expression Tree)とは**
  C#のラムダ式を、実行可能なコードとしてではなく「コードの構造を表すデータ」として扱う仕組み(`Expression<Func<...>>`型)。LINQ to SQLやEntity Frameworkのクエリ変換などで使われる。式ツリーの中では、`ref struct`(スタック限定の型、`Span<T>`など)は表現できないという制約がある。

【説明】

C# 13で、`params`パラメーターが配列型だけでなく`params ReadOnlySpan<T>`や`params Span<T>`のような「スパン型」でも書けるようになった。そして両方のオーバーロードが存在する場合、C#のオーバーロード解決規則では、パフォーマンス上の理由から`params`配列型よりも`params`スパン型のオーバーロードが優先的に選ばれるようになった。

.NET 9では、`string.Join`、`Path.Combine`、`Console.WriteLine`など多数のコアAPIに、既存の`params T[]`版に加えて新しい`params ReadOnlySpan<T>`版のオーバーロードが追加された(詳しくは影響を受けるAPIの一覧を参照)。これにより、.NET 9 + C# 13でこれらのメソッドを展開形式で呼び出すコードを再コンパイルすると、コンパイラは自動的に新しい`params`スパン版のオーバーロードにバインドするようになる。

通常のコードではこれは「暗黙的に配列の割り当てを避けて高速化される」という良い効果しかない。しかし、**式ツリー(`Expression<...>`)の中でこれらのメソッドを呼んでいる場合は話が別。** 式ツリーは`ref struct`であるスパン型を表現できないため、コンパイラエラーになる。

```csharp
using System.Linq.Expressions;

Expression<Func<string, string, string>> join =
    (x, y) => string.Join("", x, y);
```

.NET 8では`Join(String, String[])`にバインドされ問題なくコンパイルできていたが、C# 13 + .NET 9では`Join(String, ReadOnlySpan<String>)`にバインドされようとし、次のコンパイルエラーになる。

```
エラー CS8640: 式ツリーに ref 構造体または制限型 'ReadOnlySpan' の値を含めることはできません。
エラー CS9226: 式ツリーに、配列以外のパラメーターの展開形式が含まれていない可能性があります
```

【放置したときの影響】

- **式ツリーの中で該当APIを呼んでいない場合**: 影響なし。むしろパフォーマンスが自動的に改善される、良い変更。
- **式ツリーの中で該当APIを呼んでいる場合**: **コンパイルエラーになり、ビルドが通らなくなる。** これは実行時に静かに壊れるタイプの変更ではなく、ビルド時に確実に検知できるので危険度としては低いが、「動かない」という意味では影響が大きい。dicom-tool-3のようにEntity Framework Core(EF Core)を使ってLINQクエリをデータベースに変換している場合、EF CoreのLINQプロバイダーが内部で式ツリーを使うため、クエリの中で対象APIを使っていると引っかかる可能性がある。

【プロジェクトでの調べ方】

まず、影響を受けるAPI一覧(`string.Join`、`string.Format`、`Path.Combine`、`Path.Join`、`Console.WriteLine`、`Task.WhenAll`等、多数)の中から、実際にdicom-tool-3で使われているものをGrepで洗い出した。`string.Join(`と`Path.Combine(`はいくつかヒットした(例: `services/DicomTool.DicomScp/Services/DicomScpService.cs:122`の`string.Join(", ", pc.GetTransferSyntaxes())`、`shared/DicomTool.Shared/Constants/StoragePaths.cs`の`Path.Combine(contentRootPath, IncomingRelativePath)`など)が、いずれも通常のメソッド呼び出しであり`Expression<...>`ラムダ式の中ではない。dicom-tool-3はEF Core等のO/Rマッパーを使っておらず(データアクセスの実装は要確認だが、少なくとも今回Grepした範囲ではLINQ式ツリーを組み立てて評価するようなコードは見当たらなかった)、式ツリーの利用箇所自体が限られる。

念のため`Expression<`でもGrepし、式ツリーを明示的に使っているコードがないか確認するとよい。**現状のGrep結果からは、この変更がdicom-tool-3に実害を与える可能性は低いと判断できる。**

【改修方法】

もし式ツリーの中で該当APIを使っていてコンパイルエラーになった場合は、明示的に配列を渡すことで、コンパイラに`params`配列版オーバーロードを選ばせる。

```csharp
// Before(エラーになる)
Expression<Func<string, string, string>> join =
    (x, y) => string.Join("", x, y);

// After(明示的な配列でparams配列版に固定)
Expression<Func<string, string, string>> join =
    (x, y) => string.Join("", new string[] { x, y });
```

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### System.Void の配列の型を作成できません
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/type-instance

【前提知識】

- **System.Voidとは**
  C#で「戻り値がない」ことを表す`void`キーワードに対応する、裏側の型。通常は`typeof(void)`のように間接的にしか触れることのない特殊な型で、`void`型の変数や配列を直接C#コードで書くことはできない(`void[] x;`はコンパイルエラーになる)。
- **Type.MakeArrayType()とは**
  `System.Type`(型情報を表すオブジェクト)に対して呼び出すと、「その型の配列」を表す新しい`Type`オブジェクトを動的に作れるリフレクションAPI。たとえば`typeof(int).MakeArrayType()`は`int[]`という型情報を返す。

【説明】

以前は、`typeof(void).MakeArrayType()`のように、本来C#では書けないはずの「`void`の配列」という無効な型情報を、リフレクション経由でなら作れてしまっていた。作れてしまった`Type`オブジェクト自体は一応存在するが、これを他のAPI(型を実際にインスタンス化する処理など)に渡すと、予期しない動作やクラッシュを引き起こすことがあった。

.NET 9以降では、`typeof(void).MakeArrayType()`を呼び出すと例外がスローされるようになり、そもそもこの無効な型を作ること自体ができなくなった。

変更理由は、「`void[]`という型はC#の言語レベルでも拒否される無効な概念であり、これを作れてしまうこと自体が一貫性のない挙動の原因になっていたため、すべての状況で一律に禁止することにした」というもの。

【放置したときの影響】

**通常のアプリケーション開発では、まず遭遇しない類の変更。** `typeof(void).MakeArrayType()`のような、意図的に`void`の配列型を作ろうとするコードは、リフレクションを駆使した非常に特殊なライブラリ・フレームワーク実装(コード生成、動的プロキシ生成など)以外では書かれることがまずない。もし該当箇所があれば、実行時に例外がスローされるようになる。

【プロジェクトでの調べ方】

`MakeArrayType`でGrepする。実際に調べたところ、dicom-tool-3のC#コードから`MakeArrayType`は1件もヒットしなかった。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

対応不要。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### 既定の Equals() と GetHashCode() は InlineArrayAttribute でマークされた型でスローします
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/inlinearrayattribute

【前提知識】

- **InlineArrayAttributeとは**
  .NET 8/C# 12で追加された、構造体(struct)に付ける属性。「インライン配列(inline array)」と呼ばれる、固定サイズの配列を構造体の中に直接埋め込む(ヒープに別途配列を確保しない)ための機能で、`Span<T>`と組み合わせてパフォーマンスが重要な低レベルコードで使われる。かなりニッチな高度な機能で、普段のアプリケーション開発でC#の言語機能として直接書くことは少ない。
- **Equals()とGetHashCode()の既定実装とは**
  C#のすべての値型(struct)は、`object`の`Equals(object)`と`GetHashCode()`を継承している。何もオーバーライドしなければ、`ValueType`クラスがリフレクションベースで「フィールドの値を1つずつ比較する」という既定の実装を提供する。
- **NotSupportedExceptionとは**
  「このメソッド・機能はサポートされていません」という意味の例外。呼び出すこと自体は許されているが、実行するとエラーになる。

【説明】

以前、`InlineArrayAttribute`が付いた構造体で`Equals()`や`GetHashCode()`を独自にオーバーライドしていない場合、既定の実装は内部の「プレースホルダー`ref`フィールド」だけを見て等価性やハッシュコードを計算していた。これは実質的に構造体の中身(実際の配列要素)を正しく比較できておらず、間違った結果を返す可能性のあるバグだった。

.NET 9以降では、この不正確な既定実装をそのまま提供する代わりに、`InlineArrayAttribute`が付いた型に対する既定の`Equals()`/`GetHashCode()`の呼び出しは、常に`NotSupportedException`をスローするようになった。「間違った答えを黙って返す」より「サポートしていないと明示的に教える」方が安全、という判断。

変更理由は、「以前の既定実装は等価性判定・ハッシュ計算の両方について正しくなく、開発者に誤った正確性の感覚(実は正しく比較できていないのに、比較できていると思い込ませる)を与えてしまっていたため」。

【放置したときの影響】

**一般的なアプリケーション開発では、`InlineArrayAttribute`を自分で使うことはほぼない。** この属性は低レベルなパフォーマンスチューニング用の機能であり、多くの開発者は間接的に(BCLやサードパーティライブラリの内部実装として)恩恵を受けるだけで、自分で書くことは稀。もし自作の`InlineArrayAttribute`付き構造体があり、かつ`Equals()`/`GetHashCode()`をオーバーライドしていない場合、それらを呼び出した瞬間に例外で落ちるようになる(以前は間違った値が返るだけで例外にはならなかった)。

【プロジェクトでの調べ方】

`InlineArray`でGrepする。実際に調べたところ、dicom-tool-3のC#コードから`InlineArray`は1件もヒットしなかった。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

対応不要。もし将来`InlineArrayAttribute`を使う構造体を自作する場合は、`Equals(object)`と`GetHashCode()`の両方を、実際の配列要素を正しく比較するようにオーバーライドしておく必要がある。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### EnumConverter は登録された型が enum であることを検証する
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/enumconverter

【前提知識】

- **TypeConverterとEnumConverterとは**
  `System.ComponentModel.TypeConverter`は、ある型と別の型(多くの場合`string`)を相互変換するための古くからある仕組み。WinFormsのプロパティグリッドや、一部の設定ファイル読み込みなどで使われる。`EnumConverter`はその中でも「`enum`型と文字列の変換」を担当する専用のコンバーター。
- **enum(列挙型)とは**
  `enum Color { Red, Green, Blue }`のように、あらかじめ決められた選択肢の集合を表す型。
- **トリミング(Trimming)とは**
  発行(publish)時に、実際に使われていないコード(未使用のクラスやメソッド)をアプリから削除して、実行ファイルのサイズを小さくする.NETの機能。自己完結型アプリやNative AOTでよく使われる。トリミングは静的解析で「使われているか」を判断するため、リフレクションで動的にアクセスされる型は誤って削除されてしまうことがあり、`DynamicallyAccessedMembersAttribute`という注釈でリフレクション対象の型をトリマーに教える必要がある。

【説明】

以前は、`EnumConverter`のコンストラクター(`EnumConverter(Type)`)に、`enum`型ではない任意の型を渡してもエラーにならなかった(本来`enum`専用のコンバーターなのに、検証されていなかった)。

.NET 9以降では、渡された型が本当に`enum`型かどうかがチェックされるようになり、`enum`型でない場合は`ArgumentException`がスローされるようになった。

```csharp
// .NET 8以前: エラーにならない(ただし正しく動作もしない)
var converter = new EnumConverter(typeof(string));

// .NET 9以降: ArgumentExceptionがスローされる
```

変更理由は2つある。1つは単純に「`enum`専用のコンバーターなのだから`enum`型だけを受け付けるのが論理的に正しい」という一貫性の話。もう1つ、より実務的な理由として、トリミング対応がある。`EnumConverter`が内部で`enum`型のメンバーにリフレクションでアクセスするため、トリマーに「この型のメンバーは消さないで」と教える`DynamicallyAccessedMembersAttribute`の注釈が必要だったが、`enum`以外の型を許してしまう設計だと、この注釈の要件がうまく機能しなかった。今回`enum`のみに限定することで、注釈の要件も整理された。

【放置したときの影響】

**通常のアプリケーション開発で、`EnumConverter`を`enum`以外の型に対して明示的に使うコードはほぼ書かれない。** `EnumConverter`はWinFormsのデザイナーや設定ファイルのバインディングの裏側で暗黙的に使われることが多く、意図的に`enum`型を渡している限りは何も影響を受けない。もし誤って`enum`以外の型を渡すコードが(バグとして)存在していた場合、以前は気づかれずに動いていた(かもしれない)ものが、.NET 9以降では例外で明確に検知されるようになる。

dicom-tool-3には`services/DicomTool.TrayApp`というWinFormsアプリがあり、`enum`型のプロパティを扱う設定画面などがあれば間接的に`EnumConverter`が使われている可能性はあるが、いずれも正しく`enum`型に対して使われているはずで、通常は問題にならない。

【プロジェクトでの調べ方】

`EnumConverter`でGrepする。実際に調べたところ、dicom-tool-3のC#コードから`EnumConverter`は1件もヒットしなかった(明示的な直接利用はない)。TrayApp内で`enum`型のプロパティを持つ設定クラスがあれば、`TypeDescriptor`経由で間接的に`EnumConverter`が使われる可能性はあるが、それらは正しく`enum`型に対して適用されるため、通常この変更で問題は起きない。**この変更は現時点のdicom-tool-3には実害を与えない見込み。**

【改修方法】

対応不要。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### FromKeyedServicesAttribute はキーなしパラメータを挿入しなくなりました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/non-keyed-params

【前提知識】

- **依存性の注入(DI: Dependency Injection)とは**
  クラスが必要とする別のクラス(依存先)を、自分で`new`して作るのではなく、コンストラクターの引数として外部から「注入」してもらう設計パターン。ASP.NET Core(dicom-tool-3の`backend/DicomTool.Api`など)は標準でDIコンテナ(`IServiceCollection`)を内蔵しており、`services.AddScoped<IFoo, Foo>()`のように登録しておくと、コンストラクターに`IFoo foo`という引数を書くだけで自動的にインスタンスが渡ってくる。
- **キー付きサービス(Keyed Services)とは**
  .NET 8から追加された機能。同じインターフェース(`IService`など)に対して複数の実装を登録し、「キー」(文字列など)で区別して使い分けられるようにする仕組み。
  ```csharp
  services.AddKeyedSingleton<IService, ServiceA>("service1");
  services.AddKeyedSingleton<IService, ServiceB>("service2");
  ```
  利用側は`[FromKeyedServicesAttribute("service1")]`という属性をコンストラクター引数に付けて、「このキーで登録されたものをちょうだい」と指定する。
- **キーなしサービスとは**
  上記のようなキーを付けずに`services.AddSingleton<IService, ServiceC>()`のように普通に登録したサービス。

【説明】

以前は、`[FromKeyedServices("service1")]`と指定してキー付きサービスの注入を要求したのに、実際には`"service1"`というキーでは何も登録されておらず、代わりに**キーなしの**`IService`が登録されていた場合、DIコンテナは例外を投げずに、間違ってそのキーなしのサービスを注入してしまっていた。つまり「特定の実装を狙って指定したつもりが、意図しない別の実装が黙って渡ってくる」というバグの温床になっていた。

.NET 9以降(および.NET 8.0.9以降にバックポート)では、指定したキーに対応するキー付きサービスが見つからない場合、`InvalidOperationException`が明確にスローされるようになった。これは「サービスが登録されていない場合はエラーにする」という、DIコンテナの他の一般的な挙動と一致させるための修正。

変更理由は、「サービスの登録漏れという構成ミスを、実行時のバグとして静かに見逃すのではなく、きちんとエラーとして検出できるようにするため」。

【放置したときの影響】

**dicom-tool-3が「キー付きサービス」機能自体を使っていなければ、無関係。** もし使っていて、かつ登録ミス(キー付きで登録すべきところをキーなしで登録してしまっていた等)があった場合は、以前は気づかれずに動いていたものが、.NET 9/10移行後は起動時またはリクエスト処理時に`InvalidOperationException`で例外が発生するようになる。これは「動かなくなる」という意味では影響が大きいが、逆に言えば「隠れていたバグが顕在化する」良い変更でもある。

```csharp
// もし "service1" というキーで登録されていなかった場合
public MyService([FromKeyedServices("service1")] IService service1) { ... }
// .NET 8以前: キーなしのIServiceが黙って渡される(バグに気づきにくい)
// .NET 9以降: InvalidOperationExceptionがスローされる
```

【プロジェクトでの調べ方】

`FromKeyedServices`、`AddKeyedScoped`、`AddKeyedSingleton`、`AddKeyedTransient`でGrepする。実際に調べたところ、dicom-tool-3のC#コードからこれらはいずれも1件もヒットしなかった。`backend/DicomTool.Api`のDI登録(`Program.cs`等)は通常の`AddScoped`/`AddSingleton`/`AddTransient`のみを使っており、キー付きサービス機能自体を利用していない。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

対応不要。将来キー付きサービスを導入する場合は、`FromKeyedServicesAttribute`で指定するキーが、`AddKeyedScoped()`等で確実に同じキーで登録されていることを確認する。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### IncrementingPollingCounter の初期コールバックは非同期です
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/async-callback

【前提知識】

- **EventSource/EventListenerとは**
  .NETに組み込まれている、パフォーマンスカウンターやトレース情報を発行・観測するための仕組み。`EventSource`がイベントを発行する側、`EventListener`がそれを受け取る側。`dotnet-counters`のような外部監視ツールも、内部的にはこの仕組みを使っている。
- **IncrementingPollingCounterとは**
  「単調増加していく値」(処理件数の累計など)を、一定間隔でポーリング(定期的に値を取りに行くこと)して報告するためのカウンター。コールバック関数(値を取得するデリゲート)を登録しておくと、`EventSource`側が裏で定期的にそれを呼び出してくれる。
- **同期/非同期呼び出しとタイマースレッドとは**
  「同期的に呼ばれる」とは、呼び出し元のコードの流れの中でそのまま実行されること。「非同期的に(別スレッドで)呼ばれる」とは、専用の別スレッド(タイマースレッド)が独立したタイミングで呼び出すこと。後者の場合、いつ呼ばれるかは呼び出し元のコードからは予測しづらい。

【説明】

`IncrementingPollingCounter`にコールバック(値取得関数)を登録すると、以前はその「最初の1回目の呼び出し」だけが、カウンターを有効化した操作を行ったスレッド上で同期的に(その場で即座に)実行されていた。2回目以降の呼び出しは、専用のタイマースレッド上で非同期的に行われていた。

.NET 9以降では、最初の呼び出しも含めて、常にタイマースレッド上で非同期的に行われるようになった。これにより、「カウンターを有効化した直後に値を変更するコード」があると、その変更が最初のコールバックに反映されるかどうかが、OSのスレッドスケジューリング次第で不確定になる。

```csharp
log.SomeInterestingValue++; // カウンター有効化直後に値を変更
// .NET 8以前: 最初のコールバックがこの変更前後どちらのタイミングで呼ばれるかは、
//             有効化操作を行ったスレッド上で同期的に呼ばれるため、比較的予測しやすかった
// .NET 9以降: 常に別スレッドで非同期に呼ばれるため、この変更の前に呼ばれるか後に呼ばれるかは
//             タイミング次第で変わりうる
```

変更理由は、「`EventListener`のロックが保持されたままコールバック関数を実行すると、デッドロック(お互いが相手の解放を待ち続けて止まってしまう状態)が起きる可能性があったため、それを解消するための修正」。

【放置したときの影響】

**`dotnet-counters`などの外部監視ツールでメトリックを見るだけの通常のシナリオでは、対応不要。** 公式ドキュメントでも「これらのシナリオは引き続き正常に動作する」と明記されている。

影響が出るのは、**`EventListener`を使って`IncrementingPollingCounter`の値をインプロセス(自プロセス内)でテストしているコード**に限られる。カウンターを有効化した直後にポーリング対象の値を変更し、「最初のコールバックでその変更後の値が観測できること」を前提にしたテストがあると、タイミング次第でテストが不安定(flaky)になる可能性がある。

【プロジェクトでの調べ方】

`IncrementingPollingCounter`、`PollingCounter`、`EventCounter`、`EventSource`でGrepする。実際に調べたところ、dicom-tool-3のC#コードからこれらはいずれも1件もヒットしなかった。dicom-tool-3ではメトリクス収集の仕組みとして`EventSource`ベースのカスタムカウンターは使われていない(ログ出力は`ILogger`ベース)。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

対応不要。将来`EventListener`経由でカウンター値をテストするコードを書く場合は、`EnableEvents()`呼び出し直後に値を変更するのではなく、`EventListener`から最初のカウンターイベントを一度受信してから値を変更するようにする(`ManualResetEvent`等で同期を取る)。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### インライン配列構造体のサイズ制限が適用される
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/inlinearray-size

【前提知識】

- **インライン配列構造体とは**
  前述の`InlineArrayAttribute`(項目9参照)が付いた構造体のこと。「1メビバイト(MiB、約104万バイト)までのサイズに制限する」ことが元々の仕様上の意図だった。
- **整数オーバーフローとラップアラウンドとは**
  コンピューターの整数型には表現できる範囲の上限がある。`Int32.MaxValue`(約21億)を超える計算をすると、値が予測しにくい形でぐるっと一周して小さな値に戻ってしまう(ラップアラウンドする)ことがある。これはバグの原因になりやすい典型的な現象。

【説明】

`InlineArrayAttribute`は.NET 8で導入された際、構造体のサイズに1MiBという上限を設ける意図だった。しかし実装上のバグにより、C#コンパイラが標準で出力する「シーケンシャルレイアウト」(フィールドが宣言順にメモリ上へ並ぶ配置方式)を持つインライン配列構造体には、この制限が実際にはまったく適用されていなかった。

その結果、.NET 8では極端に大きなサイズ(あるいは整数オーバーフローでラップアラウンドしてしまうような、`Int32.MaxValue + 1`のような不正な値)をインライン配列構造体のサイズとして宣言できてしまい、予測不能な挙動につながる可能性があった。

.NET 9以降では、このバグが修正され、意図通り1MiBのサイズ制限がすべてのインライン配列構造体に適用されるようになった。

変更理由は、単純に「元々設ける予定だった制限が、実装バグにより機能していなかったので、意図通りに修正した」というもの。

【放置したときの影響】

**通常のアプリケーション開発では影響なし。** 前述の通り`InlineArrayAttribute`自体を自分で使うことがまず稀であり、さらにその中でも1MiBを超える極端に大きなサイズを指定していたようなケースはほとんど存在しない。もし該当するコードがあれば、.NET 9以降ではコンパイルエラーまたは実行時エラーになる。

【プロジェクトでの調べ方】

`InlineArray`でGrepする(項目9で確認済み)。実際に調べたところ、dicom-tool-3のC#コードから`InlineArray`は1件もヒットしなかった。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

対応不要。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### InMemoryDirectoryInfo が rootDir をファイルの先頭に追加する
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/inmemorydirinfo-prepends-rootdir

【前提知識】

- **glob(グロブ)パターンマッチングとは**
  `**/*.cs`のような「ワイルドカード付きのパターン」でファイルパスを絞り込む仕組み。`.gitignore`の記法をイメージすると分かりやすい。
- **Microsoft.Extensions.FileSystemGlobbingとは**
  .NETで、実際のディスク上のファイルに対してglobパターンマッチングを行うためのライブラリ。`Matcher`クラスに`AddInclude("**/*.cs")`のようにパターンを追加し、`.Execute()`や`.Match()`でマッチするファイルを取得する。
- **InMemoryDirectoryInfoとは**
  通常`Matcher`は実際のディスク上のフォルダーに対してマッチングを行うが、`InMemoryDirectoryInfo`を使うと、実在するファイルシステムにアクセスすることなく、メモリ上に用意した仮想的なファイルパスのリストに対してマッチングを行える。テストコードなどでファイルシステムに依存しないテストを書きたいときに便利。
- **現在の作業ディレクトリ(CWD: Current Working Directory)とは**
  プログラムが実行されている「今いる場所」を表すパス。相対パスは、特に指定がなければこのCWDを基準に解決される。

【説明】

`InMemoryDirectoryInfo`のコンストラクターには、ルートディレクトリ(`rootDir`)と、その中にある(という想定の)ファイルパスの一覧(`files`)を渡す。

以前は、`files`に渡した相対パスが、実際には**現在の作業ディレクトリ(CWD)**を基準に解決されてしまっていた。これは「メモリ上だけで完結するはずの型なのに、実行環境のCWDという外部要因に依存してしまう」という設計上の不整合であり、CWDと`InMemoryDirectoryInfo`に指定したいパスのドライブ文字が異なる場合などにマッチングがブロックされる不具合の原因にもなっていた([dotnet/runtime#93107](https://github.com/dotnet/runtime/issues/93107))。

.NET 9以降では、`files`の相対パスは、CWDではなく**コンストラクターで指定した`rootDir`**を基準として解決されるようになった。つまり、「`files`に含まれるすべてのパスは、`rootDir`の配下にあるもの」という、より直感的で一貫した挙動になった。

```csharp
string rootDir = "root";
string[] files = ["dir1/test.0", "dir1/subdir/test.1", "dir2/test.2"];

// .NET 9以降: files内のパスはすべて rootDir("root")の下にあるものとして扱われる
PatternMatchingResult result = new Matcher().AddInclude("**/*").Match(rootDir, files);
```

変更理由は、CWDへの不要な依存を断ち切り、ドライブ文字の異なる環境でもブロックされずに動作するようにするため。

【放置したときの影響】

**`Microsoft.Extensions.FileSystemGlobbing`の`InMemoryDirectoryInfo`を明示的に使っていない限り、影響なし。** この型はテストコードで「実際にファイルを用意せずにglobマッチングをテストしたい」場合に使われる、やや専門的なAPI。使っている場合は、.NET 9以降で相対パスの解決基準が変わるため、以前CWDを基準に組んでいたテストがマッチしなくなる(結果が空になる、または想定と異なるファイルがマッチする)可能性がある。

```csharp
// Before(CWDを暗黙的に利用していたイメージ)
string rootDir = "dir1"; // CWD配下のdir1という意図
string[] files = ["dir1/test.0", "dir2/test.2"];

// After(rootDir配下のものとしてfilesを組み立て直す必要がある)
string rootDir = "root";
string[] files = ["dir1/test.0", "dir2/test.2"];
// スコープを絞りたい場合はパターン側で調整
new Matcher().AddInclude("dir1/**/*").Match(rootDir, files);
```

【プロジェクトでの調べ方】

`InMemoryDirectoryInfo`、`FileSystemGlobbing`、`new Matcher()`でGrepする。実際に調べたところ、dicom-tool-3のC#コードからこれらはいずれも1件もヒットしなかった。テストコード(`backend/DicomTool.Api.Tests`等)を含め、glob形式のファイルマッチングを行う処理は見当たらなかった。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

対応不要。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### 整数を使用する新しい TimeSpan.From*() オーバーロード
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/timespan-from-overloads

【前提知識】

- **TimeSpanとは**
  `System.TimeSpan`は、「3秒間」「1時間30分」のような時間の長さ(期間)を表す型。`TimeSpan.FromSeconds(3)`のように、`From`で始まる静的メソッド群で、秒・分・時間などの単位から`TimeSpan`インスタンスを作れる。
- **浮動小数点数(Double)の誤差とは**
  `double`型は2進数で小数を近似的に表現するため、`0.1`のような一見きりの良い小数でも、内部的には完全に正確な値として表現できず、わずかな誤差を含むことがある。これが積み重なると、計算結果が意図しない値になることがある。
- **F#とは**
  .NETで動く、C#とは文法が異なる関数型プログラミング言語。オーバーロード解決の仕組みがC#と異なり、より厳密に「引数の型から一意にメソッドを決定できること」を要求する場面がある。

【説明】

以前、`TimeSpan.FromSeconds()`、`FromMinutes()`、`FromHours()`、`FromDays()`、`FromMilliseconds()`、`FromMicroseconds()`はいずれも`double`型の引数を受け取るオーバーロードしか存在しなかった。`double`は誤差を含みうる浮動小数点数であり、これが原因のバグや使い勝手の悪さが指摘されていた。

.NET 9では、これらのメソッドに整数(`long`など)を受け取る新しいオーバーロードが追加された。C#で`TimeSpan.FromSeconds(3)`のように整数リテラルを渡すコードは、既存の`double`版と新しい整数版のどちらにもマッチしうるが、C#では「より具体的な型」である整数版が優先して選ばれるため、通常は問題なくビルドが通る。

ただし、**F#のコードでは話が別。** F#のオーバーロード解決規則はC#よりも厳密で、型注釈なしに`TimeSpan.FromMinutes(20)`のようなコードを書くと、複数の候補(`int64`版、`float`版など)から一意に決められず、コンパイルエラーになる。

```
error FS0041: メソッド "FromMinutes" に固有のオーバーロードは、
このプログラム ポイント以前は、型情報に基づいて決定できませんでした。
```

変更理由は、「`double`引数はもともと誤差を含みうる不正確な表現であり、ユーザーの混乱やバグの原因になっていたため、整数を渡せる新しいオーバーロードを追加して、より正確・効率的に扱えるようにするため」。

【放置したときの影響】

**dicom-tool-3はC#プロジェクトであり、F#コードは含まれない。** この変更が実際に問題を起こすのはF#コードに対してのみであり、通常のC#コードは基本的に再コンパイルするだけで問題なく動く(整数版オーバーロードに自動的にバインドされるだけで、意味的な挙動は変わらない)。

【プロジェクトでの調べ方】

まず、dicom-tool-3のプロジェクトファイル(`.csproj`)を見る限りすべてC#プロジェクトであり、F#プロジェクト(`.fsproj`)は存在しない。念のため`TimeSpan.From`でGrepしたところ、`services/DicomTool.Worker/Workflows/UploadDicomWorkflow.cs`や`DeleteDicomWorkflow.cs`(Temporalワークフローのリトライ設定・タイムアウト設定)、`services/DicomTool.TrayApp/TrayApplicationContext.cs`などで多数使われている(例: `TimeSpan.FromSeconds(3)`、`TimeSpan.FromMinutes(1)`)が、いずれも整数リテラルをそのまま渡すC#の一般的な書き方であり、C#のオーバーロード解決規則の下では特に問題なく動作する。**この変更はF#未使用のdicom-tool-3には実質影響しない。**

【改修方法】

対応不要(C#のみのプロジェクトのため)。もし将来F#プロジェクトを追加する場合は、`TimeSpan.FromMinutes(20)`のようなあいまいな呼び出しに対して`TimeSpan.FromMinutes(20L)`や`TimeSpan.FromMinutes(20.0)`のように型を明示する必要がある。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### 一部の OOB パッケージの新しいバージョン
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/oob-packages

【前提知識】

- **OOB(Out-Of-Band)パッケージとは**
  .NET本体(ランタイム)のリリースサイクルとは別のタイミングで、NuGet経由で個別に配布されているパッケージのこと。`System.Memory`や`Microsoft.Bcl.HashCode`のように、本来.NET本体に含まれる機能を、古い.NET Framework環境などでも使えるようにするために単独パッケージとして配布しているものが多い。
- **TFM(Target Framework Moniker、ターゲットフレームワークモニカー)とは**
  `net8.0`や`net472`のように、「このパッケージ・プロジェクトはどの.NETバージョン向けにビルドされているか」を表す識別子。1つのNuGetパッケージが複数のTFM向けのバイナリを内包していることも多い(マルチターゲティング)。

【説明】

`Microsoft.Bcl.HashCode`、`System.Buffers`、`System.Memory`、`System.Numerics.Vectors`、`System.Threading.Tasks.Extensions`など、10種類以上のOOBパッケージについて、ソースコードの管理場所が(既にサポート終了した)`dotnet/corefx`や`dotnet/runtime`の古いブランチから、現在アクティブにメンテナンスされている[`dotnet/maintenance-packages`](https://github.com/dotnet/maintenance-packages)リポジトリへ移管された。

これに伴い、各パッケージの新しいマイナーバージョンがリリースされ(例: `System.Memory`は4.5.xから4.6.0へ)、対応するTFMが更新された。ソースコード自体に変更はないが、パッケージがサポートするターゲットフレームワークの範囲が変わったため、場合によっては「以前サポートされていた古いTFM向けのバイナリが、新しいバージョンでは提供されなくなる」ことがある。

変更理由は、既にサポート終了した古いブランチで管理され続けていたこれらのパッケージを、現役でメンテナンスされているリポジトリへ移して、今後も適切に保守できるようにするため。

【放置したときの影響】

**dicom-tool-3が対象パッケージを直接NuGet参照していない限り、直接的な影響はない。** 影響があるとすれば、間接的にこれらのパッケージに依存している他のNuGetパッケージを使っている場合で、依存関係の解決(NuGet復元)時にバージョンの不整合や、古いTFM向けの互換シムが取得できなくなるといった問題が起きる可能性がある程度。ソースコード自体は変わっていないため、動作面での破壊的変更というよりは「パッケージ管理・ビルド面」での注意点に近い。

【プロジェクトでの調べ方】

各`.csproj`ファイルで、`Microsoft.Bcl.HashCode`、`System.Buffers`、`System.Memory`、`System.Numerics.Vectors`、`System.Threading.Tasks.Extensions`、`System.Data.SqlClient`等のパッケージを直接`PackageReference`していないか確認する(`dotnet list package`コマンドや、リポジトリ内`*.csproj`をGrepするとよい)。

dicom-tool-3は対象フレームワークが`net10.0`(および`net10.0-windows`)であり、これらのOOBパッケージは主に.NET Framework等の古い環境向けの互換パッケージであるため、`net10.0`をターゲットにしている時点でそもそも参照される可能性は低い。実際にリポジトリ内を軽く確認した範囲では、これらのパッケージへの直接参照は見当たらなかった。**この変更は現時点のdicom-tool-3には実害を与えない可能性が高い。**

【改修方法】

対応不要。もし`dotnet restore`やビルド時にこれらのパッケージ絡みで警告・エラーが出た場合は、[パッケージサポートポリシー](https://github.com/dotnet/maintenance-packages/tree/main/package-support-policy.md)を確認し、該当パッケージを最新版に更新する、または不要であれば参照自体を削除する。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### RuntimeHelpers.GetSubArray が異なる型を返す
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/getsubarray-return

【前提知識】

- **範囲演算子(Range operator, `..`)とは**
  C# 8で追加された、配列やリストの一部分を取り出すための構文。`arr[1..3]`のように書くと、インデックス1から3の手前までの要素を新しい配列として取り出せる。裏側では、コンパイラが`RuntimeHelpers.GetSubArray`というメソッドを自動的に呼び出している。
- **配列の共変性(covariance)とは**
  C#では、`string[]`型の変数を`object[]`型の変数に代入できる(`object[] arr = new string[1];`)。これを配列の共変性と呼ぶ。ただし実行時には、この配列の「本当の型」は`string[]`のままであり、`object[]`として振る舞っているだけ、という点に注意が必要。

【説明】

`RuntimeHelpers.GetSubArray<T>(T[] array, Range range)`は、C#コンパイラが範囲演算子(`arr[1..3]`)を実現するために内部的に使うメソッド。

以前は、このメソッドが返す配列の型は、常に型引数`T`そのものの配列型(`T[]`)だった。つまり、共変性を使って`object[]`型の変数に`string[]`の実体を入れていた場合、`GetSubArray<object>(...)`が返す配列は`object[]`型になっていた(実体が`string[]`だったにもかかわらず)。

.NET 9以降では、返される配列の型が、渡した`array`引数の**実際の型**(この例では`string[]`)と一致するように修正された。

```csharp
object[] arr = new string[1];
// .NET 8以前: arr[1..2] が返す配列の実際の型は object[]
// .NET 9以降: arr[1..2] が返す配列の実際の型は string[](arrの実体と一致)
```

変更理由は、C#のパターンマッチング機能(特にリストパターン)の設計が、「`GetSubArray`が返す配列の型はソース配列の実体の型と一致する」ことを前提にしていたため。以前の挙動では、共変配列のスライスを使った複雑なパターンマッチング式で予期しない動作を引き起こしていた([dotnet/roslyn#69053](https://github.com/dotnet/roslyn/issues/69053))。

【放置したときの影響】

**この挙動の違いは、配列の共変性(`object[] arr = new string[1];`のような書き方)を使った上で範囲演算子によるスライスを行っている、かなり限定的なコードでしか観測できない。** 通常、配列は宣言した型のまま使うことがほとんどで、共変代入を意図的に使うコードは少ない。もし該当するコードがあれば、スライス後の配列の型情報(`GetType()`の結果や、型チェックを伴うパターンマッチング)に依存する処理の結果が変わる可能性がある。

【プロジェクトでの調べ方】

`GetSubArray`でGrepする(このメソッドはコンパイラが自動生成する呼び出しのため、通常は明示的にコードに書かれることはない)。実際に調べたところ、dicom-tool-3のC#コードから`GetSubArray`は1件もヒットしなかった。また、配列の共変代入(`object[] x = new string[]...`のような書き方)自体、一般的なC#開発では意図的に書かれることが少ない書き方であり、dicom-tool-3でもそのようなパターンは見当たらなかった。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

対応不要。もし共変配列とスライス(範囲演算子)を組み合わせているコードが見つかった場合は、共変代入への依存を取り除き、宣言時から実際の型で扱うようにする。

```csharp
// Before
object[] arr = new string[1];
M(arr[1..2]);

// After
string[] arr = new string[1];
M(arr[1..2]);
```

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### String.Trim(params ReadOnlySpan<char>) オーバーロードを削除
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/string-trim

【前提知識】

- **String.Trim/TrimStart/TrimEndとは**
  文字列の前後(または片方)から、指定した文字を取り除くメソッド群。引数なしなら空白文字を、`char[]`や個別の`char`を渡せば「その中に含まれるどの文字でも」取り除く、という「配列的な(各要素が個別の区切り文字として扱われる)」挙動をする。
- **拡張メソッド(Extension Method)とは**
  `public static class Extensions { public static string TrimEnd(this string s, string suffix) { ... } }`のように、既存の型(ここでは`string`)に対して、あたかも元々そのクラスにあったかのようなメソッドを追加できるC#の機能。ただし、もし後から本家の`string`型自身に同名のインスタンスメソッドが追加されると、C#の名前解決規則により、そちらが優先して呼ばれるようになり、拡張メソッドの方は無視されてしまう。
- **.NET 9のプレビュー版とGA(正式版)の違いとは**
  .NET 9はPreview 1〜7、RC1、RC2という開発途中のプレビュー版を経て、最終的にGA(General Availability、正式リリース)される。今回の変更は「プレビュー版の一時期にだけ存在した挙動が、GA版で元に戻された」という、やや複雑な経緯を持つ。

【説明】

.NET 9の開発途中(Preview 6〜RC2)では、`string.Trim(params ReadOnlySpan<char>)`のような、文字列全体をひとかたまりの「トリム対象文字の並び」として受け取る新しいオーバーロードが一時的に追加されていた。

しかしこの新オーバーロードには問題があった。すでに世の中の多くのプロジェクトが、独自の拡張メソッド`TrimEnd(this string target, string suffix)`(部分文字列を丸ごと取り除く、という意味の実装)を定義していた。新オーバーロードが追加されたことで、`"12345!!!!".TrimEnd("!!!")`のような呼び出しが、開発者の意図していた拡張メソッドではなく、新しい組み込みの`Trim`系オーバーロード(「!」という1文字を末尾から全部取り除く、という「配列的」な意味)に奪われてしまい、結果が変わってしまう問題が発生した。

- 拡張メソッドの意図: `"12345!!!!".TrimEnd("!!!")` → `"12345!"`(3つの「!」をひとかたまりとして1回だけ除去)
- 新オーバーロードでの結果: `"12345!!!!".TrimEnd("!!!")` → `"12345"`(「!」という文字を末尾からあるだけすべて除去)

この意図しない挙動変化の影響が大きいと判断され、**.NET 9の正式リリース(GA)版では、この新しい`string`引数版オーバーロードは削除され、元の`char[]`ベースの挙動に戻された。**

【放置したときの影響】

このドキュメント自体が想定している読者は主に「.NET 9のプレビュー版でビルドしていた開発者」であり、.NET 8から直接.NET 9/10のGA版へアップデートする場合は、この一時的な期間の挙動を経由しないため、実質的な影響は小さい。ただし、以下の2パターンは押さえておく必要がある。

1. **`str.Trim(';', ',', '.')`のように個々の文字をカンマ区切りで渡すコード**: これは影響なし。GA版では自動的に`char[]`版のオーバーロードにコンパイルされ、以前と同じ挙動になる。
2. **`ReadOnlySpan<char>`のスライス(`someArray.AsSpan(0, 2)`など)を明示的に`Trim`系メソッドに渡しているコード**: GA版ではこのオーバーロードが削除されているため、**コンパイルが通らなくなる**(該当するオーバーロードが見つからないというエラー)。

```csharp
private static readonly char[] s_allowedWhitespace = { ' ', '\t', ' ', ' ' };
str = str.Trim(s_allowedWhitespace.AsSpan(0, 2)); // GA版ではコンパイルエラー
```

なお注記として、.NET 9のPreview 6〜RC2に対してビルドしたアセンブリをGA版の.NET 9/10環境でそのまま実行し続けると、`MissingMethodException`が実行時に発生する可能性があるため、必ず再ビルドが必要、と公式ドキュメントに明記されている。

【プロジェクトでの調べ方】

`.Trim(`、`.TrimStart(`、`.TrimEnd(`でGrepする。実際に調べたところ、dicom-tool-3のC#コードから`.Trim(`/`.TrimStart(`/`.TrimEnd(`の呼び出しは1件もヒットしなかった。**この変更は現時点のdicom-tool-3には影響しない。**

なお、この変更はdicom-tool-3が最初から.NET 9のプレビュー版を経由せず、.NET 8から.NET 10へ直接アップデートする想定であれば、そもそも「以前の動作」の期間を経験しないため、実質的に気にする必要はほぼない。

【改修方法】

対応不要。念のため、既存の自作拡張メソッドで`Trim`/`TrimStart`/`TrimEnd`という名前を使っているものがないか確認しておくと安心(将来また似た問題が起きた場合の予防線として)。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### 空の環境変数をサポート
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/empty-env-variable

【前提知識】

- **環境変数(Environment Variable)とは**
  OS(Windows/Linux/macOS)がプロセスに渡す、キーと値のペアで表される設定情報。`PATH`や、dicom-tool-3のような.NETアプリでよく使われる`ASPNETCORE_ENVIRONMENT`などが代表例。`Environment.SetEnvironmentVariable(key, value)`で現在のプロセスの環境変数を設定・削除でき、`Environment.SetEnvironmentVariable(key, null)`のように`null`を渡すと、その環境変数自体を削除する、という約束事がある。
- **string.Emptyとnullの違いとは**
  `string.Empty`(`""`)は「中身が空の文字列」という有効な値。`null`は「値そのものが存在しない」ことを表す特別な状態。プログラミング全般で、この2つを混同するとバグの元になりやすい。
- **ProcessStartInfo.Environmentとは**
  `System.Diagnostics.Process.Start()`で新しいプロセス(子プロセス)を起動する際、その子プロセスに引き継ぐ環境変数を指定するためのプロパティ。`ProcessStartInfo.Environment["KEY"] = "value"`のように使う。

【説明】

以前は、`Environment.SetEnvironmentVariable("TEST", "")`(空文字列を渡す)と`Environment.SetEnvironmentVariable("TEST", null)`(nullを渡す)が、**どちらも同じ扱い**になっていて、両方とも環境変数`TEST`自体を削除する、という挙動だった。つまり「環境変数の値を空文字列に設定したい」という操作が、そもそも実現不可能だった(多くのOSでは空文字列も有効な環境変数の値のはずなのに)。

`ProcessStartInfo.Environment`/`EnvironmentVariables`側は逆で、空文字列を設定しても`null`を設定しても、どちらも子プロセスの環境変数が「空の値」に設定される(削除はされない)という、`Environment.SetEnvironmentVariable`とは矛盾した挙動になっていた。

.NET 9以降では、この2つのAPIの挙動が整理・統一された。

- `Environment.SetEnvironmentVariable("TEST", string.Empty)` → 環境変数の値が**空文字列に設定される**(削除ではなくなった)
- `Environment.SetEnvironmentVariable("TEST", null)` → 従来通り環境変数が**削除される**(変更なし)
- `ProcessStartInfo.Environment["TEST"] = null` → 環境変数が**削除される**ようになった(以前は空値設定だった)
- `ProcessStartInfo.Environment["TEST"] = string.Empty` → 従来通り**空の値に設定される**(変更なし)

変更理由は、多くのプラットフォームで「値が空文字列の環境変数」自体は有効な状態であるにもかかわらず、これまでの.NET APIではそれを正しく表現・設定できなかったため。

【放置したときの影響】

**「環境変数を空文字列に設定するつもりで`null`を渡していたコード」、または「環境変数を削除するつもりで`string.Empty`を渡していたコード」がある場合に限り影響が出る。** それ以外の一般的な使い方(環境変数を具体的な値に設定する、または`null`を渡して明示的に削除する)には影響しない。

```csharp
// もし「削除したい」という意図で書かれていたなら影響なし
Environment.SetEnvironmentVariable("Jwt__Issuer", null); // .NET 9以降も変わらず削除される

// もし「値を空にしたい」という意図でnullを渡していた場合は要注意
// (.NET 8以前は削除扱いだったが、.NET 9以降も引き続きnullは削除)
```

【プロジェクトでの調べ方】

`SetEnvironmentVariable`、`ProcessStartInfo`でGrepする。実際に調べたところ、`backend/DicomTool.Api.Tests/Infrastructure/DicomToolWebApplicationFactory.cs`(149〜154行目付近)でテスト用のJWT設定値(`Jwt__Issuer`、`Jwt__Audience`、`Jwt__ExpiryMinutes`)を`Environment.SetEnvironmentVariable`で設定しているコードが見つかった。ただしいずれも具体的な文字列値("DicomTool.Api.Tests"や"60"など)を設定しているだけで、`string.Empty`や`null`を意図的に渡しているものではないため、今回の挙動変更の対象外。

`services/DicomTool.TrayApp/Program.cs`(199行目)では`Process.Start(new ProcessStartInfo(timelineUrl) ...)`のように`ProcessStartInfo`を使っているが、これはURLを開くための単純な起動であり、`.Environment`プロパティを操作するコードではない。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

対応不要。念のため、今後環境変数を「削除」したいのか「空文字列に設定」したいのかは、コード上で`null`と`string.Empty`を意図的に使い分けるよう意識するとよい。

```csharp
// 環境変数を削除したい場合
Environment.SetEnvironmentVariable("KEY", null);

// 環境変数の値を空にしたい(削除はしない)場合
Environment.SetEnvironmentVariable("KEY", string.Empty);
```

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）

### ZipArchiveEntry の名前とコメントは UTF8 フラグを尊重する
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/9.0/ziparchiveentry-encoding

【前提知識】

- **文字エンコーディングとZIPファイル名の話**
  ZIPファイルの仕様は歴史的経緯から、エントリ(中に入っているファイル)の名前やコメントをどの文字エンコーディングで解釈すべきかが、実装によってばらつきがあった。今日ではUTF-8を使うのが標準的だが、古いツールでは各国語向けのローカルなコードページ(Windows日本語環境ならShift-JISなど)を使っていることもある。
  ZIPの仕様上、各エントリのヘッダーには「このエントリの名前とコメントはUTF-8でエンコードされている」ことを示す専用のビットフラグ(UTF8ビット)が用意されている。
- **entryNameEncodingパラメータとは**
  `ZipArchive`のコンストラクターに渡せる、「ZIP内のエントリ名・コメントをデコードする際に使う文字エンコーディング」を明示的に指定するためのオプション引数。省略するとシステムの既定のコードページ(.NET Core/.NET系ではUTF-8)にフォールバックする。

【説明】

以前(.NET 7・.NET 8)は、`ZipArchive`をインスタンス化する際にユーザーが`entryNameEncoding`パラメータを明示的に指定していると、そのエントリ自身のUTF8ビットフラグの状態に関わらず、**常に**ユーザー指定のエンコーディングが優先して使われてしまっていた。これは.NET 7で紛れ込んだ回帰(リグレッション、以前は正しく動いていたのに壊れてしまった不具合)であり、ZIP仕様(セクション4.4.4・付録D)に本来沿っていない挙動だった。

.NET 9以降では、この回帰が修正され、**ZIPエントリ自身が持つUTF8ビットフラグが最優先で尊重される**ようになった。UTF8ビットフラグが立っている場合は、ユーザーが`entryNameEncoding`で何を指定していても無視してUTF-8としてデコードする。UTF8ビットフラグが立っていない場合にのみ、ユーザー指定の`entryNameEncoding`(未指定ならシステムの既定コードページ)が使われる。

変更理由は、.NET 7・8で入り込んだ不具合を修正し、ZIPファイル形式の仕様に正しく準拠させるため。

【放置したときの影響】

主に2つのパターンで影響が出る可能性がある。

1. **`ZipArchive`のコンストラクターに独自の`entryNameEncoding`を渡していて、かつUTF8ビットフラグが立っているZIPファイルを扱っている場合**: 以前は指定したエンコーディングが常に優先されていたが、.NET 9以降はUTF8ビットフラグが優先されるため、デコード結果(ファイル名の文字化けの有無など)が変わる可能性がある。
2. **UTF8ビットフラグは立っているのに、実際の中身はUTF-8以外でエンコードされているという「不正な」ZIPファイルを解析している場合**: 以前は(バグのおかげで)`entryNameEncoding`を指定すれば正しく読めていたかもしれないが、.NET 9以降はUTF8ビットフラグを信じてUTF-8としてデコードしようとするため、解析できなくなる(文字化けする)可能性がある。公式ドキュメントも「以前の動作はバグだった」と明言している。

**通常、ZIPファイルの読み書きに`entryNameEncoding`を明示的に指定していなければ、この変更の影響はほぼない。**

【プロジェクトでの調べ方】

`ZipArchive`、`ZipFile.`(項目1で既に確認済み)でGrepする。実際に調べたところ、dicom-tool-3のC#コードから`ZipArchive`関連のAPIは1件もヒットしなかった。**この変更は現時点のdicom-tool-3には影響しない。**

【改修方法】

対応不要(現状ZIP機能を使っていないため)。将来ZIP読み込み機能(例えば複数のDICOMファイルをまとめてダウンロード・アップロードするような機能)を追加し、かつ日本語ファイル名を含むZIPを扱う場合は、この挙動変更(UTF8ビットフラグが優先されること)を踏まえてテストしておくとよい。

【参考記事】
- （公式ドキュメント以外に参考にした記事は特になし）
