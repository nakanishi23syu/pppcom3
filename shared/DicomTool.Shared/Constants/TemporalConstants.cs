namespace DicomTool.Shared.Constants;

// ======================================================
// TemporalConstants — Temporalワークフローの「呼び出し規約」
// ======================================================
// docs/CONTRACT.md 4章参照。Temporalは「どのTask Queueを介して」「どのWorkflow Type名の」
// ワークフローを呼ぶか、という2つの文字列さえ合っていれば、クライアント側（呼び出す側）は
// ワークフローの実装コードを一切参照しなくても起動できる（疎結合なクライアントAPI）。
//
// これは意図的な設計判断: backend/DicomTool.Api や services/DicomTool.DicomScp は
// 「ワークフローを起動する側」であって「ワークフローの中身を知っている側」ではない。
// 実務のマイクロサービスで「他チームが実装したワークフローを、API契約だけ知って呼び出す」
// 感覚に近い状況を学習目的で再現している。
public static class TemporalConstants
{
    /// <summary>
    /// Temporal Namespace。マルチテナントで名前空間を分ける仕組みだが、
    /// 学習用のセルフホスト構成では既定の "default" を使う。
    /// </summary>
    public const string Namespace = "default";

    /// <summary>
    /// このリポジトリの全ワークフロー・全アクティビティが登録される Task Queue 名。
    /// Task QueueはTemporalにおける「どのWorker群が処理を拾うか」を決めるキューであり、
    /// Worker(services/DicomTool.Worker)はこの名前で待ち受け、Client(Api/DicomScp)は
    /// この名前を指定してタスクを積む。
    /// </summary>
    public const string TaskQueue = "dicom-tool-task-queue";

    /// <summary>
    /// アップロード時ワークフローのWorkflow Type名（文字列ベースの起動に使う）。
    /// 実装本体は services/DicomTool.Worker/Workflows/UploadDicomWorkflow.cs。
    /// </summary>
    public const string UploadDicomWorkflowTypeName = "UploadDicomWorkflow";

    /// <summary>
    /// 削除時ワークフローのWorkflow Type名。
    /// 実装本体は services/DicomTool.Worker/Workflows/DeleteDicomWorkflow.cs。
    /// </summary>
    public const string DeleteDicomWorkflowTypeName = "DeleteDicomWorkflow";
}
