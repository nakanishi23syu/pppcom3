## Core .NET ライブラリ

### API の廃止
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/obsolete-apis

【前提知識】

- **「廃止（Obsolete）」とは**
  .NETのクラスやメソッドの中には、「今後は使わないでほしい。もっと良い代わりの方法があります」という意味で印を付けられているものがある。この印は`[Obsolete]`という「属性（Attribute）」という仕組みで表現され、そのAPIを使おうとするとVisual Studioやビルド時に警告（時にはエラー）が出る。
- **`SYSLIBxxxx`という警告番号（診断ID）とは**
  .NETの標準ライブラリ内で「このAPIはもう古い」と警告を出すとき、`SYSLIB0058`のような形式の固有の番号（診断ID）が振られる。これはC#コンパイラの警告のうち、`CS`から始まる番号（例:`CS0618`＝「廃止された部材を使っている」という汎用警告）とは別物で、「このAPI固有の理由」を示すためにMicrosoftが独自に用意している番号。
  - 通常、警告は`#pragma warning disable CS0618`のように「番号を指定して黙らせる（抑制する）」ことができる。しかし`SYSLIBxxxx`系は、CS0618を抑制しても消えない仕組みになっている（後述）。
- **なぜわざわざ「抑制できない」ようにしてあるのか**
  「もう古いAPIだから使うな」という警告を、プロジェクト全体でまとめて`CS0618`を抑制することで、うっかり全部黙らせてしまう事故を防ぐため。`SYSLIBxxxx`という個別の番号にすることで、「この特定のAPIについてだけ、事情があって警告を抑制する」という限定的な対応を強制できる。

【説明】

.NET 10で新たに6つのAPIが「古い」とマークされた。これらは通常の`[Obsolete]`ではなく、それぞれ専用の診断ID（`SYSLIB0058`〜`SYSLIB0062`）付きで警告される。

- 以前の動作: .NET 9以前では、これらのAPI（例:`SslStream.CipherAlgorithm`、`Rfc2898DeriveBytes`の古いコンストラクター、`Queryable.MaxBy`/`MinBy`の一部オーバーロード、`XsltSettings.EnableScript`など）を使ってもビルド警告は出なかった。
- 新しい動作: .NET 10からは、これらを使うとコンパイル時に警告（またはエラー）が出るようになった。
- 変更理由: 古いAPIには「セキュリティ強度が分かりにくい」「非推奨の設計」などの問題があり、利用者に代替APIへの移行を促すため。個別の診断IDにすることで、「一括抑制」ではなく「この1箇所だけ意図して古いAPIを使う」という選択が可能になる。

対象の6つ:

| 診断ID | 内容 |
| --- | --- |
| SYSLIB0058 | `SslStream`の`KeyExchangeAlgorithm`等の古い暗号強度プロパティ→`NegotiatedCipherSuite`を使う |
| SYSLIB0059 | `SystemEvents.EventsThreadShutdown`→`AppDomain.ProcessExit`を使う |
| SYSLIB0060 | `Rfc2898DeriveBytes`の旧コンストラクター→`Rfc2898DeriveBytes.Pbkdf2`を使う |
| SYSLIB0061 | `Queryable.MaxBy`/`MinBy`の`IComparer<TSource>`版→`IComparer<TKey>`版を使う |
| SYSLIB0062 | `XsltSettings.EnableScript` |

【放置したときの影響】

このプロジェクトが対象APIをまったく使っていなければ、ビルド時に何の変化も起きない（警告0件のまま）。

もし使っていた場合は、.NET 10へのアップデート直後に該当箇所でビルド警告（プロジェクト設定によってはエラー）が出るようになる。実行時の挙動自体は変わらないので「動かなくなる」ことはないが、CIで警告をエラー扱いにしている設定（`TreatWarningsAsErrors`）だとビルドが失敗する。

【プロジェクトでの調べ方】

対象6つのAPIそれぞれをGrepで確認した。

```
KeyExchangeAlgorithm / SslStream / Rfc2898DeriveBytes / MaxBy / MinBy / EnableScript / XsltSettings
```

いずれも0件ヒット。dicom-tool-3はDICOM通信・DB登録・ファイル保存が中心で、TLS暗号強度の直接参照、独自のパスワードハッシュ実装、XSLT処理は行っていないため、**現時点でこの変更の影響は受けない**。

【改修方法】

現状は改修不要。念のため、.NET 10へのアップデート後に一度フルビルドしてSYSLIB0058〜0062の警告が出ないことを確認しておくとよい。

もし将来的にパスワードのハッシュ化などで`Rfc2898DeriveBytes`を使うことになった場合の例:

```csharp
// Before（廃止予定のコンストラクター。SYSLIB0060警告が出る）
using var deriveBytes = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
byte[] key = deriveBytes.GetBytes(32);

// After（静的メソッドを使う）
byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### ActivitySource.CreateActivity と ActivitySource.StartActivity の動作変更
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/activity-sampling

【前提知識】

- **分散トレーシング（Distributed Tracing）とは**
  1つのユーザー操作が複数のサービス（このプロジェクトなら`DicomTool.Api`→`DicomTool.Worker`→`DicomTool.DicomScp`のように）をまたいで処理されるとき、「どのリクエストがどのサービスをどんな順番で通ったか」を追跡できるようにする仕組み。ASP.NET CoreやTemporal SDKは内部でこの仕組みを使っており、ログと組み合わせて障害調査などに使われる（OpenTelemetryという業界標準の仕組みで可視化することが多い）。
- **`Activity`と`ActivitySource`とは**
  .NETでは、「1つの処理区間（例:1回のHTTPリクエスト処理、1回のC-STORE処理）」を表すオブジェクトが[Activity](https://learn.microsoft.com/ja-jp/dotnet/api/system.diagnostics.activity)。この`Activity`を作り出す工場役が`ActivitySource`で、`activitySource.StartActivity("処理名")`のように呼ぶと、処理の開始・終了を計測する`Activity`インスタンスが手に入る。
- **サンプリング（Sampling）とは**
  「本番環境で発生する全リクエストのトレース情報を記録すると量が膨大になりすぎる」ため、一部だけを記録対象に選ぶ仕組み。`ActivityListener`という「トレース情報を受け取る側」が、「この`Activity`は記録する/しない」を`ActivitySamplingResult`という列挙型（`None`/`PropagationData`/`AllData`/`AllDataAndRecorded`）で返す。`PropagationData`は「記録はしないが、トレースIDだけは下流に伝える（伝播する）」という中間的な選択肢。

【説明】

- 以前の動作: 親となる`Activity`があり、かつサンプリング結果が`PropagationData`（記録はしないがID伝播だけする）だった場合、なぜか`Activity.Recorded`（記録フラグ）が`true`になっていた。これは`PropagationData`の定義（「記録しない」）と矛盾する不具合だった。
- 新しい動作: .NET 10からは、`PropagationData`のときは`Recorded`が正しく`false`になる。
- 変更理由: 以前の動作はOpenTelemetryの仕様に沿っておらず、バグだったため修正された。

【放置したときの影響】

このプロジェクトのように`ActivityListener`を自作していない（＝OpenTelemetryやAPM製品の既定の設定をそのまま使っている、あるいはトレーシングを何も設定していない）場合は、この変更による挙動の違いはほぼ体感できない。影響が出るのは、独自に`ActivityListener.Sample`を実装して`PropagationData`を返すサンプラーを書いていた場合や、OpenTelemetry .NETでカスタムサンプラーを組んでいた場合のみ。

【プロジェクトでの調べ方】

`ActivitySource`・`ActivityListener`・`new Activity(`をGrepしたが0件。dicom-tool-3では分散トレーシングの独自実装は行っておらず、ASP.NET Core／Temporal SDKが内部的に使っている分にとどまる。**この変更が原因で挙動が変わる可能性はほぼない**。

【改修方法】

改修不要。将来的にOpenTelemetryを導入し、独自のサンプラーを実装する場合は、`PropagationData`を返したときに「記録されない」という前提でコードを書くこと（以前の「実は記録されていた」という抜け道に依存しないこと）。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### Arm64 SVE のノンフォールティング ロードにはマスキングが必要
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/sve-nonfaulting-loads-mask-parameter

【前提知識】

- **CPUの「イントリンシック（Intrinsics）」とは**
  通常、C#のコードはCPUの種類を意識せずに書ける（.NETランタイムがよしなに機械語に変換してくれる）。しかし、`System.Runtime.Intrinsics`名前空間のAPIを使うと、特定のCPUが持つ「SIMD命令」（1回の命令で複数のデータをまとめて処理する高速化命令）を直接呼び出せる。これは主に画像処理・数値計算など、性能が非常にシビアな分野でしか使われない、かなり低レイヤーなAPI。
- **Arm64 SVE（Scalable Vector Extension）とは**
  Armプロセッサ（Apple SiliconのMacやAWS Gravitonなど）が持つ、SIMD命令の一種。「ノンフォールティング ロード（NonFaulting Load）」は、メモリ上の存在しない領域を読もうとしてもエラー（フォールト）を起こさずに読み込みを続けられる特殊な読み込み命令で、配列の末尾を超えて読みに行くような処理を安全に書くために使う。

【説明】

- 以前の動作: `Sve.LoadVector*NonFaulting*`系のAPIは、読み込み開始アドレスだけを受け取っていた。一部の要素だけ読みたい場合は、読み込んだ後に`Sve.ConditionalSelect(mask, 読み込み結果, ゼロ)`で選別する必要があった。
- 新しい動作: .NET 10からは、これらのAPIの第一引数に「どの要素を読むか」を示す`mask`パラメーターが必須になった。
- 変更理由: `ConditionalSelect`で後から選別するやり方だと、CPU内部の「最初にフォールトしたレーンを記録するレジスタ（FFR）」の状態が正しく更新されない。マスクを最初から渡す専用APIにすることでこの問題を回避した。

【放置したときの影響】

このプロジェクトはDICOM画像データの通信・保存を扱うが、Arm64 SVEイントリンシックのような低レイヤーな最適化コードは書いていない。**万が一Arm64 SVEを直接使うコードがあれば、.NET 10でコンパイルエラーになる**（引数の数が合わなくなるため）レベルの重い変更だが、通常のアプリ開発では遭遇しない。

【プロジェクトでの調べ方】

`System.Runtime.Intrinsics`・`Sve.`・`LoadVectorNonFaulting`のようなキーワードでソース全体を検索したが、該当箇所は見当たらなかった（そもそもこのプロジェクトはASP.NET Core Web APIやWinFormsが中心で、SIMDレベルの最適化コードを書く要件がない）。**このプロジェクトには影響しない**。

【改修方法】

改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### BufferedStream.WriteByte が暗黙的フラッシュを実行しなくなりました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/bufferedstream-writebyte-flush

【前提知識】

- **`Stream`と`BufferedStream`とは**
  .NETでファイルやネットワーク越しにバイト列を読み書きする基本の仕組みが`Stream`（`FileStream`、`MemoryStream`、`NetworkStream`など）。ただし、1バイトずつこまめに書き込むと、そのたびにディスクI/Oやネットワーク送信が発生して非常に遅くなることがある。[BufferedStream](https://learn.microsoft.com/ja-jp/dotnet/api/system.io.bufferedstream)は、内部にメモリ上の「バッファー（ためておく場所）」を持ち、ある程度データが溜まってからまとめて書き込む（＝I/O回数を減らす）ラッパークラス。
- **「フラッシュ（Flush）」とは**
  バッファーに溜めていたデータを、実際の書き込み先（ディスクやネットワーク）へ強制的に送り出す操作。`Flush()`メソッドを呼ぶか、`BufferedStream`を`Dispose`（`using`のブロックを抜けるときなど）すると発生する。
- **`WriteByte`とは**
  `Stream`が持つ、「1バイトだけ書き込む」ためのメソッド。`Write(byte[], int, int)`（複数バイトまとめて書く）とは別に用意されている。

【説明】

- 以前の動作: `BufferedStream`の内部バッファーが`WriteByte`の呼び出しでちょうど満杯になると、`BufferedStream`が自動的に（暗黙的に）`Flush`相当の処理を行い、下位のストリームへ書き込んでいた。これは`Write`メソッドなど他の書き込みメソッドには無かった挙動で、`BufferedStream`内で一貫性がなかった。
- 新しい動作: .NET 10からは、`WriteByte`でバッファーが満杯になっても、自動フラッシュは行われなくなった。明示的に`Flush()`を呼ぶか、ストリームを`Dispose`したときだけフラッシュされる。
- 変更理由: `Write`や`WriteAsync`など他のメソッドと動作を統一するため。バラバラだった挙動が原因で、フラッシュのタイミングに依存した予期しないパフォーマンス問題や副作用が起きうるという指摘があった。

【放置したときの影響】

「バッファーが満杯になった瞬間に、下位のストリーム側で何か（ファイルへの反映、ネットワーク送信など）が起きること」に依存したコードがあると、.NET 10ではそのタイミングがズレる（今までより遅く反映される）可能性がある。

具体例:

```csharp
using var fileStream = new FileStream("output.bin", FileMode.Create);
using var bufferedStream = new BufferedStream(fileStream, bufferSize: 4096);

for (int i = 0; i < 10000; i++)
{
    bufferedStream.WriteByte((byte)i);
    // .NET 9以前: バッファーが満杯になるたびに自動でファイルへ反映されていた
    // .NET 10以降: Flush()を呼ぶかDisposeするまでファイルへ反映されない
}
// このループの後、明示的にbufferedStream.Flush()を呼ばずにプロセスが異常終了すると、
// .NET 10ではバッファー内の未フラッシュ分がファイルに残らない可能性がある
```

通常、`using`で最後まで正常にブロックを抜ければ`Dispose`時にフラッシュされるため実害はないが、途中で例外・強制終了した場合や、「他プロセスが書き込み途中のファイルを覗き見る」ような設計だと差が出る。

【放置したときの影響（続き）】

【プロジェクトでの調べ方】

`BufferedStream`でソース全体をGrepしたが0件。dicom-tool-3のファイル保存処理（`SaveToStorageActivity`等）は、DICOMライブラリ（fo-dicom）や`File.WriteAllBytes`/`FileStream`を直接使っており、`BufferedStream`でラップするコードは存在しない。**この変更の影響は受けない**。

【改修方法】

改修不要。将来`BufferedStream.WriteByte`を大量ループで使うコードを書く場合は、ループの後に明示的な`Flush()`を入れる習慣をつけておくとよい。

```csharp
// Before（.NET 9以前は自動フラッシュに頼れた）
BufferedStream bufferedStream = new(new MemoryStream(), bufferSize: 4);
bufferedStream.WriteByte(1);
bufferedStream.WriteByte(2);
bufferedStream.WriteByte(3);
bufferedStream.WriteByte(4);

// After（.NET 10では明示的にFlushする）
BufferedStream bufferedStream2 = new(new MemoryStream(), bufferSize: 4);
bufferedStream2.WriteByte(1);
bufferedStream2.WriteByte(2);
bufferedStream2.WriteByte(3);
bufferedStream2.WriteByte(4);
bufferedStream2.Flush();
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### スパンパラメーターを使用した C# 14 のオーバーロード解決
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/csharp-overload-resolution

【前提知識】

- **`Span<T>`/`ReadOnlySpan<T>`とは**
  配列や文字列の一部（あるいは全部）を、コピーせずに「参照」として扱える型。大きな配列の一部だけ処理したいときなどに、余計なメモリコピーを避けられるパフォーマンス最適化用の型。C#の標準ライブラリの多くのメソッド（`string.Contains`など）には、配列版とスパン版の両方が用意されていることが多い。
- **「オーバーロード解決（Overload Resolution）」とは**
  同じ名前のメソッドが複数の引数の型で定義されている（オーバーロードされている）とき、C#コンパイラが「この呼び出しではどの版を使うか」を自動的に選ぶ処理。例えば`int[]`を渡したときに配列版とスパン版のどちらが呼ばれるかは、このオーバーロード解決のルールで決まる。
- **`Expression<TDelegate>`（式木/Expression Tree）とは**
  C#のラムダ式（`x => x + 1`のような短い関数）を、「実行可能なコード」としてではなく「コードの構造を表すデータ」として扱えるようにする仕組み。LINQ to SQL/Entity Frameworkが、C#のラムダ式を裏でSQLに変換するときなどに使っている。式木は「コンパイルして実行する」（`.Compile()`）ことも、「ツリーを解釈しながら実行する」（インタプリタ的な実行）こともできる。

【説明】

- 以前の動作: C# 13以前では、`ReadOnlySpan<T>`や`Span<T>`を受け取る拡張メソッドは、`T[]`（普通の配列）に対しては「型推論の候補」に入りにくく、実質的に配列を渡したときは配列向けのメソッド（`System.Linq.Enumerable`など）だけが選ばれていた。
- 新しい動作: C# 14（.NET 10に同梱）では、スパンを受け取るメソッド（`System.MemoryExtensions`クラスのメソッドなど）が、配列を渡したときの候補としてより積極的に選ばれるようになった。
- 変更理由: 1つの`ReadOnlySpan<T>`拡張メソッドが配列にもスパンにも両方使えるようになり、API設計・利用が簡素化されるため。

【放置したときの影響】

通常の（式木を使わない）コードでは、この変更によって実行結果が変わることはほとんどない（スパン版・配列版のどちらが選ばれても最終結果は同じになるよう設計されているため）。

影響が出るのは「式木（`Expression<TDelegate>`）を作り、それを`.Compile(preferInterpretation: true)`のように**インタプリタ実行**しているコード」に限られる。この場合、C# 14からはスパン版のメソッド（`MemoryExtensions.Contains`など）にバインドされるようになるが、式木のインタプリタ実行はスパン型の扱いに制限があり、実行時例外が発生する可能性がある。

```csharp
// 式木でContainsを呼ぶケース。C# 14からはMemoryExtensions.Contains（スパン版）に
// バインドされ、e.Compile(preferInterpretation: true)で実行時エラーになりうる
Expression<Func<int[], int, bool>> e = (array, num) => array.Contains(num);
```

【プロジェクトでの調べ方】

`Expression<`と`preferInterpretation`でGrepしたところ、いずれも0件。dicom-tool-3ではLINQ to Entities（Entity Framework Core経由のDBクエリ）は使っているが、`Expression<TDelegate>`を自前で組み立てて`Compile`するようなコードは書いていない。EF Coreは内部で式木をSQLに変換する仕組みを別途持っており、この「インタプリタ実行」の話とは別の話なので影響しない。**このプロジェクトへの影響はない**。

【改修方法】

改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### 一般数学における一貫したシフト動作
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/generic-math

【前提知識】

- **ジェネリック数学（Generic Math）とは**
  .NET 7で導入された仕組みで、`int`・`double`・`decimal`など「数値として扱える型」に共通のインターフェイス（`INumber<T>`、`IShiftOperators<TSelf,TOther,TResult>`など）を用意し、`T`が数値型でありさえすれば、型を問わず同じ計算コード（例:合計を求める関数）を1つ書けば済むようにした機能。あまり一般的なアプリ開発では使わないが、数値計算ライブラリなどで使われる。
- **ビットシフト演算（`<<`, `>>`, `>>>`）とは**
  2進数で表した数値のビット列を左右にずらす演算。例えば`byte`型（8ビット）の値を`<<`（左シフト）で8ビット分ずらすと、元のビットは全部押し出されてしまう（これを「オーバーシフト」と呼ぶ）。C言語系の言語では、シフト量が型のビット幅を超える場合の挙動は歴史的に実装依存でわかりにくい部分があり、C#では通常「シフト量を型のビット幅でマスクする（余りを取る）」という規則で処理される。

【説明】

- 以前の動作: ジェネリック数学経由（`T`を`IShiftOperators<...>`などで制約した汎用コード）で`byte`や`short`のような小さい整数型に対してシフト演算をすると、型によって「シフト量がマスクされる場合」と「されない場合」が混在しており、一貫性がなかった。例えば`byte`型の値を`8`ビット分左シフトすると、期待と異なる結果になることがあった。
- 新しい動作: .NET 10では、すべての組み込み整数型で、必要に応じてシフト量をマスクする（＝仕様として定義された動作に統一する）よう実装が修正された。
- 変更理由: 小さい整数型に対するC#のマスク動作が、ジェネリック数学の実装と食い違っており、設計通りの動作になっていなかったバグ修正。

【放置したときの影響】

このプロジェクトのような一般的な業務アプリ（DICOM通信・DB・ファイル操作が中心）では、ジェネリック数学を使った汎用シフト演算コード自体を書く機会がほぼない。仮に影響が出るとしても、シフト量が型のビット幅を超えるような特殊な計算（例: `byte`を9ビットシフトするなど、通常は書かないコード）に限られる。

```csharp
// ジェネリック数学でシフトを行う例（T: IShiftOperators<T, int, T>）
static T ShiftLeft<T>(T value, int amount) where T : IShiftOperators<T, int, T>
    => value << amount;

// byteのような小さい型に対して、ビット幅を超えるシフト量を渡した場合の挙動が
// .NET 9以前と10で変わりうる（通常のアプリコードでは意図的にこうした呼び出しはしない）
```

【プロジェクトでの調べ方】

`IShiftOperators`・`INumber<`・ジェネリック数学関連のキーワード、および`operator <<`のような記法でソース全体を検索したが該当なし。dicom-tool-3のソースコードには、ジェネリック数学を使った独自の数値計算コードは存在しない。**このプロジェクトへの影響はない**。

【改修方法】

改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### W3C 標準に更新された既定のトレース コンテキスト 伝達子
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/default-trace-context-propagator

【前提知識】

- **「トレース コンテキストの伝達（Propagation）」とは**
  分散トレーシング（前述の「ActivitySource」の項目参照）において、「今処理しているのはどのトレースの一部か」という情報（トレースID、親のID、追加情報など）を、サービス間のHTTPリクエストのヘッダーに載せて次のサービスへ運ぶ仕組み。この「ヘッダーに詰める/取り出す」ロジックを担当するのが`DistributedContextPropagator`。
- **W3C Trace Context標準とは**
  トレース情報をHTTPヘッダーでやり取りするための、業界標準の仕様（`traceparent`ヘッダーなど）。W3Cという標準化団体が定めている。標準化される前は、各ベンダー（Microsoft含む）が独自の形式（レガシー形式）でヘッダーをやり取りしていた。
- **`baggage`（手荷物）とは**
  トレースIDだけでなく、「ユーザーID」のような任意のキー・バリュー情報も一緒に伝播させたいときに使う仕組み。W3Cでは`baggage`という専用ヘッダー名で標準化されているが、.NETのレガシー実装は独自に`Correlation-Context`という非標準のヘッダー名を使っていた。

【説明】

- 以前の動作: `DistributedContextPropagator.CreateDefaultPropagator()`（既定の伝達子を作るメソッド）はレガシー形式の伝達子を返し、`DistributedContextPropagator.Current`（実際に使われるインスタンス）も既定でレガシー版だった。ヘッダー名も非標準の`Correlation-Context`を使っていた。
- 新しい動作: .NET 10からは、既定でW3C標準準拠の伝達子が使われるようになった。標準の`baggage`ヘッダーを使い、フォーマットチェックも厳格になる。
- 変更理由: W3C Trace ContextとBaggageの仕様に完全準拠させるため。

【放置したときの影響】

このプロジェクトはASP.NET Core Web APIとTemporalワーカーで構成されており、ASP.NET Coreは元々内部でトレース伝達の仕組みを使っている。ただし、独自に`DistributedContextPropagator`を触ったり、`Correlation-Context`ヘッダーを直接読み書きするコードがなければ、通常は挙動の違いを意識することはない。

影響が出うるのは、「自社の複数サービス間で、独自に旧形式の`Correlation-Context`ヘッダーを解釈するミドルウェアなどを書いていた」場合や、「外部の監視ツール（APM製品）が旧形式のヘッダーを前提にしていた」場合。この場合、.NET 10にアップデートした瞬間にヘッダー形式が変わり、トレースの連携が途切れる可能性がある。

【プロジェクトでの調べ方】

`DistributedContextPropagator`・`Correlation-Context`・`baggage`でソース全体をGrepしたが該当なし。dicom-tool-3では分散トレーシングの伝達方式を独自に実装しておらず、ASP.NET Coreの既定の仕組みに任せている。**この変更で挙動が変わったとしても、コード側の対応は不要**（トレース収集基盤を別途導入する場合のみ、そちらの互換性を確認すればよい）。

【改修方法】

改修不要。もし過去のトレースデータとの互換性のために旧形式が必要になった場合は、アプリ起動時に以下を1回呼ぶだけで従来の動作に戻せる。

```csharp
// 従来のレガシー伝達子（Correlation-Contextヘッダー）に戻す場合
DistributedContextPropagator.Current = DistributedContextPropagator.CreatePreW3CPropagator();
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### DriveInfo.DriveFormat は Linux ファイルシステムの種類を返します
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/driveinfo-driveformat-linux

【前提知識】

- **`DriveInfo`クラスとは**
  「Cドライブ」「Dドライブ」（Windowsの場合）や、Linuxのマウントポイント（例:`/`や`/mnt/data`）といった「ドライブ/ファイルシステム」の情報（総容量、空き容量、フォーマット形式など）を取得するための.NET標準クラス。dicom-tool-3では、保存先ストレージの空き容量チェックに使われている。
- **`DriveFormat`プロパティとは**
  そのドライブが「どんなファイルシステム形式でフォーマットされているか」を表す文字列を返すプロパティ。Windowsなら`"NTFS"`、Linuxなら`"ext4"`のような値になる。
- **Linuxカーネルのファイルシステム識別子とは**
  Linuxカーネル内部では、`ext2`/`ext3`/`ext4`のような具体的な種類を区別するのではなく、内部の「マジックナンバー（特定の数値）」で管理している部分があり、.NETの実装ではこれを人間が読める文字列に変換する処理が入っている。

【説明】

- 以前の動作: .NETは、Linuxのファイルシステムのマジックナンバーを文字列に変換する際、複数の異なるファイルシステム種別が同じマジックナンバーを共有しているケースを区別できていなかった（例:`ext3`と`ext4`を区別できない、`cgroup`と`cgroup2`をどちらも`cgroupfs`/`cgroup2fs`と返す、SELinuxのファイルシステムを`selinux`と返すなど、実際のLinuxカーネルが使う名称と微妙に違っていた）。
- 新しい動作: .NET 10からは、Linuxカーネルが実際に使っている文字列表現（例:SELinuxなら`selinuxfs`、cgroupなら`cgroup`/`cgroup2`）をそのまま返すようになり、より正確・詳細になった（例:`ext3`と`ext4`を区別できるようになった）。
- 変更理由: より正確なファイルシステム種別の情報を提供するため。

【放置したときの影響】

`DriveFormat`プロパティの返す**文字列の内容**が変わるだけで、`DriveInfo`自体が使えなくなるわけではない。もし`DriveFormat`の戻り値を文字列で厳密に比較・分岐しているコード（例:`if (drive.DriveFormat == "selinux")`）があれば、Linux環境で.NET 10に上げた途端にその分岐が働かなくなる（`"selinuxfs"`に変わるため）可能性がある。Windows環境では通常この変更の影響はない（Windowsのファイルシステム名の扱いは別ロジック）。

【プロジェクトでの調べ方】

`DriveInfo`・`DriveFormat`でGrepすると、以下の2箇所がヒットした。

- `services/DicomTool.Worker/Activities/CheckStorageCapacityActivity.cs`（コメント内で言及）
- `services/DicomTool.StorageGuard/Program.cs`（実際に`DriveInfo`を生成し、容量情報を返すエンドポイントで使用）

`DicomTool.StorageGuard/Program.cs`の該当箇所を読むと、`new DriveInfo(...)`を作った後に使っているのは`drive.TotalSize`と`drive.TotalFreeSpace`（容量まわりのプロパティ）のみで、`DriveFormat`プロパティ自体は参照していなかった。つまり**`DriveInfo`は使っているが、この変更の対象である`DriveFormat`は使っていないため、この変更の影響は受けない**。将来「保存先ストレージのファイルシステム種別に応じて挙動を変える」といった機能を追加する場合は、この変更を踏まえて実装する必要がある。

【改修方法】

現状は改修不要。将来`DriveFormat`を使う場合は、文字列の完全一致比較に頼らず、Linux/Windowsそれぞれの実際の値を確認してから実装すること。

```csharp
// 将来DriveFormatで分岐を書く場合の例（Linux上でSELinuxファイルシステムを判定したいケース）
// Before（.NET 9以前の値を前提にした古い判定。.NET 10では真になりにくい）
if (drive.DriveFormat == "selinux") { /* ... */ }

// After（.NET 10のLinuxカーネル準拠の値に合わせる）
if (drive.DriveFormat == "selinuxfs") { /* ... */ }
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### DefaultValueAttribute のコンストラクタから DynamicallyAccessedMembers 注釈が削除されました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/defaultvalueattribute-dynamically-accessed-members

【前提知識】

- **トリミング（Trimming）とは**
  .NETアプリを発行（Publish）するとき、「実際に使われていないコード（クラス・メソッド）」をビルド成果物から削除して、実行ファイルのサイズを小さくする最適化機能。ただし、リフレクション（後述）で実行時に動的に呼び出されるコードは、ビルド時には「使われているかどうか」が判断できないため、誤って削除されてしまう危険がある。
- **リフレクション（Reflection）と`DynamicallyAccessedMembersAttribute`とは**
  リフレクションは、型名や文字列をもとに、実行時に動的にクラスやメソッドを呼び出す仕組み（例:`Type.GetType("SomeNamespace.SomeClass")`）。トリミング機能はこれを静的に解析できないため、「このパラメーター/プロパティが表す型の、こういうメンバーはリフレクションで使うかもしれないので消さないで」と目印を付けるための属性が`[DynamicallyAccessedMembers(...)]`。
- **`DefaultValueAttribute`とは**
  プロパティなどに「既定値はこれです」という情報をメタデータとして付与するための属性（`System.ComponentModel`名前空間）。主にWinFormsのプロパティグリッドや、一部のシリアライザーなどが参照する。

【説明】

- 以前の動作: `DefaultValueAttribute(Type, String)`という、文字列から値を型変換して既定値を作るコンストラクターに`[DynamicallyAccessedMembers]`の注釈が付いていた。しかしこのコンストラクターは実はトリミングに対応しておらず、トリミングされたアプリで実際に呼び出すと例外が発生する作りだった。それでも、トリミング警告を無効化する機能スイッチを使っていると、警告が出ないまま「実行時に動くかもしれない」誤解を招く状態だった。
- 新しい動作: .NET 10では、この不正確な注釈が削除された。結果として、トリミングされたアプリでこのコンストラクターを使うと、以前よりも確実にトリミング警告が出るようになった（実行時に動く可能性はむしろ低くなった＝より正直な状態になった）。
- 変更理由: この属性はトリミングされたアプリで確実に動作しない設計であり、誤解を招く注釈を残しておくべきではないため。

【放置したときの影響】

このプロジェクトはASP.NET Core Web API・Temporalワーカー・WinFormsトレイアプリで構成されているが、通常の運用（`dotnet run`や通常の`dotnet publish`）ではトリミング機能自体が有効になっていない限り関係ない。トリミング（`PublishTrimmed`）は主に自己完結型の小型実行ファイルを作るときに使うオプションで、既定では無効。

`DefaultValueAttribute(Type, string)`コンストラクターを直接使っているコードがあり、かつ将来的にトリミング発行を有効にした場合にのみ、警告や実行時例外のリスクが顕在化する。

【プロジェクトでの調べ方】

`DefaultValueAttribute`でGrepしたところ0件。各`.csproj`にも`PublishTrimmed`の設定は見当たらない。**dicom-tool-3は現状この属性を使っておらず、トリミング発行も行っていないため、この変更の影響はない**。

【改修方法】

改修不要。将来トリミング発行を検討する場合は、`DefaultValueAttribute(Type, string)`のような「文字列から型変換する」コンストラクターの使用を避け、既定値を直接指定できるオーバーロード（例:`DefaultValueAttribute(string)`など、型変換を伴わないもの）を使うこと。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### InlineArray で許可されていない明示的な構造体サイズ
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/inlinearray-explicit-size-disallowed

【前提知識】

- **構造体（`struct`）とは**
  C#の値型。クラス（`class`、参照型）と違い、変数に代入するときにデータそのものがコピーされる（参照ではない）。値型はスタック上または他のオブジェクトの中に直接埋め込まれるため、小さいデータをまとめて高速に扱いたいときに使われる。
- **`InlineArrayAttribute`とは**
  C# 12から追加された機能で、構造体に`[InlineArray(要素数)]`という属性を付けると、その構造体を「固定長の配列のように扱える型」に変換できる、という比較的新しい低レイヤーの機能。ヒープにアロケーションされる普通の配列（`T[]`）と違い、構造体の中に直接データが並ぶため高速。ゲームエンジンや高性能な数値計算ライブラリなどで使われることが多い。
- **`StructLayoutAttribute`の`Size`とは**
  構造体のメモリ上のサイズをバイト単位で明示的に指定するための、さらに低レイヤーな属性（`[StructLayout(LayoutKind.Explicit, Size=32)]`のように使う）。通常のアプリ開発コードではまず使わない。

【説明】

- 以前の動作: `[InlineArray(8)]`が付いた構造体に、さらに`[StructLayout(..., Size=32)]`のように明示的なサイズ指定を重ねて付けることが許可されていた。しかしこの組み合わせの実際の挙動は「実装依存」で、開発者の期待通りに動くとは限らない、あいまいな状態だった。
- 新しい動作: .NET 10からは、この組み合わせ自体が禁止された。このような構造体のインスタンスを作ろうとすると`TypeLoadException`（型の読み込みに失敗したという例外）が発生する。
- 変更理由: `InlineArray`に明示的な`Size`を指定する意味自体があいまいで、仕様と矛盾する解釈を生んでいたため。

【放置したときの影響】

`InlineArrayAttribute`自体、通常の業務アプリ開発（Web API・DBアクセス・DICOM通信など）ではまず使わない、かなり専門的な最適化用の機能。dicom-tool-3のようなプロジェクトでこの組み合わせを使っているコードがあれば、.NET 10へのアップデート直後にそのクラスを初めて使った瞬間に`TypeLoadException`で落ちる（重い影響）が、そもそも使っていなければ無関係。

【プロジェクトでの調べ方】

`InlineArray`でソース全体をGrepしたが0件。dicom-tool-3のコードには`InlineArrayAttribute`を使った構造体は存在しない。**この変更の影響はない**。

【改修方法】

改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### FilePatternMatch.Stem が非 null 許容に変更されました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/filepatternmatch-stem-nonnullable

【前提知識】

- **`Microsoft.Extensions.FileSystemGlobbing`とは**
  「`**/*.dcm`のような、ワイルドカードを使ったファイルパターン（glob パターン）でファイルを検索する」ための.NET標準ライブラリ。`Matcher`クラスなどを使って、指定フォルダ配下から条件に合うファイル一覧を取得できる。ASP.NET CoreのSDKの一部でも内部的に使われている。
- **`FilePatternMatch`と`Stem`とは**
  `Matcher`で検索した結果、1件のマッチしたファイルを表すのが`FilePatternMatch`。`Stem`プロパティは、パターンのうちワイルドカード部分にマッチした「相対パスの残り部分」を表す文字列（例:パターン`**/*.dcm`に対して`sub/file1.dcm`がマッチしたときの`**`部分に相当する`sub/file1.dcm`）。
- **null許容参照型（Nullable Reference Types）とは**
  C# 8以降の機能で、`string`型の変数に`null`が入りうるかどうかをコンパイラが型上でチェックしてくれる仕組み。`string?`と書けば「nullかもしれない」、`string`（`?`なし）と書けば「nullではないはず」という約束をコンパイラに伝える。この項目は、`Stem`プロパティの「nullかもしれない」という約束の付け方（注釈）が変わった、という話。

【説明】

- 以前の動作: `FilePatternMatch`のコンストラクターは、`stem`引数に`null`を渡してもコンパイル時の警告なく通っていた。それに合わせて`Stem`プロパティも「nullかもしれない」（`string?`）として注釈されていた。しかしこれは実態と合っておらず、「マッチが成功した場合は`Stem`は絶対に`null`にならない」という実際の仕様を正しく表現できていなかった。
- 新しい動作: .NET 10からは、`FilePatternMatch`のコンストラクターに`null`を渡すと**コンパイル時警告**が出るようになり、実行時には`ArgumentNullException`（引数が`null`だったという例外）がスローされるようになった。`Stem`プロパティも「マッチが成功していれば`null`にならない」ことを`[MemberNotNullWhen]`という注釈で正確に表現するようになった。
- 変更理由: 以前の`null`許容の注釈が不正確で、実態（`stem`に`null`を渡すことは実質想定されていない）とズレていたのを是正するため。

【放置したときの影響】

このライブラリ（`Microsoft.Extensions.FileSystemGlobbing`）を直接使って`FilePatternMatch`を自前で組み立てているコード（`new FilePatternMatch(path, null)`のように呼んでいる箇所）があれば、.NET 10へのアップデート後にコンパイル警告が出て、実行時には`ArgumentNullException`で落ちるようになる。一方、`Matcher.Execute(...)`のようにライブラリが内部で`FilePatternMatch`を生成してくれるだけの一般的な使い方（自分で`new FilePatternMatch(...)`しない使い方）であれば、影響はほぼない。

```csharp
// これを自前で書いていた場合は影響がある（.NET 10ではArgumentNullExceptionになる）
var match = new FilePatternMatch("path/to/file.txt", null);
```

【プロジェクトでの調べ方】

`FileSystemGlobbing`・`Matcher(`・`FilePatternMatch`でGrepしたが該当箇所は0件。dicom-tool-3のファイル検索処理（保存先フォルダの走査など）は、`Directory.GetFiles`や`Directory.EnumerateFiles`をシンプルに使っており、`Microsoft.Extensions.FileSystemGlobbing`ライブラリ自体を利用していない。**このプロジェクトへの影響はない**。

【改修方法】

改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### GnuTarEntry と PaxTarEntry に既定では atime と ctime が含まれなくなりました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/tar-atime-ctime-default

【前提知識】

- **`System.Formats.Tar`とは**
  .NET 7から追加された、Linux/Unix系でよく使われる`.tar`アーカイブ形式（複数ファイルを1つにまとめる形式。`.tar.gz`のように圧縮と組み合わせて使うことが多い）を読み書きするための標準ライブラリ。`TarWriter`（書き込み）・`TarReader`（読み込み）・`GnuTarEntry`/`PaxTarEntry`（tarフォーマットのバリエーションごとのエントリ表現クラス）などから構成される。
- **`atime`（アクセス時刻）・`ctime`（変更時刻＝メタデータの変更時刻）とは**
  Unix系ファイルシステムがファイルごとに管理する複数のタイムスタンプの一部。`mtime`（内容の最終更新時刻。おなじみの「更新日時」）とは別に、「最後にアクセス（読み取り）された時刻」が`atime`、「パーミッションなどメタデータが変更された時刻」が`ctime`。tarアーカイブのGNU形式やPAX形式は、これらのタイムスタンプもオプションで保存できる。
- **`GnuTarEntry`/`PaxTarEntry`とは**
  tarフォーマットには複数の方言（V7、ustar、GNU、PAX）があり、`System.Formats.Tar`ではそれぞれに対応するエントリクラスが用意されている。`GnuTarEntry`と`PaxTarEntry`は、GNU tar・PAX形式それぞれに対応する、拡張フィールド（`atime`/`ctime`含む）を持てるエントリクラス。

【説明】

- 以前の動作: `GnuTarEntry`や`PaxTarEntry`を**新規作成**すると、常に`atime`・`ctime`の値が自動的に付与されていた。
- 新しい動作: .NET 10からは、新規作成時に`atime`・`ctime`は自動付与されなくなった。「既存のtarアーカイブから読み込んだエントリで、元々これらのフィールドが含まれていた場合」または「利用者が明示的にプロパティで設定した場合」のみ値が入る。なお`ModificationTime`（更新時刻/`mtime`相当）はこれまで通り自動設定される。
- 変更理由: 一部のtarリーダー（他社製ツールなど）が`atime`/`ctime`フィールドをサポートしておらず、互換性の問題を起こすことがあった。また、「読み込んだtarをそのまま書き戻したときに、余計な変更が入らないようにする（ラウンドトリップの改善）」という目的もある。

【放置したときの影響】

dicom-tool-3がtarアーカイブの作成・読み込みを行っていなければ無関係。もし行っている場合、tar内のファイルの`atime`/`ctime`情報を後続処理で読み取って何かを判定しているようなコードがあると、.NET 10で新規作成したtarにはこれらの値が入らなくなり、判定がすり抜ける可能性がある（ただし多くのユースケースでは`atime`/`ctime`は使われず、`mtime`だけで十分なことが多い）。

【プロジェクトでの調べ方】

`System.Formats.Tar`・`TarWriter`・`TarReader`・`GnuTarEntry`・`PaxTarEntry`でGrepしたが該当なし。dicom-tool-3はDICOMファイル（`.dcm`）を個別またはフォルダ単位でファイルシステムに保存する設計で、tarアーカイブへの圧縮・展開機能自体を持っていない。**このプロジェクトへの影響はない**。

【改修方法】

改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### LDAP DirectoryControl の解析がより厳格に
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/ldap-directorycontrol-parsing

【前提知識】

- **LDAP（Lightweight Directory Access Protocol）とは**
  組織内のユーザー・グループ情報などを管理する「ディレクトリサービス」（代表例:Active Directory）に問い合わせるための通信プロトコル。.NETでは`System.DirectoryServices.Protocols`名前空間の`LdapConnection`などを使ってLDAPサーバーと通信する。
- **`DirectoryControl`とBERエンコーディングとは**
  LDAP通信では、リクエスト/レスポンスに追加のオプション情報（「コントロール」と呼ぶ）を付加できる。この情報は`ASN.1`という仕様に基づいた`BER`（Basic Encoding Rules）というバイナリ形式でエンコードされる。`System.DirectoryServices.Protocols.DirectoryControl`はこの「コントロール」をC#オブジェクトとして扱うためのクラス。
- **`BerConverter`とは**
  BER形式のバイナリと、C#のオブジェクトを相互変換するためのクラス。以前はOSが提供するBER解析機能（Windowsのネイティブライブラリなど）を使っていたが、.NETがマネージド（プラットフォーム非依存のC#）コードで再実装した、という変更が背景にある。

【説明】

- 以前の動作: BERデータの解析がかなり緩く、「タグ（データの種類を示す識別子）を検証しない」「末尾に余分なデータがあっても無視する」「無効なUTF8バイト列でも例外を出さず、不正な文字を別の文字に自動的に置き換えて処理していた」など、仕様（RFC）に厳密でない部分があった。またOSによって挙動差もあった（例: 長さ0の文字列を`null`として返すか空文字列として返すかがWindowsのバージョンによって違った）。
- 新しい動作: .NET 10からは、ASN.1タグの検証、末尾データの禁止、長さチェック、無効なUTF8バイト列に対する例外送出など、RFC・仕様によりいっそう厳密に準拠するようになった。プラットフォーム間の挙動の違いもなくなった。
- 変更理由: RFC（インターネット標準の仕様書）への準拠。特にサーバー側が仕様に沿わない不正なデータを送ってきた場合に、以前は黙って（不正確なまま）処理していたのを、きちんとエラーとして検知できるようにするため。

【放置したときの影響】

`System.DirectoryServices.Protocols`（LDAP通信）を使っていなければ完全に無関係。使っている場合でも、通信相手のLDAPサーバー（Active Directoryなど）がRFCに準拠していれば挙動は変わらない。相手サーバーが仕様から外れたデータ（無効なUTF8バイト列や、余分な末尾データなど）を返してくる場合にのみ、.NET 10では例外がスローされるようになる（＝以前は気づかず動いていた不具合が顕在化する）。

【プロジェクトでの調べ方】

`DirectoryServices`・`LdapConnection`・`DirectoryControl`でGrepしたが該当なし。dicom-tool-3はDICOM通信（C-ECHO/C-STORE/C-FIND/C-MOVE）とREST API・DB操作が中心であり、Active Directory等のディレクトリサービスとの連携機能は存在しない（ユーザー認証まわりも本プロジェクトの範囲では別方式）。**このプロジェクトへの影響はない**。

【改修方法】

改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### MacCatalyst バージョンの正規化
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/maccatalyst-version-normalization

【前提知識】

- **MacCatalystとは**
  Appleが提供する技術で、iPad向けに作ったアプリをmacOS上でも動かせるようにする仕組み。.NET MAUI（.NETのクロスプラットフォームUIフレームワーク）でMac向けアプリをビルドするときのターゲットの1つとして登場する。dicom-tool-3のようなWindows専用のWinFormsトレイアプリやASP.NET Core Web APIとは無関係な、Apple製品向けの技術。
  - なお、「MacCatalyst」という名前と「LDAP DirectoryControl」の話とは関係がなく、この項目単体でApple関連の話であることに注意（当プロジェクトはASP.NET Core / WinForms / Temporalワーカーのみで構成されている）。
- **`OperatingSystem.IsMacCatalystVersionAtLeast`とは**
  「現在動作しているMacCatalystのバージョンが指定した値以上かどうか」を判定するAPI。「メジャー.マイナー.ビルド」のようなバージョン番号を比較することで、OS機能の対応可否を判定するのに使う。

【説明】

- 以前の動作: MacCatalystのバージョン情報（`Version`型）のうち、「ビルド」コンポーネントが正規化されておらず、メジャー・マイナーの2つしか情報がない場合にバージョン比較が正しく行われないことがあった。
- 新しい動作: .NET 10からは、メジャー・マイナー・ビルドの3コンポーネントに常に正規化される（未定義のビルド番号は`0`扱い、リビジョンは常に`-1`扱い）ようになり、iOSバージョンとの一貫性が取れるようになった。
- 変更理由: 不正確なバージョン判定を防ぎ、iOSとMacCatalystのバージョン管理方式を揃えるため。

【放置したときの影響】

dicom-tool-3はWindows専用のWinFormsトレイアプリとASP.NET Core Web API・Temporalワーカー・DICOM通信サービスで構成されており、macOS/MacCatalyst向けのビルドターゲットは存在しない。**この変更は完全に無関係**。

【プロジェクトでの調べ方】

`MacCatalyst`でソース全体・全`.csproj`をGrepしたが該当なし。各サービスのターゲットフレームワークは`net10.0`または`net10.0-windows`（`DicomTool.TrayApp`）のみで、`net10.0-maccatalyst`のようなターゲットは1つも存在しない。**このプロジェクトへの影響はない**。

【改修方法】

改修不要。

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### .NET ランタイムで既定の終了シグナル ハンドラーが提供されなくなりました
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/sigterm-signal-handler

【前提知識】

- **プロセスの終了シグナルとは**
  Linux/UnixのOSは、実行中のプロセスに対して「終了してほしい」という通知を「シグナル」という仕組みで送る。代表的なのが`SIGTERM`（穏便に終了してほしい、というお願い）や`SIGKILL`（問答無用で強制終了、こちらは無視できない）。Dockerコンテナを`docker stop`で止めるときも、まず`SIGTERM`が送られ、一定時間後に反応がなければ`SIGKILL`される、という流れになる。Windowsには厳密にはシグナルという概念はないが、コンソールアプリの終了イベント（`CTRL_SHUTDOWN_EVENT`/`CTRL_CLOSE_EVENT`）が近い役割を持つ。
- **グレースフル シャットダウン（Graceful Shutdown）とは**
  終了通知を受け取った際に、いきなりプロセスを強制終了するのではなく、「今処理中のリクエストを終わらせる」「DB接続を閉じる」「未保存のデータをフラッシュする」といった後始末をしてから終了すること。ASP.NET CoreのWebサーバーやTemporalワーカーのようにDBやネットワーク接続を持つプロセスでは、この後始末が重要になる。
- **`AppDomain.ProcessExit`イベントと`Generic Host`(`IHost`)とは**
  .NETには、「プロセスが終了する直前に呼ばれるイベント」として`AppDomain.ProcessExit`がある。ASP.NET Coreや`Microsoft.Extensions.Hosting`パッケージが提供する「Generic Host」（`WebApplication.CreateBuilder`や`Host.CreateApplicationBuilder`で作るホスト。dicom-tool-3の全サービスがこれを使っている）は、内部でこの終了シグナルを検知し、登録済みの`IHostedService`（バックグラウンドサービス）に「後始末をする時間」を与えてから終了する仕組み(`UseConsoleLifetime`)を標準で持っている。

【説明】

- 以前の動作: .NETランタイム自身が、Unix系OSでの`SIGTERM`（Windowsでは`SIGTERM`/`SIGHUP`相当のシグナル）に対して既定のハンドラーを登録し、それによって`AppDomain.ProcessExit`や`AssemblyLoadContext.Unloading`イベントを発火させてから、アプリケーションを穏便に終了させていた。
- 新しい動作: .NET 10からは、.NETランタイムはこの既定ハンドラーの登録をやめた。結果として、OS標準の終了シグナルハンドラー（何もしなければ即座にプロセスを終了させる）がそのまま使われるようになり、`AppDomain.ProcessExit`や`AssemblyLoadContext.Unloading`イベントは（何もしなければ）発火しなくなった。
- 変更理由: ランタイムが常に同じ終了ハンドラーを登録してしまうと、「コンソールアプリ」「コンテナー」「Windowsサービス」など、アプリの種類ごとに求められる終了処理の粒度が違うのに対応しきれない（例えばWindowsサービスでは.NETランタイムの既定ハンドラーとは異なる仕組みが必要）。「アプリの種類に応じたシグナル処理は、より上位のライブラリ（ASP.NET CoreのGeneric Hostなど）やアプリコード側に任せるべき」という設計判断による。

【放置したときの影響】

**ASP.NET CoreやGeneric Hostを使っている一般的なアプリ（このプロジェクトの全サービスが該当）では、公式ドキュメントでも「アクション不要」と明記されている。** Generic Host（`WebApplication.CreateBuilder`/`Host.CreateApplicationBuilder`）は`UseConsoleLifetime`を通じて、自前で必要なシグナルハンドラーを登録する仕組みを既に持っているため、この変更の直接の影響を受けない。

一方、Generic Hostを使わずに、自分で`Main`メソッドの中でループを回すだけのシンプルなコンソールアプリを書いていた場合は影響が出る。以前は何もしなくても`SIGTERM`受信時に`ProcessExit`イベントが発火してくれていたが、.NET 10ではそれに頼れなくなり、シグナルを受けた瞬間に何の後始末もなくプロセスが終了してしまう。

```csharp
// Generic Hostを使わない素朴なコンソールアプリの例（.NET 9以前は暗黙にSIGTERMをフックしてくれていた）
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    Console.WriteLine("後始末をしています...");
    // DB接続のクローズなど
};

while (true)
{
    // 何か常駐処理
}
// .NET 10では、docker stop等でSIGTERMが送られてもProcessExitが発火せず、
// 「後始末をしています...」が出力されないままプロセスが終了する可能性がある
```

【プロジェクトでの調べ方】

`DicomTool.Worker/Program.cs`を確認したところ、`Host.CreateApplicationBuilder(args)`でGeneric Hostを構築し、最後に`await host.RunAsync();`でブロックする構成になっている（コメントにも「Ctrl+C(SIGINT)やコンテナのSIGTERM等でGraceful Shutdownがかかるまでブロックし続ける」と明記されている）。他の全サービスの`Program.cs`も同様に確認した。

| サービス | エントリポイントの構築方法 |
| --- | --- |
| `backend/DicomTool.Api` | `WebApplication.CreateBuilder(args)` |
| `services/DicomTool.DicomScp` | `WebApplication.CreateBuilder(args)` |
| `services/DicomTool.StorageGuard` | `WebApplication.CreateBuilder(args)` |
| `services/DicomTool.Worker` | `Host.CreateApplicationBuilder(args)` |
| `services/DicomTool.TrayApp` | `WebApplication.CreateBuilder()`(内部でWebホストを起動) |

すべてのサービスがGeneric Host（`WebApplication`または`Host`）を経由しており、独自に「素のコンソールアプリでシグナルだけ待つ」ようなコードは存在しなかった。`AppDomain.ProcessExit`や`PosixSignalRegistration`を直接使っている箇所も0件。**したがってこのプロジェクトは、この変更に対して基本的にアクション不要**（Generic Hostが内部で面倒を見てくれる）。

【改修方法】

改修は基本的に不要。ただし、.NET 10へアップデートした後は、実際にDockerコンテナ上や本番相当の環境で「`docker stop`（＝SIGTERM送信）できちんとログが出て後始末が行われるか」を一度手動で確認しておくと安心。

もし将来、Generic Hostを使わない単純なコンソールツール（例:`tools/`配下のようなスクリプト的なプログラム）で終了処理が必要になった場合は、以下のように自分で明示的にハンドラーを登録する。

```csharp
// Generic Hostを使わないシンプルなツールで、SIGTERM時の後始末を自前で行いたい場合
using var termSignalRegistration = PosixSignalRegistration.Create(
    PosixSignal.SIGTERM,
    context =>
    {
        Console.WriteLine("後始末をしています...");
        Environment.Exit(0);
    });

// アプリ本体の処理
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）

### コア ライブラリ に含まれる System.Linq.AsyncEnumerable
リンク：https://learn.microsoft.com/ja-jp/dotnet/core/compatibility/core-libraries/10.0/asyncenumerable

【前提知識】

- **`IAsyncEnumerable<T>`とは**
  「非同期に、1件ずつ順番に値を返してくるコレクション」を表すインターフェイス。通常の`IEnumerable<T>`（`foreach`で1件ずつ取り出せるコレクション）の非同期版で、`await foreach`構文で使う。例えば「DBから大量の行を少しずつ取得しながら処理したい」「ネットワークから逐次届くデータを処理したい」といった場面で使われる。dicom-tool-3では、DICOM通信のC-FIND（検索）・C-MOVE（転送）応答が「1件ずつ複数回返ってきて、最後に完了通知が来る」という形なので、`IAsyncEnumerable<T>`で表現されている（`DicomScpService.cs`の`OnCFindRequestAsync`/`OnCMoveRequestAsync`）。
- **LINQ（`Select`/`Where`など）とは**
  コレクションに対して「変換する（`Select`）」「絞り込む（`Where`）」といった操作をメソッドチェーンで書ける.NET標準の仕組み。普通の`IEnumerable<T>`には標準で`System.Linq.Enumerable`クラスがLINQメソッドを提供しているが、`IAsyncEnumerable<T>`には長らく標準のLINQメソッドが用意されておらず、コミュニティが提供する`System.Linq.Async`という非公式（準標準ではあるが非Microsoft管理）のNuGetパッケージがこの隙間を埋めていた。

【説明】

- 以前の動作: `IAsyncEnumerable<T>`に対して`Select`や`Where`のようなLINQ操作をしたい場合、`System.Linq.Async`というコミュニティ管理のNuGetパッケージを別途インストールする必要があった。
- 新しい動作: .NET 10で、Microsoft公式の`System.Linq.AsyncEnumerable`クラスが標準ライブラリに追加され、`IAsyncEnumerable<T>`向けのLINQメソッド一式が標準で使えるようになった。もし`System.Linq.Async`パッケージを引き続き参照していると、同名のメソッド（例:`Select`）が標準ライブラリ側とパッケージ側の両方に存在することになり、「どちらを使うか一意に決まらない」というあいまいさによるコンパイルエラーが起きる可能性がある。
- 変更理由: `IAsyncEnumerable<T>`は広く使われる基本的なインターフェイスであるため、LINQサポートをプラットフォーム（.NET本体）自身が提供すべきだという声（`System.Linq.Async`のメンテナ自身からの要望を含む）に応えたもの。

【放置したときの影響】

`System.Linq.Async`パッケージへの直接参照がなければ、この変更によって既存コードが壊れることはない（新しい標準クラスが追加されるだけ）。もし直接参照していた場合、.NET 10へアップデートした後に、`Select`のような共通メソッド名の呼び出し箇所で「どちらのSelectか一意に決まらない」というあいまいさのコンパイルエラーが起きる可能性がある。

```csharp
// System.Linq.Asyncパッケージへの直接参照が残っている状態で.NET 10にアップデートすると、
// 標準ライブラリのSystem.Linq.AsyncEnumerable.Selectと衝突し、あいまいさのエラーになりうる
await foreach (var x in asyncSource.Select(i => i * 2))
{
    // ...
}
```

【プロジェクトでの調べ方】

まず`System.Linq.Async`パッケージへの参照を全`.csproj`でGrepしたが0件（そもそも参照していない）。

次に`IAsyncEnumerable`の使用箇所をGrepしたところ、`services/DicomTool.DicomScp/Services/DicomScpService.cs`の`OnCFindRequestAsync`/`OnCMoveRequestAsync`メソッドで、C-FIND/C-MOVE応答を`async IAsyncEnumerable<...>`として`yield return`する形で使われていた（fo-dicomライブラリの`IDicomCFindProvider`/`IDicomCMoveProvider`インターフェイスの実装として必須の型）。ただし、この`IAsyncEnumerable<T>`に対して`Select`/`Where`のようなLINQ拡張メソッドを呼んでいる箇所は見当たらず（`.SelectAwait`等のキーワードも0件）、`await foreach`で単純に列挙しているだけの使い方だった。

**したがって、「`IAsyncEnumerable<T>`自体は使っているが、LINQメソッドとの衝突が起きる要素（`System.Linq.Async`パッケージ参照・`Select`/`Where`等の拡張メソッド呼び出し）は存在しない」ため、この変更によるビルドエラーのリスクはない**。

【改修方法】

現状は改修不要。将来、C-FIND応答の組み立てなどで`IAsyncEnumerable<T>`に対してLINQ操作（絞り込み・変換）を書きたくなった場合は、追加のNuGetパッケージをインストールしなくても、.NET 10の標準ライブラリの`System.Linq.AsyncEnumerable`がそのまま使える。

```csharp
// .NET 10からは、追加パッケージなしでIAsyncEnumerable<T>にLINQが使える
using System.Linq;

async IAsyncEnumerable<DicomCFindResponse> OnCFindRequestAsync(DicomCFindRequest request)
{
    IAsyncEnumerable<DicomDataset> matches = SearchAsync(request);

    // System.Linq.AsyncEnumerable（.NET 10標準）のSelectをそのまま使える
    await foreach (var response in matches.Select(ds => BuildResponse(ds)))
    {
        yield return response;
    }
}
```

【参考記事】

- （公式ドキュメント以外に参考にした技術ブログ等は特になし）
