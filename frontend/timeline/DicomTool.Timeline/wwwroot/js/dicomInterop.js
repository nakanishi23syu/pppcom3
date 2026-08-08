// ======================================================
// wwwroot/js/dicomInterop.js — dicom.ts ライブラリを呼び出すJSInteropブリッジ
// ======================================================
// dicom-tool-2/blazor/DicomTool.Blazor の同名ファイルの移植。
//
// 【このTimelineサービスでは現時点でどこからも呼ばれていないことについて】
// 移植元のDicomTool.Blazorでは Services/DicomRenderer.cs（C#側）が
//     await js.InvokeAsync<IJSObjectReference>("import", "./js/dicomInterop.js")
// という形でこのファイルをESモジュールとして動的importし、Viewer.razor / SeriesThumbnailPanel.razor
// からDICOM画像をcanvasへ描画していた。
//
// dicom-tool-3では画像表示そのものは独立したViewer(Nuxt3, http://localhost:3200)の役割に
// 分離されたため（docs/CONTRACT.md 1章）、Timeline側にはDicomRenderer.cs相当のC#呼び出し元を
// 移植していない。本タスクの指示に従いファイル自体は移植のうえここに残してあるが、
// 現状は「呼び出されない静的アセット」になっている。将来Timeline側でも簡易サムネイル表示等の
// 要件が追加された場合に、このファイルとC#側の呼び出しコードを合わせて復活させる想定。
//
// ======================================================
// 【なぜこのファイルが必要か: JSInteropの基本モデル】
// ======================================================
// Blazor WebAssemblyはC#をブラウザ上でWebAssemblyとして実行するが、DOM操作やWebGL・
// Canvas 2D APIそのものを直接叩く手段は無い（.NET標準ライブラリにブラウザAPIの薄いラッパーが
// 一部あるだけで、WebGLのような複雑なAPI・npmエコシステムのライブラリは対象外）。
// そこで「C#に無い機能はJavaScriptに実装してもらい、C#側からJSInteropで呼び出す」という
// 分担にする。逆にJS側からC#のメソッドを呼ぶ「JS→C#」の向きのInteropも可能だが、
// 元のアプリで使っていたのは「C#→JS」の向きだけ（画像描画の指示を出すだけで、結果をJSから
// C#へ戻す必要が無いため）。
//
// 【ESモジュールとして書く理由】
// Blazorの `IJSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/xxx.js")` は、
// ブラウザ標準の動的 `import()` をそのまま呼ぶ薄いラッパーになっている。
// これにより、このファイルは「グローバルスコープを汚さないES Module」として読み込まれ、
// `export` した関数だけが `IJSObjectReference.InvokeVoidAsync("関数名", 引数...)` から
// 呼び出し可能になる（グローバル変数として window にぶら下げる古い方式より安全）。
//
// ======================================================
// 【dicom.ts をCDN(esm.sh)からブラウザ上で直接importする】
// ======================================================
// 移植元のVue版はnpmでインストールした dicom.ts (^1.3.0) を `import dicomts from 'dicom.ts'`
// のようにバンドラ(Vite)経由で読み込んでいた。BlazorプロジェクトにはNode.js/npmのビルドパイプラインが
// 無い（.csprojのビルドがそのままdotnet buildで完結する）ため、同じ感覚でnpmパッケージをそのまま
// importすることはできない。
//
// 代わりに、esm.sh（npmパッケージをブラウザで直接importできるESモジュールの形に変換して配信してくれる
// 無料CDN）を使う。esm.sh は依存パッケージ（@wearemothership/dicom-character-set・pako・twgl.js等）も
// 全部まとめて解決し、ブラウザがそのまま`import`できる .mjs を返してくれる
// （Access-Control-Allow-Origin: * も返るためCORSの問題も無い）。
//
// バージョンはdicom-tool-2側の frontend/package.json の "dicom.ts": "^1.3.0" に合わせて固定している
// （CDN側で意図せず新しいメジャーバージョンに切り替わらないよう、あえて `^` を付けずピン留め）。
import dicomts from 'https://esm.sh/dicom.ts@1.3.0';

// ======================================================
// parseDicomFile — .dcm ファイルをfetchしてパースする
// ======================================================
// backendの静的ファイル配信（/dicom-files/...）からバイナリを取得し、
// dicom.ts の parseImage() でDICOMのタグ構造を解析したDCMImageオブジェクトに変換する。
async function parseDicomFile(filePath) {
    const res = await fetch(filePath);
    if (!res.ok) {
        throw new Error(`DICOMファイルの取得に失敗しました: ${filePath} (HTTP ${res.status})`);
    }
    const buffer = await res.arrayBuffer();
    // parseImage はパースに失敗すると例外ではなく null を返す仕様（dicom.tsのd.ts定義より）。
    const image = dicomts.parseImage(buffer);
    if (!image) {
        throw new Error(`DICOMファイルのパースに失敗しました: ${filePath}`);
    }
    return image;
}

// ======================================================
// renderDicomToCanvas — .dcm ファイルを<canvas>要素に描画する（C#側から呼ばれる公開関数）
// ======================================================
// 移植元では Services/DicomRenderer.cs の RenderToCanvasAsync から
//     module.InvokeVoidAsync("renderDicomToCanvas", filePath, canvasElementId, scale)
// という形で呼ばれていた。
//
// 【引数がHTMLCanvasElementそのものではなく文字列(canvasElementId)である理由】
// BlazorのC#コードはDOM要素の参照を直接は持てない（ElementReferenceという「その要素を指す
// トークン」を持てるだけで、C#の世界からDOM APIを直接叩くことはできない）。
// 最もシンプルな橋渡し方法は、Razor側でcanvasにユニークな `id` 属性を振っておき、
// C#からはその「id文字列」だけをJSInteropで渡し、JS側で `document.getElementById(id)` を
// 呼んでDOM要素を取得する、というやり方。
export async function renderDicomToCanvas(filePath, canvasElementId, scale = 1) {
    const canvas = document.getElementById(canvasElementId);
    if (!canvas) {
        throw new Error(`canvas要素が見つかりません: #${canvasElementId}`);
    }
    const image = await parseDicomFile(filePath);
    // dicom.ts の render() はWebGLコンテキストを使って高速にピクセルデータをcanvasへ焼き込む。
    // ウィンドウ幅・ウィンドウ中心（DICOMのWindow Center/Width、いわゆる階調表示のコントラスト調整）
    // や色空間変換など、本来なら数百行かかる処理をこの1行に集約してくれている。
    await dicomts.render(image, canvas, scale);
}

// ======================================================
// renderDicomThumbnail — サムネイル一覧向けの「共有WebGLキャンバス」描画
// ======================================================
// dicom.ts の render() は canvasごとに新しいWebGLコンテキストを1つ消費し、ブラウザには
// 同時に保持できるWebGLコンテキスト数の上限がある（Chromeで実用上16個程度）。シリーズの枚数が
// 多いと、サムネイルの数だけWebGLコンテキストを作ってしまい
// 「Too many active WebGL contexts. Oldest context will be lost.」という警告とともに
// 古いサムネイルの描画結果が失われる不具合が起きる。
//
// そのため、実際にWebGLで描画するcanvas（sharedCanvasId）はページ内で共有する1枚だけにし、
// 各サムネイル用のcanvas（destCanvasId）へは、共有canvasに描画できた結果を
// 2D Context の drawImage() でピクセルごとコピーするだけにする。
// こうすることで、サムネイルが何百枚あってもWebGLコンテキストは1つで済む。
//
// 【呼び出し側で直列化する前提であることに注意】
// 共有canvasは同時に1回の描画しか受け付けられない（並行して呼ぶと互いの描画結果を
// 上書きしてしまう）。呼び出し側（C#のforeachループ内でawaitする）が自然に直列実行になる設計に
// する前提のため、このJS関数自体は「1回呼ばれたら1回だけ描画する」という単純な実装のままでよい。
export async function renderDicomThumbnail(filePath, sharedCanvasId, destCanvasId) {
    const shared = document.getElementById(sharedCanvasId);
    const dest = document.getElementById(destCanvasId);
    if (!shared || !dest) {
        throw new Error('canvas要素が見つかりません（共有canvasまたはサムネイルcanvas）');
    }

    const image = await parseDicomFile(filePath);
    // ① 共有canvasにWebGLで描画する（ここでだけWebGLコンテキストを使う）
    await dicomts.render(image, shared, 1);

    // ② 共有canvasの描画結果を、このサムネイル専用の2D canvasへコピーする
    dest.width = shared.width;
    dest.height = shared.height;
    dest.getContext('2d').drawImage(shared, 0, 0);
}
