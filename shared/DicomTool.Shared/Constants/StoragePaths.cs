namespace DicomTool.Shared.Constants;

// ======================================================
// StoragePaths — DICOMファイルの物理保存場所の規約
// ======================================================
// docs/CONTRACT.md 6章参照。backend/DicomTool.Api、services/DicomTool.DicomScp、
// services/DicomTool.Worker はいずれも「自分のプロジェクトフォルダから見て2階層上」に
// リポジトリルートがある構成（例: backend/DicomTool.Api → ../../ → リポジトリルート）。
// この前提が崩れる場合（プロジェクトを移動した等）は、この相対パスの階層数も
// 合わせて見直すこと。
public static class StoragePaths
{
    /// <summary>
    /// 受信直後・まだTemporalワークフローで正式登録されていないファイルを置くステージング領域。
    /// 各サービスの ContentRootPath（実行ディレクトリ）からの相対パス。
    /// </summary>
    public const string IncomingRelativePath = "../../infra/data/dicom-incoming";

    /// <summary>
    /// UploadDicomWorkflow完了後の正式ストレージ領域。
    /// この配下は {StudyInstanceUid}/{SeriesInstanceUid}/{SopInstanceUid}.dcm という
    /// 構造で配置される（dicom-tool(旧)のDicomUploadServiceの規約を踏襲）。
    /// </summary>
    public const string StorageRelativePath = "../../infra/data/dicom-storage";

    /// <summary>
    /// ContentRootPathと上記の相対パスから、実際に読み書きする絶対パスを組み立てる。
    /// 呼び出し側で Directory.CreateDirectory して使うこと（このメソッド自体はフォルダを作らない）。
    /// </summary>
    public static string ResolveIncomingPath(string contentRootPath) =>
        Path.GetFullPath(Path.Combine(contentRootPath, IncomingRelativePath));

    public static string ResolveStoragePath(string contentRootPath) =>
        Path.GetFullPath(Path.Combine(contentRootPath, StorageRelativePath));
}
