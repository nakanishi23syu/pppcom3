namespace DicomTool.Worker.Activities;

// RegisterDicomRecordActivity の戻り値。ワークフロー側でログ出力・結果DTO組み立てに使う程度の
// 軽い情報で十分なため、EF CoreのDB内部主キー(int Id)だけを返す。
public sealed record RegisterDicomRecordResult(
    int UserStudyId,
    int UserSeriesId,
    int UserSopId
);
