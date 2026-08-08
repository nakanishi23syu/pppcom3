namespace DicomTool.Shared.Entities;

// ======================================================
// AppUser — ログインするユーザー（読影医）を表すエンティティ（テーブル: app_user）
// ======================================================
// ロールをテーブルで分けるほどの複雑さは不要なため、IsAdmin フラグ1つで
// 「一般の読影医」と「管理者」を区別している。
//
// パスワードは平文で保存せず、PasswordHasher<AppUser> でハッシュ化する（DicomTool.Api側の実装）。
public sealed class AppUser
{
    public int Id { get; set; }

    // ログインID。GraphQL側には公開するが、PasswordHashは公開しない。
    public required string Username { get; init; }

    public required string PasswordHash { get; set; }

    public string DisplayName { get; init; } = "";

    // true: 管理者（並べ替え保存など、より多くの操作が許可される）
    public bool IsAdmin { get; init; }
}
