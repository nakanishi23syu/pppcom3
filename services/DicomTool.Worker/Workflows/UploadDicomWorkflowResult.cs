namespace DicomTool.Worker.Workflows;

// UploadDicomWorkflow.RunAsyncの戻り値。Temporal Web UI(http://localhost:8233)や、
// クライアント側(client.GetResultAsync等)から「結局どのDICOM画像が登録されたか」を
// 一目で確認できるように、確定したUIDだけを軽量に返す。
public sealed record UploadDicomWorkflowResult(
    string StudyInstanceUid,
    string SeriesInstanceUid,
    string SopInstanceUid
);
