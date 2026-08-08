namespace DicomTool.Api.GraphQL;

// ======================================================
// DicomUploadAcceptedResult — uploadDicomFiles Mutationが返す「受理」結果
// ======================================================
// 【なぜ旧DicomUploadResult（StudyUID等の確定値を含む）から型を変えたのか】
// 旧実装ではアップロードされたファイルをその場でfo-dicomパースし、DBに同期保存してから
// 「本当に登録できたStudy/Series/SOPのUID」を含むDicomUploadResultを返していた。
// dicom-tool-3ではアップロード処理を非同期化し（docs/CONTRACT.md 2章）、
// このMutationは「ステージング領域に生ファイルを書き込み、UploadDicomWorkflowを起動する」
// ところまでしか行わない。実際にfo-dicomでタグを解析してStudy/Series/SOPのUIDが確定するのは、
// Worker側のSaveToStorageActivityが実行された後（＝このMutationがレスポンスを返した後）である。
// つまりレスポンスを返す時点ではまだ「確定したUID」は存在しないため、
// 存在しない値をレスポンスに含めることはできない。
//
// これは実務のPACS/画像アップロードAPIでもよくある設計で、HTTPでいう
// 「202 Accepted（受理したが処理はまだ完了していない）」の考え方に相当する。
// クライアント（フロントエンド）は「受け付けられたこと」と「進捗を追うための手がかり
// （WorkflowId）」だけを受け取り、実際に登録が完了したかどうかは
// 「studies Queryを再取得する」「Temporal Web UI(http://localhost:8233)でWorkflowIdを検索する」
// といった別の手段で確認する、という非同期UIの設計を学習する意図がある。
public sealed class DicomUploadAcceptedResult
{
    // アップロード元の元ファイル名（ブラウザが送ってきた名前）。
    public required string FileName { get; init; }

    // 起動したUploadDicomWorkflowのWorkflow ID。
    // Temporal Web UI（http://localhost:8233）でこのIDを検索すると、
    // 実行状況（SaveToStorageActivity/RegisterDicomRecordActivityの進捗、失敗時のリトライ状況）を
    // 確認できる。
    public required string WorkflowId { get; init; }

    // ステージング領域への書き込み・ワークフロー起動そのものに成功したかどうか
    // （＝「受理できたか」）。DICOMファイルとして正しいかどうかの判定はまだ行っていないため、
    // trueであっても最終的にWorker側のパースで失敗する可能性はある。
    public required bool Accepted { get; init; }

    public required string Message { get; init; }
}
