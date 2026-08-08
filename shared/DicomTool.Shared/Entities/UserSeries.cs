namespace DicomTool.Shared.Entities;

// ======================================================
// UserSeries — 1回の撮影条件のまとまり（テーブル: user_series）
// ======================================================
// Study（検査） > Series（シリーズ） > Sop（画像1枚） の中間階層。
public sealed class UserSeries : IOrderable
{
    public int Id { get; set; }

    // Series Instance UIDはDICOM由来の不変な識別子のため編集不可（initのまま）。
    public required string SeriesInstanceUid { get; init; }

    public required string SeriesNumber { get; set; }
    public string SeriesDescription { get; set; } = "";
    public string Modality { get; set; } = "";

    // 親（UserStudy）への外部キー
    public int UserStudyId { get; set; }
    public UserStudy? Study { get; init; }

    public List<UserSop> Sops { get; init; } = [];

    // このシリーズに含まれる画像のうち、未読が何件残っているか。
    // GraphQL上では「もう計算済みの値」として単純に公開する（DBカラムではない。DbContext側でIgnore設定）。
    public int UnreadCount => Sops.Count(s => !s.IsRead);

    // Notion風のドラッグ&ドロップ並べ替えで保存する表示順。UserStudy.Orderと同じ考え方。
    public int Order { get; set; }

    string IOrderable.ReorderKey => SeriesInstanceUid;
}
