namespace DicomTool.Api.GraphQL;

// ======================================================
// ChangeInputs — 「並べ替え＋インライン編集」統合Mutationの入力型
// ======================================================
// フロントエンドは元データとの差分だけをこの型に詰めて渡す（未変更のプロパティはnull）。
// Mutation.ApplyChangesAsync がKey（UID）で対象行を引き当て、Orderとフィールドを
// まとめて更新する（詳細はMutation.cs参照）。
//
// IChangeInput: Study/Series/Sopの3つの入力型を、共通のジェネリックメソッド
// （ApplyChangesAsync）1つで処理できるようにするための最小限のインターフェース。
// Key（UID）はGraphQLスキーマ上は各型固有の名前（studyInstanceUid等）で公開したいため、
// IOrderable.ReorderKeyと同様に明示的インターフェース実装にしている。
public interface IChangeInput
{
    string Key { get; }
    int? Order { get; }
}

public sealed class StudyChangeInput : IChangeInput
{
    public required string StudyInstanceUid { get; init; }
    public int? Order { get; init; }
    public string? PatientId { get; init; }
    public string? PatientName { get; init; }
    public DateOnly? StudyDate { get; init; }
    public string? StudyDescription { get; init; }
    public string? Modality { get; init; }

    string IChangeInput.Key => StudyInstanceUid;
}

public sealed class SeriesChangeInput : IChangeInput
{
    public required string SeriesInstanceUid { get; init; }
    public int? Order { get; init; }
    public string? SeriesNumber { get; init; }
    public string? SeriesDescription { get; init; }
    public string? Modality { get; init; }

    string IChangeInput.Key => SeriesInstanceUid;
}

public sealed class SopChangeInput : IChangeInput
{
    public required string SopInstanceUid { get; init; }
    public int? Order { get; init; }
    public string? InstanceNumber { get; init; }

    string IChangeInput.Key => SopInstanceUid;
}
