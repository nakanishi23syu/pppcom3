namespace DicomTool.Shared.Entities;

// ======================================================
// UserSop — DICOM画像1枚（SOP Instance）を表すエンティティ（テーブル: user_sop）
// ======================================================
// 「読影」という業務行為は、「読影医がまだ見ていない画像」と「もう見た画像」を区別する必要がある。
// このIsRead以下のプロパティ群は、その最小限の仮実装。
public sealed class UserSop : IOrderable
{
    public int Id { get; set; }

    // SOP Instance UID・FilePathは実ファイルと直結する不変な値のため編集不可（initのまま）。
    public required string SopInstanceUid { get; init; }

    // infra/data/dicom-storage/ を起点とした相対パス（docs/CONTRACT.md 6章）。
    // 「正式に確定保存された後」のパスのみを持ち、ステージング領域の一時パスはここには入らない。
    public required string FilePath { get; init; }

    public required string InstanceNumber { get; set; }

    // ── 既読/未読フラグ（このプロジェクトの主目的の仮実装） ──
    public bool IsRead { get; set; }

    // 既読にした日時。未読に戻すとnullに戻す。
    public DateTimeOffset? ReadAt { get; set; }

    // 誰が既読にしたか（読影医のユーザーID等を想定）。
    public string? ReadByUserId { get; set; }

    // 親（UserSeries）への外部キー
    public int UserSeriesId { get; set; }
    public UserSeries? Series { get; init; }

    // Notion風のドラッグ&ドロップ並べ替えで保存する表示順。UserStudy.Orderと同じ考え方。
    public int Order { get; set; }

    string IOrderable.ReorderKey => SopInstanceUid;
}
