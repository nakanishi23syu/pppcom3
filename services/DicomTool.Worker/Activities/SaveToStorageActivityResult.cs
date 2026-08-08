namespace DicomTool.Worker.Activities;

// ======================================================
// SaveToStorageActivityResult — SaveToStorageActivity の戻り値
// ======================================================
// UploadDicomWorkflow内で「① ストレージ保存 → ② DB登録」の間を橋渡しするデータ。
//
// 【なぜこの型がDicomTool.Sharedではなくこのプロジェクト内にあるのか】
// UploadDicomWorkflowInput/DeleteDicomWorkflowInputはWorkerの「外」（backend/DicomTool.Api、
// services/DicomTool.DicomScp）からもらう入力なのでSharedに置く必要があるが、
// この型は「Worker内部の1つのワークフロー実行の中でActivityからActivityへ受け渡すだけ」の
// 中間データであり、他プロセスは一切参照しない。他プロセスが知る必要のない型を
// 無意味にSharedへ置くと「本当に契約として必要なもの」が埋もれるため、あえてWorker内に閉じる。
//
// 【なぜrecordか】
// TemporalはActivityの引数・戻り値をPayload（既定はJSON）にシリアライズしてワークフロー履歴に
// 保存する。record型はプロパティの組み合わせだけで構成される単純な値であり、
// JSONとの相互変換にクセがなく、シリアライズ対象として扱いやすい。
public sealed record SaveToStorageActivityResult(
    string StudyInstanceUid,
    string SeriesInstanceUid,
    string SopInstanceUid,

    string PatientId,
    string PatientName,
    DateOnly StudyDate,
    string StudyDescription,
    string StudyModality,
    string AccessionNumber,
    string BodyPartExamined,

    string SeriesNumber,
    string SeriesDescription,
    string SeriesModality,

    string InstanceNumber,

    // infra/data/dicom-storage/ を起点とした相対パス（docs/CONTRACT.md 6章）。
    // UserSop.FilePathへそのまま書き込む値であるため、常にスラッシュ区切りで統一する。
    string RelativeFilePath
);
