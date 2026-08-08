using DicomTool.Shared.Contracts;
using DicomTool.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;

namespace DicomTool.Worker.Activities;

// ======================================================
// DeleteRecordActivity — PostgreSQLから対象のUserStudy/UserSeries/UserSopを削除する
// ======================================================
// docs/CONTRACT.md 3章「② DeleteRecordActivity」に対応。
//
// 【EF CoreのCascade設定に任せる】
// shared/DicomTool.Shared/Data/DicomDbContext.cs の OnModelCreating で
// UserStudy→UserSeries、UserSeries→UserSopの各リレーションに
// `.OnDelete(DeleteBehavior.Cascade)` が設定されている。これはEF Coreに
// 「PostgreSQL側の外部キー制約自体をON DELETE CASCADEにしてね」と指示するものなので、
// このActivityはStudyやSeriesの行をRemoveするだけでよく、子のSeries/Sopを
// 自分でループして消して回る必要が無い（DB側が自動的にやってくれる）。
public sealed class DeleteRecordActivity(
    DicomDbContext db,
    ILogger<DeleteRecordActivity> logger)
{
    // 名前衝突回避のため明示指定(Activities/DeleteFromStorageActivity.csのコメント参照)。
    [Activity("DeleteRecord")]
    public async Task DeleteAsync(DeleteDicomWorkflowInput input)
    {
        var deleted = input.TargetType switch
        {
            DicomDeleteTargetType.Study => await DeleteStudyAsync(input.TargetUid),
            DicomDeleteTargetType.Series => await DeleteSeriesAsync(input.TargetUid),
            DicomDeleteTargetType.Sop => await DeleteSopAsync(input.TargetUid),
            _ => throw new ArgumentOutOfRangeException(
                nameof(input), input.TargetType, "未知のDicomDeleteTargetTypeです"),
        };

        if (deleted)
        {
            await db.SaveChangesAsync();
        }
    }

    private async Task<bool> DeleteStudyAsync(string studyUid)
    {
        var study = await db.UserStudies.FirstOrDefaultAsync(s => s.StudyInstanceUid == studyUid);
        if (study is null)
        {
            // 【べき等性】既に削除済み(＝at-least-once実行によるリトライ)なら何もしなくてよい。
            // DeleteFromStorageActivity.csと同じ考え方で、例外にはしない。
            logger.LogInformation("削除対象のStudy(UID={StudyUid})は既にDBに存在しません（スキップ）", studyUid);
            return false;
        }

        db.UserStudies.Remove(study);
        logger.LogInformation("UserStudy(Id={Id}, UID={StudyUid})を削除します（子Series/SopはDBのCascadeで自動削除）", study.Id, studyUid);
        return true;
    }

    private async Task<bool> DeleteSeriesAsync(string seriesUid)
    {
        var series = await db.UserSeries.FirstOrDefaultAsync(se => se.SeriesInstanceUid == seriesUid);
        if (series is null)
        {
            logger.LogInformation("削除対象のSeries(UID={SeriesUid})は既にDBに存在しません（スキップ）", seriesUid);
            return false;
        }

        db.UserSeries.Remove(series);
        logger.LogInformation("UserSeries(Id={Id}, UID={SeriesUid})を削除します（子SopはDBのCascadeで自動削除）", series.Id, seriesUid);
        return true;
    }

    private async Task<bool> DeleteSopAsync(string sopUid)
    {
        var sop = await db.UserSops.FirstOrDefaultAsync(s => s.SopInstanceUid == sopUid);
        if (sop is null)
        {
            logger.LogInformation("削除対象のSop(UID={SopUid})は既にDBに存在しません（スキップ）", sopUid);
            return false;
        }

        db.UserSops.Remove(sop);
        logger.LogInformation("UserSop(Id={Id}, UID={SopUid})を削除します", sop.Id, sopUid);
        return true;
    }
}
