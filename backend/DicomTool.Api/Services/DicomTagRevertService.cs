using DicomTool.Shared.Constants;
using DicomTool.Shared.Entities;
using FellowOakDicom;

namespace DicomTool.Api.Services;

// ======================================================
// DicomTagRevertService — 「DICOMタグへの復元」機能だけを担当する
// ======================================================
// 【なぜ旧DicomUploadServiceから大幅に縮小したのか】
// 旧実装（dicom-tool(旧)のDicomUploadService）は
//   1. アップロードされたファイルをfo-dicomでパースしてDBへupsert保存する
//   2. DB即時削除に合わせてディスク上の実ファイルを削除する
//   3. インライン編集で上書きされたフィールドをDICOMタグの値に戻す（復元）
// の3つをまとめて担っていたが、dicom-tool-3では 1. と 2. はTemporalワークフロー
// （services/DicomTool.Worker の SaveToStorageActivity / DeleteFromStorageActivity）の
// 責務に移した（docs/CONTRACT.md 2章・3章）。
// このApiプロセスは「ステージング領域に生ファイルを置くだけ」「ワークフローを起動するだけ」に
// 徹し、実際のfo-dicom解析やディスクI/Oの実行はWorker側の別プロセスが担う。
//
// 唯一 3.「復元」機能だけはこのApi側に残る。復元は「確定済みストレージの既存ファイルを
// 読み直すだけ」の読み取り専用処理であり、Worker経由の非同期化・リトライを必要とする
// 書き込み処理ではないため、GraphQLのMutationからその場で同期的に完結させても実務的な
// 問題が無い（レスポンスを返すまでdisk I/Oを1回行うだけ）。
//
// クラス名の後ろの (IHostEnvironment env) はプライマリコンストラクタ（C# 12構文）。
// env はこのクラスのどのメソッドからも使えるフィールドとして扱われる。
public sealed class DicomTagRevertService(IHostEnvironment env)
{
    // ======================================================
    // DICOMタグへの復元系（Mutation.RevertStudy/Series/SopFieldsAsync から呼ばれる）
    // ======================================================
    // インライン編集（SaveXxxChangesAsync）で上書きされた値を、実際のDICOMファイルに
    // 書かれているタグの値に戻す。DBには「編集前の値」を別途保持していないため、
    // 実ファイルそのものを都度読み直すことで「タグの値」を取得する
    // （FilePathはinit専用でアップロード後に変わらないため、常に元のファイルを指している）。
    public async Task RevertStudyTagsAsync(UserStudy study, string anyRelativeFilePathInStudy)
    {
        var ds = await OpenDatasetAsync(anyRelativeFilePathInStudy);
        study.PatientId = ds.GetSingleValueOrDefault(DicomTag.PatientID, "");
        study.PatientName = ds.GetSingleValueOrDefault(DicomTag.PatientName, "");
        if (ds.TryGetSingleValue<DateTime>(DicomTag.StudyDate, out var parsedDate))
        {
            study.StudyDate = DateOnly.FromDateTime(parsedDate);
        }
        study.StudyDescription = ds.GetSingleValueOrDefault(DicomTag.StudyDescription, "");
        study.Modality = ds.GetSingleValueOrDefault(DicomTag.Modality, "");
        study.AccessionNumber = ds.GetSingleValueOrDefault(DicomTag.AccessionNumber, "");
        study.BodyPartExamined = ds.GetSingleValueOrDefault(DicomTag.BodyPartExamined, "");
    }

    public async Task RevertSeriesTagsAsync(UserSeries series, string anyRelativeFilePathInSeries)
    {
        var ds = await OpenDatasetAsync(anyRelativeFilePathInSeries);
        series.SeriesNumber = ds.GetSingleValueOrDefault(DicomTag.SeriesNumber, "");
        series.SeriesDescription = ds.GetSingleValueOrDefault(DicomTag.SeriesDescription, "");
        series.Modality = ds.GetSingleValueOrDefault(DicomTag.Modality, "");
    }

    public async Task RevertSopTagsAsync(UserSop sop)
    {
        var ds = await OpenDatasetAsync(sop.FilePath);
        sop.InstanceNumber = ds.GetSingleValueOrDefault(DicomTag.InstanceNumber, "");
    }

    private async Task<DicomDataset> OpenDatasetAsync(string relativeFilePath)
    {
        var fullPath = ResolveFullPath(relativeFilePath);
        var file = await DicomFile.OpenAsync(fullPath);
        return file.Dataset;
    }

    // 相対パス（infra/data/dicom-storage/ を起点、UserSop.FilePathと同じ規約）→実ディスクパスの変換。
    // 保存規約（StoragePaths）を共有定数に一本化しているため、appsettings.jsonのStorage:DicomRootは
    // もう参照しない（docs/CONTRACT.md 6章）。
    private string ResolveFullPath(string relativePath) =>
        System.IO.Path.Combine(StoragePaths.ResolveStoragePath(env.ContentRootPath), relativePath);
}
