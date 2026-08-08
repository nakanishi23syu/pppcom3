namespace DicomTool.Api.Configuration;

// ======================================================
// JwtOptions — JWT発行・検証に使う設定
// ======================================================
// appsettings.json の "Jwt" セクションから読み込む。
// SigningKey は「トークンの正当性を保証する秘密鍵」。本番運用では環境変数や
// シークレットマネージャー経由で注入し、リポジトリにコミットしないこと
// （このプロジェクトは学習用のため、appsettings.Development.json に開発用の値を直接置いている）。
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "DicomTool.Api";
    public string Audience { get; set; } = "DicomTool.Frontend";
    public string SigningKey { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 60 * 8; // 既定8時間（1診療日を想定）
}
