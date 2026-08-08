namespace DicomTool.Shared.Contracts;

// ======================================================
// DeleteDicomWorkflowInput — DeleteDicomWorkflow の入力DTO
// ======================================================
// docs/CONTRACT.md 3章参照。削除は「対象の種類」「UID」「削除すべきストレージ上のパス」の
// 3点があれば、ストレージ削除・DB削除のどちらのActivityも独立して実行できる。
public enum DicomDeleteTargetType
{
    Study,
    Series,
    Sop,
}

public sealed record DeleteDicomWorkflowInput(
    DicomDeleteTargetType TargetType,

    // TargetTypeに応じて StudyInstanceUid / SeriesInstanceUid / SopInstanceUid のいずれかが入る。
    string TargetUid,

    // infra/data/dicom-storage/ を起点とした相対パス。
    // Study削除なら "{StudyUid}" ディレクトリ、Series削除なら "{StudyUid}/{SeriesUid}" ディレクトリ、
    // Sop削除なら UserSop.FilePath そのもの（ファイル1つ）を指す。
    string RelativeStoragePath
);
