using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DicomTool.Api.Configuration;
using DicomTool.Shared.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DicomTool.Api.Services;

// ======================================================
// AuthService — パスワードのハッシュ化/照合 と JWT発行を担当する
// ======================================================
// 【なぜここに集約するか】
// パスワードの扱い（ハッシュ化・照合）とトークンの発行ロジックは、
// Mutation.cs に直接書くと肥大化しテストもしづらい。
// 「認証」という1つの関心事としてこのクラスにまとめている。
//
// クラス名の後ろの (IOptions<JwtOptions> jwtOptions) は「プライマリコンストラクタ」という
// C# 12の構文。普通のコンストラクタ＋privateフィールドを省略して書けるもので、
// jwtOptions はクラス内のどのメソッドからも使えるフィールドとして扱われる。
//
// 【IOptions&lt;JwtOptions&gt; とは】
// appsettings.json の "Jwt" セクションの値をC#オブジェクトとして受け取るための、
// ASP.NET Core標準の「Optionsパターン」。Program.cs 側で
//   builder.Services.Configure&lt;JwtOptions&gt;(jwtSection);
// と登録しておくと、jwtOptions.Value で appsettings.json の値（Issuer/Audience/SigningKey等）
// が読める状態でDIコンテナから渡ってくる（＝設定ファイルの値を直接new JwtOptions()するのではなく、
// IOptions越しに取得するのがASP.NET Coreの慣習）。
public sealed class AuthService(IOptions<JwtOptions> jwtOptions)
{
    // PasswordHasher<T> はASP.NET Coreに同梱されているハッシュ化ユーティリティ
    // （PBKDF2ベース、ソルト自動生成）。TはHash対象に紐づく型を渡すだけで、
    // 実際にはAppUserの中身は参照しない（型引数は用途を表すマーカー的な役割）。
    private readonly PasswordHasher<AppUser> _passwordHasher = new();

    public string HashPassword(string plainPassword) =>
        _passwordHasher.HashPassword(default!, plainPassword);

    public bool VerifyPassword(AppUser user, string plainPassword) =>
        _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, plainPassword)
            != PasswordVerificationResult.Failed;

    // ======================================================
    // GenerateToken — ログイン成功時にJWTを発行する
    // ======================================================
    // クレーム（Claims）にユーザー名と管理者フラグを積んでおくと、
    // Mutation側の [Authorize(Roles = "Admin")] 等でトークンの中身だけで認可判定できる
    // （リクエストのたびにDBへ管理者かどうか問い合わせなくて済む）。
    public string GenerateToken(AppUser user)
    {
        var options = jwtOptions.Value;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Username),
            new("displayName", user.DisplayName),
        };
        if (user.IsAdmin)
        {
            // ASP.NET Coreの[Authorize(Roles = "Admin")]はClaimTypes.Roleクレームを見る規約。
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(options.ExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
