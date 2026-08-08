using DicomTool.Api.Services;
using DicomTool.Shared.Data;
using DicomTool.Shared.Entities;

namespace DicomTool.Api.Data;

// ======================================================
// DbSeeder — 起動時にログイン用アカウントだけを投入する
// ======================================================
// 実際のアップロード機能はTemporalワークフロー経由の非同期処理になったため、
// ダミーの検査データ（UserStudy/Series/Sop）はここでは投入しない
// （中身のないダミーデータがサムネイル読み込み失敗の原因になるため）。
// ログイン用アカウントだけは学習・動作確認に必要なため引き続き投入する。
//
// Program.cs から、起動時に「テーブルが空なら」投入する形で呼び出す。
internal static class DbSeeder
{
    public static void SeedIfEmpty(DicomDbContext db, AuthService authService)
    {
        SeedUsersIfEmpty(db, authService);
    }

    // ======================================================
    // ログインユーザーの初期投入
    // ======================================================
    // 学習・動作確認用の開発専用アカウント。本番運用ではこのような固定パスワードの
    // シードは絶対に行わないこと（ここではbackend/README.mdに明記した上であえて用意している）。
    private static void SeedUsersIfEmpty(DicomDbContext db, AuthService authService)
    {
        if (db.AppUsers.Any())
        {
            return;
        }

        var admin = new AppUser
        {
            Username = "admin",
            DisplayName = "管理者",
            IsAdmin = true,
            PasswordHash = "",
        };
        admin.PasswordHash = authService.HashPassword("admin1234");

        var doctor = new AppUser
        {
            Username = "dr-tanaka",
            DisplayName = "田中医師",
            IsAdmin = false,
            PasswordHash = "",
        };
        doctor.PasswordHash = authService.HashPassword("doctor1234");

        db.AppUsers.AddRange(admin, doctor);
        db.SaveChanges();
    }
}
