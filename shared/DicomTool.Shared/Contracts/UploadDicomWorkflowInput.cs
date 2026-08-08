namespace DicomTool.Shared.Contracts;

// ======================================================
// UploadDicomWorkflowInput — UploadDicomWorkflow の入力DTO
// ======================================================
// docs/CONTRACT.md 2章の「経路A(DICOM C-STORE)」「経路B(GraphQL multipart)」の
// どちらから来た場合でも、Temporalワークフローに渡す時点ではこの共通形式に揃える。
//
// 【なぜDICOMタグそのもの(Study/Series/SOP UID等)をここに含めないのか】
// あえて含めていない。ワークフロー側の SaveToStorageActivity が
// ステージングファイルを fo-dicom で開いてタグを読み取る設計にしているため
// （services/DicomTool.Worker/Activities/SaveToStorageActivity.cs参照）。
// こうすることで「ファイルの中身が正」であり続け、呼び出し側(Api/DicomScp)が
// タグの読み取りロジックを重複して持つ必要がなくなる。
//
// 【record型を使う理由】
// Temporalはワークフロー引数を独自のPayload形式（既定はJSON）にシリアライズして
// Task Queueに積む。record型はイミュータブルかつ値の等価性で比較されるため、
// 「ワークフロー入力は一度確定したら変わらないデータ」という性質と相性が良い。
public sealed record UploadDicomWorkflowInput(
    // infra/data/dicom-incoming/ 配下に置かれた、受信直後のファイル名（拡張子込み）。
    // StoragePaths.ResolveIncomingPath(...) と組み合わせて絶対パスを組み立てる。
    string StagingFileName,

    // アップロード元の元ファイル名（GraphQL経由の場合はブラウザが送ってきた名前、
    // DICOM C-STORE経由の場合は "{SOPInstanceUID}.dcm" 等、SCP側で便宜的に付けた名前）。
    // ユーザーへのエラーメッセージ表示や監査ログ用途で保持する。
    string OriginalFileName,

    // "DICOM_CSTORE" | "GRAPHQL_HTTP" のいずれか。
    // 「どちらの入口から来たデータか」をTemporal Web UI上で見分けられるようにするための
    // 学習用メタデータ（業務ロジックの分岐には使わない）。
    string SourceProtocol
);
