// ======================================================
// Models/GraphQLModels.cs — backend/DicomTool.Api の GraphQL レスポンス/リクエストDTO
// ======================================================
// dicom-tool-2/blazor/DicomTool.Blazor の同名ファイルの移植。GraphQLのフィールド名（camelCase）は
// JSONのキー名としてそのまま届くため、System.Text.Json のデフォルト設定では PascalCase の
// C#プロパティ名と一致しない。これを解決するため、Services/GraphQLClient.cs 側で
// JsonNamingPolicy.CamelCase を使うシリアライザオプションを共通で使い回す
// （このファイル側に [JsonPropertyName] を1つずつ書かなくて済むようにするため）。
//
// StudyService.cs をほぼそのまま移植したのに合わせ、Timeline画面が直接使わない
// フィールド・型（並べ替え・インライン編集・カスケード削除まわり）も含めて丸ごと移植している
// （backend側のスキーマは検査一覧・タイムラインで共通のため、型を分けるメリットが薄い）。

namespace DicomTool.Timeline.Models;

public class GraphQLSop
{
    public string SopInstanceUid { get; set; } = "";
    public string InstanceNumber { get; set; } = "";
    public string FilePath { get; set; } = "";
    public bool IsRead { get; set; }
    public string? ReadAt { get; set; }
    public string? ReadByUserId { get; set; }
    public int Order { get; set; }
}

public class GraphQLSeries
{
    public string SeriesInstanceUid { get; set; } = "";
    public string SeriesNumber { get; set; } = "";
    public string SeriesDescription { get; set; } = "";
    public string Modality { get; set; } = "";
    public int Order { get; set; }
    public List<GraphQLSop> Sops { get; set; } = new();
}

public class GraphQLStudy
{
    public string StudyInstanceUid { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string PatientId { get; set; } = "";
    public string StudyDate { get; set; } = "";
    public string StudyDescription { get; set; } = "";
    public string Modality { get; set; } = "";
    public string AccessionNumber { get; set; } = "";
    public string BodyPartExamined { get; set; } = "";
    public int Order { get; set; }
    public List<GraphQLSeries> Series { get; set; } = new();
}

// ── 変更保存（並べ替え + インライン編集の統合Mutation）の入力型 ──
// null のフィールドは「変更しない」扱い（backend Mutation.cs参照）。
// C#では値型（int等）のnull許容をnull許容型（int?）で表現する。
public class StudyChangeInput
{
    public string StudyInstanceUid { get; set; } = "";
    public int? Order { get; set; }
    public string? PatientId { get; set; }
    public string? PatientName { get; set; }
    /// <summary>"yyyy-MM-dd" 形式の文字列（HotChocolateのLocalDateスカラーに割り当てられる）</summary>
    public string? StudyDate { get; set; }
    public string? StudyDescription { get; set; }
    public string? Modality { get; set; }
}

public class SeriesChangeInput
{
    public string SeriesInstanceUid { get; set; } = "";
    public int? Order { get; set; }
    public string? SeriesNumber { get; set; }
    public string? SeriesDescription { get; set; }
    public string? Modality { get; set; }
}

public class SopChangeInput
{
    public string SopInstanceUid { get; set; } = "";
    public int? Order { get; set; }
    public string? InstanceNumber { get; set; }
}

public class RevertedStudyFields
{
    public string StudyInstanceUid { get; set; } = "";
    public string PatientId { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string StudyDate { get; set; } = "";
    public string StudyDescription { get; set; } = "";
    public string Modality { get; set; } = "";
    public string AccessionNumber { get; set; } = "";
    public string BodyPartExamined { get; set; } = "";
}

public class RevertedSeriesFields
{
    public string SeriesInstanceUid { get; set; } = "";
    public string SeriesNumber { get; set; } = "";
    public string SeriesDescription { get; set; } = "";
    public string Modality { get; set; } = "";
}

public class RevertedSopFields
{
    public string SopInstanceUid { get; set; } = "";
    public string InstanceNumber { get; set; } = "";
}
