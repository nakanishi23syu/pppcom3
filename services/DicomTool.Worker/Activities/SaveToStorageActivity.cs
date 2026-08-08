using DicomTool.Shared.Constants;
using DicomTool.Shared.Contracts;
using FellowOakDicom;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using Temporalio.Exceptions;

namespace DicomTool.Worker.Activities;

// ======================================================
// SaveToStorageActivity — ステージング領域のDICOMファイルをタグ解析し、正式ストレージへ移動する
// ======================================================
// docs/CONTRACT.md 2章「① SaveToStorageActivity」に対応。
//
// 【このActivityが担う責務（あえて狭くしている）】
//   1. infra/data/dicom-incoming/ (ステージング領域) にあるファイルを fo-dicom で開く
//   2. Study/Series/SOP Instance UID等のDICOMタグを読み取る
//   3. infra/data/dicom-storage/{StudyUID}/{SeriesUID}/{SOPUID}.dcm （正式ストレージ）へ保存する
//   4. 読み取ったタグ値を SaveToStorageActivityResult として返す（PostgreSQLへはまだ書き込まない）
//
// DB書き込みをしないのは、"ディスクへのファイル操作"と"DBへの書き込み"という、
// 失敗の種類も再試行の考え方も異なる2つの処理を1つのActivityに混ぜないため
// （docs/CONTRACT.md 2章末尾の設計意図、このファイル末尾のコメントも参照）。
//
// 【DIコンテナとの関係】
// このクラスはProgram.cs側で `AddScopedActivities<SaveToStorageActivity>()` によってDIコンテナに
// "Scoped"として登録される（services/DicomTool.Worker/Program.cs参照）。
// Temporal .NET SDK(Temporalio.Extensions.Hosting)は、Activity呼び出し1回ごとに新しいDIスコープを
// 自動的に作り、そのスコープからこのクラスのインスタンスを解決してからメソッドを呼び出し、
// 呼び出し終了後にスコープを破棄する。コンストラクタで受け取る IHostEnvironment は
// Generic Host が最初から用意しているシングルトンサービスなので、そのまま注入できる。
public sealed class SaveToStorageActivity(
    IHostEnvironment env,
    ILogger<SaveToStorageActivity> logger)
{
    // [Activity] を付けたメソッドだけが「Temporalのタスクキューから呼び出せる処理」として登録される
    // (Program.cs の AddScopedActivities<SaveToStorageActivity>() が、このクラス内の
    // [Activity] 属性付きメソッドを自動的に見つけて Worker に登録する)。
    //
    // 【Activityの中では何をしてもよい（＝決定性制約が無い）】
    // Workflows/UploadDicomWorkflow.cs 側のコメントで詳しく説明する通り、Workflow本体は
    // 決定性(deterministic)を保つ必要があるためファイルI/O・DB I/O等を直接書けない。
    // その"やっていいI/O"を実際に行う場所がActivityである。ここでは普通に
    // 同期/非同期のファイルI/Oや例外処理を、通常のC#コードと同じ感覚で書いてよい。
    // [Activity]に明示的な名前を渡している。既定では「メソッド名から末尾のAsyncを除いた文字列」
    // (この場合"Save")がActivity名になるが、Activity名はWorkerプロセス内でクラスをまたいでも
    // グローバルに一意でなければならない(実際、DeleteFromStorageActivity.DeleteAsyncと
    // DeleteRecordActivity.DeleteAsyncをどちらも既定名のままにすると、2つとも"Delete"という
    // 同じ名前になり "Duplicate activity named Delete" というエラーでWorker起動に失敗する)。
    // 明示的に名前を付けておくことで、名前の衝突を防ぐと同時に、Temporal Web UI
    // (http://localhost:8233)の実行履歴上でもこの名前がそのまま表示され、可読性が上がる。
    [Activity("SaveToStorage")]
    public async Task<SaveToStorageActivityResult> SaveAsync(UploadDicomWorkflowInput input)
    {
        var incomingRoot = StoragePaths.ResolveIncomingPath(env.ContentRootPath);
        var stagingFullPath = Path.Combine(incomingRoot, input.StagingFileName);

        logger.LogInformation(
            "ステージングファイルを解析します: {StagingFullPath} (元ファイル名: {OriginalFileName}, 経路: {SourceProtocol})",
            stagingFullPath, input.OriginalFileName, input.SourceProtocol);

        if (!File.Exists(stagingFullPath))
        {
            // ファイルが最初から無いのは「時間を置いて再試行しても解決しない」種類の失敗。
            // ApplicationFailureException の nonRetryable: true を明示すると、
            // ワークフロー側でどんなRetryPolicyを設定していても即座に失敗が確定し、
            // 無駄なリトライでTemporal ServerやWorkerに負荷をかけずに済む
            // （Workflows/UploadDicomWorkflow.cs のRetryPolicyコメントも参照）。
            throw new ApplicationFailureException(
                $"ステージング領域にファイルが見つかりません: {stagingFullPath}",
                nonRetryable: true);
        }

        DicomFile dicomFile;
        try
        {
            // fo-dicomでファイルを開く。DICOMとして壊れている・非DICOMファイルが混入した等の場合は
            // 例外が飛ぶが、これも「再試行しても直らない」失敗なので nonRetryable にする。
            dicomFile = await DicomFile.OpenAsync(stagingFullPath);
        }
        catch (Exception ex) when (ex is not ApplicationFailureException)
        {
            throw new ApplicationFailureException(
                $"DICOMファイルとして読み込めませんでした: {input.OriginalFileName} ({ex.Message})",
                inner: ex,
                nonRetryable: true);
        }

        var ds = dicomFile.Dataset;
        var studyUid = ds.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "");
        var seriesUid = ds.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "");
        var sopUid = ds.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, "");

        if (string.IsNullOrEmpty(studyUid) || string.IsNullOrEmpty(seriesUid) || string.IsNullOrEmpty(sopUid))
        {
            // UIDが読み取れないDICOMファイルはDICOM規格違反であり、これも再試行では直らない。
            throw new ApplicationFailureException(
                $"Study/Series/SOP Instance UIDのいずれかが読み取れませんでした: {input.OriginalFileName}",
                nonRetryable: true);
        }

        var studyDate = ds.TryGetSingleValue<DateTime>(DicomTag.StudyDate, out var parsedDate)
            ? DateOnly.FromDateTime(parsedDate)
            : DateOnly.FromDateTime(DateTime.UtcNow);
        // ↑ ここで DateTime.UtcNow を使っているのは「タグが無い場合のフォールバック値」を
        // 作るためであり、Workflow本体の決定性とは無関係（Activity内は現在時刻を読んでよい）。
        // 対照的に、Workflow.csの中で同じことをするのは禁止（決定性制約、Workflows/参照）。

        var storageRoot = StoragePaths.ResolveStoragePath(env.ContentRootPath);
        // DB上の相対パス表現は常にスラッシュ区切りで統一する（旧実装 DicomUploadService.cs と同じ規約。
        // Path.Combineの結果はWindows上ではバックスラッシュになるため、実ディスク操作用パスとは別に組み立てる）。
        var relativeFilePath = string.Join('/', studyUid, seriesUid, $"{sopUid}.dcm");
        var destinationFullPath = Path.Combine(storageRoot, studyUid, seriesUid, $"{sopUid}.dcm");

        Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath)!);

        // ここから下のI/O(ディスクへの書き込み)は「ディスクフル」「権限不足」等で失敗しうるが、
        // それは環境側の一時的な問題である可能性があるため、あえてnonRetryableにせず、
        // 通常の例外のまま投げる。Temporal SDKは「Activity内で投げた非Temporal例外」を
        // 自動的に「リトライ可能なApplicationFailureException」に変換してワークフロー側へ伝える
        // （SDK自身のXMLドキュメントコメントにもその旨明記されている）。
        // そのため、ワークフロー側で設定したRetryPolicy（Workflows/UploadDicomWorkflow.cs）に従い、
        // Temporalが自動的に再実行してくれる。
        await dicomFile.SaveAsync(destinationFullPath);

        // ステージング領域のファイルは正式ストレージへの移動が完了したら不要になるため削除する。
        // 万一削除に失敗しても（例: 他プロセスがロック中）、正式ストレージへの保存自体は
        // 完了しているため業務上は成功として扱ってよい。ログにだけ残す。
        try
        {
            File.Delete(stagingFullPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ステージングファイルの削除に失敗しましたが、処理は続行します: {StagingFullPath}", stagingFullPath);
        }

        logger.LogInformation(
            "正式ストレージへ保存しました: {DestinationFullPath} (StudyUID={StudyUid}, SeriesUID={SeriesUid}, SOPUID={SopUid})",
            destinationFullPath, studyUid, seriesUid, sopUid);

        return new SaveToStorageActivityResult(
            StudyInstanceUid: studyUid,
            SeriesInstanceUid: seriesUid,
            SopInstanceUid: sopUid,
            PatientId: ds.GetSingleValueOrDefault(DicomTag.PatientID, ""),
            PatientName: ds.GetSingleValueOrDefault(DicomTag.PatientName, ""),
            StudyDate: studyDate,
            StudyDescription: ds.GetSingleValueOrDefault(DicomTag.StudyDescription, ""),
            StudyModality: ds.GetSingleValueOrDefault(DicomTag.Modality, ""),
            AccessionNumber: ds.GetSingleValueOrDefault(DicomTag.AccessionNumber, ""),
            BodyPartExamined: ds.GetSingleValueOrDefault(DicomTag.BodyPartExamined, ""),
            SeriesNumber: ds.GetSingleValueOrDefault(DicomTag.SeriesNumber, ""),
            SeriesDescription: ds.GetSingleValueOrDefault(DicomTag.SeriesDescription, ""),
            SeriesModality: ds.GetSingleValueOrDefault(DicomTag.Modality, ""),
            InstanceNumber: ds.GetSingleValueOrDefault(DicomTag.InstanceNumber, ""),
            RelativeFilePath: relativeFilePath);
    }
}
