using DicomTool.Shared.Contracts;
using DicomTool.Worker.Activities;
using Temporalio.Common;
using Temporalio.Workflows;

namespace DicomTool.Worker.Workflows;

// ======================================================
// UploadDicomWorkflow — アップロードされたDICOMファイル1枚の「後処理」を統括するワークフロー
// ======================================================
// docs/CONTRACT.md 2章に対応。Workflow Type名は "UploadDicomWorkflow"
// (TemporalConstants.UploadDicomWorkflowTypeName)。[Workflow]属性に名前を明示しない場合、
// Temporal .NET SDKはクラス名をそのままWorkflow Type名として使うため、
// このクラス名"UploadDicomWorkflow"がそのまま契約上の名前と一致する（ズレると呼び出せなくなるため注意）。
//
// ======================================================
// 【最重要の学習ポイント】ワークフローコードの「決定性(determinism)」制約
// ======================================================
// Temporalは「ワークフローの現在の状態」をコード変数としてメモリに保持するのではなく、
// 「これまでに起きた出来事の履歴(Event History)」としてTemporal Serverに保存する。
// Workerがクラッシュしたり再起動したりした後、ワークフローの続きを実行する必要が生じたとき、
// TemporalはこのEvent Historyを最初から順番に「再生(Replay)」し、ワークフローのコードを
// もう一度先頭から実行し直すことで、変数の状態を復元する（これを「リプレイ」と呼ぶ）。
//
// このリプレイの仕組みが正しく機能するためには、
// 「同じ入力(Event History)を与えたら、ワークフローのコードは何度実行しても
//   必ず全く同じ順序で同じ判断をする」という性質＝決定性(deterministic)が絶対条件になる。
// もしワークフロー本体の中で以下のようなことをすると、リプレイのたびに結果が変わりうるため、
// Temporalは正しく状態を復元できなくなる（最悪、ワークフローが壊れて進行不能になる）:
//   - 直接のファイルI/O・DB I/O・HTTP呼び出し（実行タイミングや環境によって結果が変わりうる）
//   - DateTime.Now / DateTime.UtcNow（実行するたびに違う値になる）
//   - Guid.NewGuid() や乱数（実行するたびに違う値になる）
//   - Thread.Sleep やスレッドプールを直接操作するコード（実行順序が非決定的になる）
//   - 通常のasync/await越しの非決定的な並行処理（Task.Run等でスレッドを増やす）
//
// このワークフローのRunAsyncメソッドを見て分かる通り、実際に「ファイルを読む」「PostgreSQLに書く」
// といった一切のI/OはActivity（Activities/SaveToStorageActivity.cs、
// Activities/RegisterDicomRecordActivity.cs）側に完全に閉じ込められている。
// ワークフロー本体がしているのは「どのActivityを」「どんな順序で」「どんなオプションで」
// 呼び出すか、という“実行計画”を組み立てることだけであり、そこには非決定的な要素が一切無い。
// （Activity呼び出し自体はTemporalが結果をEvent Historyに記録するため、
//   リプレイ時はActivityを再実行せず記録済みの結果をそのまま使う＝決定性が保たれる）。
//
// もし現在時刻やランダムな値がワークフロー内でどうしても必要な場合は、
// 直接DateTime.Now等を呼ぶのではなく Workflow.UtcNow / Workflow.Random といった
// SDKが提供する「決定性を保証したAPI」を使う（このワークフローでは現状使っていないが、
// 学習用に触れておく）。
[Workflow]
public class UploadDicomWorkflow
{
    [WorkflowRun]
    public async Task<UploadDicomWorkflowResult> RunAsync(UploadDicomWorkflowInput input)
    {
        // Workflow.Logger は通常の ILogger と使い方は同じだが、内部で「リプレイ中はログを
        // 出力しない」という制御が入っている特別なロガー。普通のILoggerをワークフロー内で
        // 直接使ってしまうと、リプレイのたびに同じログが何度も出力されてしまう
        // （ログ出力自体は決定性を壊さないが、無駄なログの重複を避けるための配慮）。
        Workflow.Logger.LogInformation(
            "UploadDicomWorkflowを開始します: StagingFileName={StagingFileName}, SourceProtocol={SourceProtocol}",
            input.StagingFileName, input.SourceProtocol);

        // ── ⓪ CheckStorageCapacityActivity ──
        // 「保存先ストレージの空き容量が足りているか」を、他のどの処理よりも先にチェックする。
        //
        // 【なぜ一番最初に置くのか】
        // SaveToStorageActivity(ファイル解析・正式ストレージへの書き込み)やRegisterDicomRecordActivity
        // (DB登録)は、いずれも「一度始めたら中途半端な状態を後始末する必要が出てくる」処理である
        // (例: ファイルだけ書き込まれてDB登録は失敗した、等)。容量不足という「そもそも今回の
        // アップロードを受け付けるべきではない」状況であれば、そういった後始末が必要な処理へ
        // 進む前に、できるだけ早い段階で弾いてしまうのが安全かつ無駄がない
        // ("fail fast"の考え方。docs/CONTRACT.md 2章のActivity分割の設計思想とも一致する)。
        //
        // 実際の判定ロジックは別プロセス(services/DicomTool.StorageGuard)が持っており、
        // このActivity(Activities/CheckStorageCapacityActivity.cs)はそこへHTTPで問い合わせる
        // だけの薄いアダプター。容量不足の場合はApplicationFailureException(nonRetryable: true)が
        // 投げられ、以下のRetryPolicyに関わらずワークフローは即座に失敗する。
        //   - StorageGuardサービスへの接続自体が一時的に失敗するケース(サービス再起動中等)は
        //     「時間を置けば直るかもしれない」障害のため、SaveToStorageActivityと同様の
        //     Retry設定(最大5回・指数バックオフ)にしている。
        await Workflow.ExecuteActivityAsync(
            (CheckStorageCapacityActivity act) => act.CheckAsync(),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromSeconds(30),
                RetryPolicy = new RetryPolicy
                {
                    InitialInterval = TimeSpan.FromSeconds(2),
                    BackoffCoefficient = 2.0F,
                    MaximumInterval = TimeSpan.FromSeconds(30),
                    MaximumAttempts = 5,
                },
            });

        // ── ① SaveToStorageActivity ──
        // ステージング領域のファイルをfo-dicomで解析し、正式ストレージへ移動する。
        //
        // 【RetryPolicyの考え方】
        // ディスクI/O(ファイルの読み書き)は「ディスクフル」「一時的なファイルロック」等の
        // 一過性の障害で失敗することがあるため、リトライで復旧できる可能性が高い処理である。
        // 一方、Activity内部で「ファイルが存在しない」「DICOMとして壊れている」等の
        // "何度やり直しても直らない"エラーは ApplicationFailureException(nonRetryable: true) を
        // 明示的に投げることでこのRetryPolicyを迂回し、即座に失敗させている
        // (Activities/SaveToStorageActivity.cs参照)。
        //   - InitialInterval: 2秒 … 1回目の再試行までの待ち時間。一過性のディスク詰まり等は
        //     数秒待てば解消することが多いという経験則。
        //   - BackoffCoefficient: 2.0 … 再試行のたびに待ち時間を2倍に伸ばす(指数バックオフ)。
        //     障害が続く場合に無駄な再試行でリソースを消費し続けないようにするための定石。
        //   - MaximumInterval: 30秒 … 待ち時間が際限なく伸びないよう上限を設ける。
        //   - MaximumAttempts: 5 … 5回試してダメなら諦める。ここが0(既定値)だと無限リトライになり、
        //     "本当に直らない障害"のときにワークフローが永遠にPending状態のままになってしまうため、
        //     学習用途では有限回数に制限して「失敗が見える」ようにしている。
        //   - StartToCloseTimeout: 1分 … 1回の試行あたりの制限時間。DICOMファイル1枚のパース＋
        //     移動は通常は一瞬で終わるはずなので、1分でも十分に余裕を持たせた値。
        var saved = await Workflow.ExecuteActivityAsync(
            (SaveToStorageActivity act) => act.SaveAsync(input),
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

        // ── ② RegisterDicomRecordActivity ──
        // ①の結果(タグ解析済みの値)を受け取り、PostgreSQLへupsertする。
        //
        // 【なぜ①と②を1つのActivityにまとめないのか（再掲・重要な設計判断）】
        // 「ストレージへの保存」と「DBへの登録」は、失敗する原因も、失敗時に取るべき対応も異なる。
        //   - ストレージ保存の失敗 → もう一度ファイルI/Oをやり直せばよい
        //   - DB登録の失敗       → もう一度DB接続してSaveChangesをやり直せばよい
        // これらを1つのActivityにまとめてしまうと、「DB接続が一時的に切れただけなのに、
        // 既に成功しているファイル移動までまるごとリトライする」という無駄が発生する
        // (ファイル移動はSaveAsync呼び出し時点で完了しており、再度動かす必要は無い)。
        // Activityを分離し、Temporalに「Activity単位」でリトライさせることで、
        // 各失敗に対して過不足のない粒度の再試行ができる（docs/CONTRACT.md 2章末尾参照）。
        //
        // DB接続はネットワーク越しの一過性障害が起きやすいため、①よりも短い間隔・多めの
        // 試行回数にしている（DB再接続は一般的にファイルI/Oの復旧より速く済むことが多いため）。
        var registered = await Workflow.ExecuteActivityAsync(
            (RegisterDicomRecordActivity act) => act.RegisterAsync(saved),
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
            "UploadDicomWorkflowが完了しました: UserStudyId={StudyId}, UserSeriesId={SeriesId}, UserSopId={SopId}",
            registered.UserStudyId, registered.UserSeriesId, registered.UserSopId);

        return new UploadDicomWorkflowResult(
            saved.StudyInstanceUid, saved.SeriesInstanceUid, saved.SopInstanceUid);
    }
}
