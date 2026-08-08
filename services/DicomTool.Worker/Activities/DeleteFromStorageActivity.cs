using DicomTool.Shared.Constants;
using DicomTool.Shared.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;

namespace DicomTool.Worker.Activities;

// ======================================================
// DeleteFromStorageActivity — 正式ストレージ上のファイル/ディレクトリを削除する
// ======================================================
// docs/CONTRACT.md 3章「① DeleteFromStorageActivity」に対応。
// TargetTypeがSopなら「ファイル1つ」の削除、Study/Seriesなら「ディレクトリごと」の削除になる
// （DeleteDicomWorkflowInput.RelativeStoragePathの意味はSharedのXMLコメント参照）。
//
// SaveToStorageActivity同様、ここでも「DB操作」を一切含めない。ストレージ削除とDB削除を
// 別Activityに分けている理由は docs/CONTRACT.md 3章・Workflows/DeleteDicomWorkflow.csを参照。
public sealed class DeleteFromStorageActivity(
    IHostEnvironment env,
    ILogger<DeleteFromStorageActivity> logger)
{
    // Activity名の明示指定が必須なケース: 既定では両方とも"Delete"という同名になり、
    // 同一Worker内で名前が衝突してWorker起動時に例外になる
    // (詳しい理由はActivities/SaveToStorageActivity.csのコメント参照)。
    [Activity("DeleteFromStorage")]
    public Task DeleteAsync(DeleteDicomWorkflowInput input)
    {
        var storageRoot = StoragePaths.ResolveStoragePath(env.ContentRootPath);
        // RelativeStoragePathはDB上の規約に合わせて'/'区切りで渡ってくる想定だが、
        // Path.Combineは実行OSに関わらず正しく結合してくれるため、そのまま渡してよい。
        var fullPath = Path.Combine(storageRoot, input.RelativeStoragePath);

        if (input.TargetType == DicomDeleteTargetType.Sop)
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                logger.LogInformation("ファイルを削除しました: {FullPath}", fullPath);
            }
            else
            {
                // 【べき等性 ―― at-least-once実行への対処】
                // 既に存在しない(＝過去のリトライで既に削除済み、または元々無かった)場合も
                // 例外にはしない。「削除する」という操作の目的（＝そのファイルが存在しないこと）は
                // 既に達成されているため、これは失敗ではなく成功として扱ってよい。
                // RegisterDicomRecordActivity.csの「べき等性」コメントと同じ考え方。
                logger.LogInformation("削除対象のファイルは既に存在しません（スキップ）: {FullPath}", fullPath);
            }
        }
        else
        {
            // Study削除・Series削除はディレクトリごと再帰削除する。
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
                logger.LogInformation("ディレクトリを削除しました: {FullPath}", fullPath);
            }
            else
            {
                logger.LogInformation("削除対象のディレクトリは既に存在しません（スキップ）: {FullPath}", fullPath);
            }
        }

        return Task.CompletedTask;
    }
}
