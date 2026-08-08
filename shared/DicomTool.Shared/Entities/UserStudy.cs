namespace DicomTool.Shared.Entities;

// ======================================================
// UserStudy — 1回の検査を表すEF Coreエンティティ（テーブル: user_study）
// ======================================================
// PatientId が同じ複数の UserStudy を日付順に並べたものが「タイムラインビュー」の元データになる
// （Timelineアプリ＝Blazorが表示する画面、frontend/timeline を参照）。
//
// DICOMファイルを毎回パースし直すのは負荷が高いため、一覧・検索・並べ替えで
// よく使う項目（患者名・患者ID・検査日・部位等）をカラムとして外だししている。
//
// dicom-tool(旧)からの変更点: このエンティティ自体はSQLite時代と同じ形のまま
// PostgreSQLへ移行する（EF Core経由なのでプロバイダを差し替えるだけでほぼそのまま動く）。
// 唯一の違いは Program.cs 側の UseSqlite → UseNpgsql（DicomTool.Api/Program.cs参照）。
public sealed class UserStudy : IOrderable
{
    // DB内部の主キー（DICOMのUIDとは別に、EF Coreのリレーション用に持つ）
    public int Id { get; set; }

    // Study Instance UIDはDICOM由来の不変な識別子のため編集不可（initのまま）。
    public required string StudyInstanceUid { get; init; }

    // 以下はNotion風インライン編集でDICOMタグとの整合性を問わず上書きできるようにするため、
    // initではなくsetにしている。
    public required string PatientId { get; set; }
    public required string PatientName { get; set; }

    // DICOMの日付は本来 "yyyyMMdd" の文字列だが、
    // タイムライン（時系列）の並び替えをしやすいよう DateOnly で持つ。
    public required DateOnly StudyDate { get; set; }

    public string StudyDescription { get; set; } = "";
    public string Modality { get; set; } = "";
    public string AccessionNumber { get; set; } = "";

    // 検査部位（DICOMタグ BodyPartExamined (0018,0015) 相当）。
    public string BodyPartExamined { get; set; } = "";

    // Notion風のドラッグ&ドロップ並べ替えで保存する表示順。
    public int Order { get; set; }

    public List<UserSeries> Series { get; init; } = [];

    string IOrderable.ReorderKey => StudyInstanceUid;
}
