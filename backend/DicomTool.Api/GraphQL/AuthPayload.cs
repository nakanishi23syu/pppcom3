namespace DicomTool.Api.GraphQL;

// ======================================================
// AuthPayload — ログイン成功時にMutation.Loginが返す型
// ======================================================
// AppUser自体をそのまま返さないのは、PasswordHash（ハッシュ化済みとはいえ機微情報）を
// うっかりGraphQLスキーマに公開してしまう事故を避けるため。
// 「ログイン結果として本当に必要なものだけ」を独立した型として定義している。
// Token は含めない: JWTはMutation側でhttpOnly Cookieとして直接レスポンスヘッダーに載せる。
// レスポンスボディに含めてしまうと、フロントエンドのJSがトークン文字列を一時的にでも
// 保持できてしまい、httpOnly Cookieでフロントに触らせない意味が薄れるため。
public sealed class AuthPayload
{
    public required string DisplayName { get; init; }
    public required bool IsAdmin { get; init; }
}
