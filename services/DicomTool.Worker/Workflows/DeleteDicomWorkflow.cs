using DicomTool.Shared.Contracts;
using DicomTool.Worker.Activities;
using Temporalio.Common;
using Temporalio.Workflows;

namespace DicomTool.Worker.Workflows;

// ======================================================
// DeleteDicomWorkflow — Study/Series/Sopいずれかの削除処理を統括するワークフロー
// ======================================================
// docs/CONTRACT.md 3章に対応。Workflow Type名は "DeleteDicomWorkflow"
// (TemporalConstants.DeleteDicomWorkflowTypeName)。
//
// 決定性制約についてはWorkflows/UploadDicomWorkflow.csの冒頭コメントで詳しく解説しているので
// ここでは繰り返さない。このワークフローも同様に、実際のI/O（ディスク削除・DB削除）を
// 一切自分では行わず、すべてActivity（Activities/DeleteFromStorageActivity.cs、
// Activities/DeleteRecordActivity.cs）に委譲している。
[Workflow]
public class DeleteDicomWorkflow
{
    [WorkflowRun]
    public async Task RunAsync(DeleteDicomWorkflowInput input)
    {
        Workflow.Logger.LogInformation(
            "DeleteDicomWorkflowを開始します: TargetType={TargetType}, TargetUid={TargetUid}",
            input.TargetType, input.TargetUid);

        // 削除系のActivityはどちらも「べき等」に作ってある
        // (Activities/DeleteFromStorageActivity.cs、Activities/DeleteRecordActivity.csの
        // 「べき等性」コメント参照＝対象が既に無くても例外にせず成功扱いにする)。
        // べき等な処理は「同じ操作を何度実行しても最終結果が変わらない」ため、
        // Temporalのat-least-once実行保証（＝「最低1回」であって「ちょうど1回」ではない）と
        // 非常に相性がよく、安心して積極的にリトライさせられる。
        // そのため削除系はアップロード系(UploadDicomWorkflow.cs)よりもやや多めの試行回数にしている。

        // ── ① DeleteFromStorageActivity ──
        // ディスクからの削除（ファイル1つ、またはディレクトリごと）。
        await Workflow.ExecuteActivityAsync(
            (DeleteFromStorageActivity act) => act.DeleteAsync(input),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromMinutes(1),
                RetryPolicy = new RetryPolicy
                {
                    InitialInterval = TimeSpan.FromSeconds(2),
                    BackoffCoefficient = 2.0F,
                    MaximumInterval = TimeSpan.FromSeconds(30),
                    MaximumAttempts = 5,
                },
            });

        // ── ② DeleteRecordActivity ──
        // PostgreSQLからの削除（EF CoreのCascade設定により子レコードも一緒に消える）。
        // アップロード側同様、ストレージ削除とDB削除を別Activityに分けることで、
        // 「DBだけ一時的に繋がらない」といった障害でもストレージ削除のやり直しを発生させない。
        await Workflow.ExecuteActivityAsync(
            (DeleteRecordActivity act) => act.DeleteAsync(input),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromSeconds(30),
                RetryPolicy = new RetryPolicy
                {
                    InitialInterval = TimeSpan.FromSeconds(1),
                    BackoffCoefficient = 2.0F,
                    MaximumInterval = TimeSpan.FromSeconds(20),
                    MaximumAttempts = 5,
                },
            });

        Workflow.Logger.LogInformation(
            "DeleteDicomWorkflowが完了しました: TargetType={TargetType}, TargetUid={TargetUid}",
            input.TargetType, input.TargetUid);
    }
}
