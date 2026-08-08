namespace DicomTool.Api.GraphQL;

// ======================================================
// MeResult — Query.Me（ログイン状態の復元）が返す型
// ======================================================
// AuthPayloadと似ているが、こちらはToken発行の結果ではなく「今のCookieが有効かどうかの照会」
// のため別型にしている（意味的に別物であることをスキーマ上でも区別するため）。
public sealed class MeResult
{
    public required string DisplayName { get; init; }
    public required bool IsAdmin { get; init; }
}
