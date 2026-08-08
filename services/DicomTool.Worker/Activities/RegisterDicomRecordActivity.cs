using DicomTool.Shared.Data;
using DicomTool.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;

namespace DicomTool.Worker.Activities;

// ======================================================
// RegisterDicomRecordActivity — 解析済みDICOMタグをPostgreSQLへupsertする
// ======================================================
// docs/CONTRACT.md 2章「② RegisterDicomRecordActivity」に対応。
// SaveToStorageActivityが返した「もうファイルとして正式ストレージに存在するタグ情報」を受け取り、
// UserStudy/UserSeries/UserSopの3テーブルへ登録する。ロジックは
// dicom-tool(旧)/backend/DicomLearning.GraphQL/Services/DicomUploadService.cs の
// UploadOneAsyncを踏襲している（UIDで既存レコードを検索し、無ければ作る＝upsert）。
//
// 【なぜこのActivityが「べき等(idempotent)」でなければならないのか ―― at-least-once実行保証】
// Temporalは各Activityの実行を「最低1回は実行される(at-least-once)」ことしか保証しない。
// 例えば、このActivityがDBへのINSERTに成功した直後、Worker(このプロセス)がクラッシュしたり
// ネットワークが切れたりして、Temporal Serverへ「成功しました」という応答が届かなかった場合、
// Temporalは「失敗したかもしれない」と判断し、同じ入力でこのActivityをもう一度実行する
// (これがリトライの本質＝Temporalは「本当に成功したか」を確認する術がなく、
//  安全側に倒して「疑わしきは再実行」という設計になっている)。
//
// そのため、このActivityを2回実行しても2重にレコードが作られないよう、
// 「SOP Instance UIDが既にDBにあれば新規作成しない」という判定を必ず先頭で行う
// （下記 AnyAsync(...) のチェック）。これが無いと、リトライのたびにDBへ重複行が増えてしまう。
//
// 【なぜProgram.cs側で AddScopedActivities<RegisterDicomRecordActivity>() なのか】
// DbContextはEF Coreの規約上「Scoped」で登録するのが原則（1回のリクエスト/1回の処理単位で
// 1つのDbContextを使い切り、使い回さない）。Temporalの拡張(Temporalio.Extensions.Hosting)は
// 「Activity実行1回ごとに新しいDIスコープを作る」という挙動を提供しており、
// これはASP.NET Coreが「HTTPリクエスト1回ごとに新しいDIスコープを作る」のと全く同じ考え方。
// そのためDbContextをこのActivityのコンストラクタでそのまま受け取れる
// (Program.cs の builder.Services.AddDbContext<DicomDbContext>(...) 参照)。
public sealed class RegisterDicomRecordActivity(
    DicomDbContext db,
    ILogger<RegisterDicomRecordActivity> logger)
{
    // Activity名を明示指定する理由はActivities/SaveToStorageActivity.csのコメント参照
    // (既定名の衝突回避＋Temporal Web UIでの可読性向上)。
    [Activity("RegisterDicomRecord")]
    public async Task<RegisterDicomRecordResult> RegisterAsync(SaveToStorageActivityResult saved)
    {
        // UserSop.Seriesはデフォルトでは自動読み込みされない(EF Coreの遅延読み込みではなく
        // 明示Include方式)ため、戻り値組み立てに必要な親Study Idを得るため明示的にIncludeする。
        var existingSop = await db.UserSops
            .Include(sop => sop.Series)
            .FirstOrDefaultAsync(sop => sop.SopInstanceUid == saved.SopInstanceUid);
        if (existingSop is not null)
        {
            // 既に登録済み＝このActivityが過去に(このリトライより前に)成功していたケース。
            // 上記コメントの通りTemporalのat-least-once保証によりここへ来ることがあるため、
            // エラーにはせず「既にある」を正常系として扱い、既存レコードのIDをそのまま返す。
            logger.LogInformation(
                "SOP Instance UID {SopUid} は既に登録済みのためスキップします（Temporalのat-least-once実行によるリトライの可能性）",
                saved.SopInstanceUid);
            return new RegisterDicomRecordResult(
                existingSop.Series!.UserStudyId, existingSop.UserSeriesId, existingSop.Id);
        }

        var study = await db.UserStudies
            .FirstOrDefaultAsync(s => s.StudyInstanceUid == saved.StudyInstanceUid);
        if (study is null)
        {
            study = new UserStudy
            {
                StudyInstanceUid = saved.StudyInstanceUid,
                PatientId = saved.PatientId,
                PatientName = saved.PatientName,
                StudyDate = saved.StudyDate,
                StudyDescription = saved.StudyDescription,
                Modality = saved.StudyModality,
                AccessionNumber = saved.AccessionNumber,
                BodyPartExamined = saved.BodyPartExamined,
                Order = await db.UserStudies.CountAsync(),
            };
            db.UserStudies.Add(study);
        }

        var series = await db.UserSeries
            .FirstOrDefaultAsync(se => se.SeriesInstanceUid == saved.SeriesInstanceUid);
        if (series is null)
        {
            series = new UserSeries
            {
                SeriesInstanceUid = saved.SeriesInstanceUid,
                SeriesNumber = saved.SeriesNumber,
                SeriesDescription = saved.SeriesDescription,
                Modality = saved.SeriesModality,
                // study.Idは新規Studyの場合まだ0(未確定、SaveChangesAsync実行時に初めてDBが採番する)。
                // その場合このCountAsyncは「UserStudyId == 0のSeries」を数えることになるが、
                // 実在の保存済みStudyのIdは常に1以上なのでヒットせず、結果的に0件＝Order=0になる
                // （旧DicomUploadServiceと同じ書き方）。
                // Seriesの親Studyへの紐付け自体は、下のstudy.Series.Add(series)というナビゲーション
                // プロパティ経由の追加によって、SaveChanges時にEF Coreが外部キーを自動的に補ってくれる。
                Order = await db.UserSeries.CountAsync(se => se.UserStudyId == study.Id),
            };
            study.Series.Add(series);
        }

        // 同じ理屈でSeriesが新規の場合はUserSeriesId==0がヒットせず0件＝Order=0になる。
        var sopOrder = await db.UserSops.CountAsync(sop => sop.UserSeriesId == series.Id);
        var sop = new UserSop
        {
            SopInstanceUid = saved.SopInstanceUid,
            InstanceNumber = saved.InstanceNumber,
            FilePath = saved.RelativeFilePath,
            IsRead = false,
            Order = sopOrder,
        };
        series.Sops.Add(sop);

        // 1つのDbContext(=1回のActivity実行スコープ)の中で、Study/Series/Sopの新規作成～
        // 外部キー確定までを1回のSaveChangesAsyncにまとめる。EF Coreはこれを1つのDBトランザクションとして
        // 実行するため、「Studyだけ作られてSeries/Sopが作られない」といった中途半端な状態にはならない
        // （＝このActivity内では「全部成功」か「全部失敗してロールバック」のどちらかしか起こらない）。
        await db.SaveChangesAsync();

        logger.LogInformation(
            "DBへ登録しました: UserStudyId={StudyId}, UserSeriesId={SeriesId}, UserSopId={SopId} (SOPUID={SopUid})",
            study.Id, series.Id, sop.Id, saved.SopInstanceUid);

        return new RegisterDicomRecordResult(study.Id, series.Id, sop.Id);
    }
}
