namespace DicomTool.Api.GraphQL;

// ======================================================
// DicomDeleteAcceptedResult — deleteStudy/deleteSeries/deleteSop Mutationが返す「受理」結果
// ======================================================
// 旧実装は「DB即時削除 + ディスク上の実ファイル削除」を同期的に行い bool（成否）を返していた。
// dicom-tool-3では削除も「ストレージ操作」「DB操作」をTemporalワークフロー
// （DeleteDicomWorkflow）に委譲する非同期処理に変更した（docs/CONTRACT.md 3章）。
// アップロード（DicomUploadAcceptedResult参照）と同様、このMutationが返せるのは
// 「削除を受け付けた」という事実だけであり、「実際にDBレコードとファイルが消え終えたか」は
// 別途Temporal Web UIやstudies Queryの再取得で確認する設計にしている。
public sealed class DicomDeleteAcceptedResult
{
    // "Study" | "Series" | "Sop"。DicomDeleteTargetTypeの文字列表現。
    public required string TargetType { get; init; }

    // 削除対象のUID（StudyInstanceUid / SeriesInstanceUid / SopInstanceUidのいずれか）。
    public required string TargetUid { get; init; }

    // 起動したDeleteDicomWorkflowのWorkflow ID。
    public required string WorkflowId { get; init; }

    public required bool Accepted { get; init; }

    public required string Message { get; init; }
}
