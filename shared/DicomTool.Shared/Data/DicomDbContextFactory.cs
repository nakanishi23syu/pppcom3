using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DicomTool.Shared.Data;

// ======================================================
// DicomDbContextFactory — `dotnet ef migrations add` 用の設計時ファクトリ
// ======================================================
// DicomTool.Shared はクラスライブラリであり、appsettings.json も DIコンテナも持たない。
// そのため `dotnet ef migrations add <Name>` をこのプロジェクト単体で実行しようとすると、
// EF CoreツールはどうやってDicomDbContextのインスタンスを組み立てればいいか分からず失敗する。
// IDesignTimeDbContextFactory<T> を実装したクラスを置いておくと、EF Coreツールはこれを
// 自動的に見つけて「設計時専用の組み立て方」として使ってくれる（実行時のDI登録とは無関係）。
//
// ここで使う接続文字列はマイグレーションのSQLを生成するためだけに使われ、
// 実際のスキーマ差分（Add-Migration）が接続先DBの中身に依存することはない
// （実行時の本当の接続文字列は各サービスのappsettings.jsonから別途渡される。
//   backend/DicomTool.Api/Program.cs 参照）。
public sealed class DicomDbContextFactory : IDesignTimeDbContextFactory<DicomDbContext>
{
    public DicomDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DicomDbContext>();

        // docs/CONTRACT.md 7章の開発用デフォルト接続文字列と同じ値。
        // マイグレーションファイル生成にしか使わないため、実際にPostgreSQLが
        // 起動している必要すらない（`dotnet ef migrations add` はSQLを生成するだけ）。
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=dicomtool;Username=dicomtool;Password=dicomtool_dev_password");

        return new DicomDbContext(optionsBuilder.Options);
    }
}
