using DicomTool.Api.Constants;
using DicomTool.Api.Configuration;
using DicomTool.Api.Services;
using DicomTool.Shared.Constants;
using DicomTool.Shared.Contracts;
using DicomTool.Shared.Data;
using DicomTool.Shared.Entities;
using HotChocolate;
using HotChocolate.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Temporalio.Client;

namespace DicomTool.Api.GraphQL;

// ======================================================
// Mutation — GraphQLの「Mutation」ルートタイプ
// ======================================================
// GraphQLには「読み取り(Query)」と「書き込み(Mutation)」で
// ルートタイプを分ける慣習がある（技術的にはQueryでも書き込みは可能だが、
// 「副作用があるかどうか」をスキーマ上で明確に区別するための約束事）。
//
// [Authorize] 属性について: HotChocolate.AspNetCore.Authorization パッケージが提供するもので、
// ASP.NET CoreのJWT認証（Program.cs の AddJwtBearer）と連動する。
// 属性なしのフィールドは未ログインでも呼べるが、[Authorize] を付けたフィールドは
// 有効なJWT（Authorizationヘッダー、実体はhttpOnly Cookie）が無いと呼び出し時にエラーになる。
public class Mutation
{
    // ======================================================
    // ログイン
    // ======================================================
    // ユーザー名・パスワードを照合し、成功したらJWTを発行する。
    // JWTはhttpOnly Cookieとしてレスポンスヘッダーに直接乗せる。
    // httpOnly Cookieはブラウザが自動送信し、JavaScriptからは読み書きできないため、
    // 万一XSSが起きてもこの経路ではトークンを盗めない（レスポンスボディにはtokenを含めない）。
    // mutation { login(username: "admin", password: "admin1234") { displayName isAdmin } }
    public async Task<AuthPayload> LoginAsync(
        string username,
        string password,
        [Service] DicomDbContext db,
        [Service] AuthService authService,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] IOptions<JwtOptions> jwtOptions)
    {
        var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null || !authService.VerifyPassword(user, password))
        {
            // HotChocolateは既定で「ハンドルしていない例外」を "Unexpected Execution Error" に
            // マスクしてクライアントへ返す（内部エラーの詳細を誤って漏らさないための安全策）。
            // ユーザーへそのまま見せたいメッセージは GraphQLException で明示的に投げる必要がある。
            throw new GraphQLException("ユーザー名またはパスワードが正しくありません。");
        }

        var token = authService.GenerateToken(user);
        AppendAuthCookie(httpContextAccessor, token, jwtOptions.Value.ExpiryMinutes);

        return new AuthPayload
        {
            DisplayName = user.DisplayName,
            IsAdmin = user.IsAdmin,
        };
    }

    // ======================================================
    // ログアウト
    // ======================================================
    // httpOnly CookieはJSから削除できないため、サーバー側で「即座に無効な期限」を持つ
    // 同名Cookieを上書きしてブラウザに削除させる。
    public bool Logout([Service] IHttpContextAccessor httpContextAccessor)
    {
        httpContextAccessor.HttpContext!.Response.Cookies.Delete(AppConstants.AuthCookieName);
        return true;
    }

    private static void AppendAuthCookie(IHttpContextAccessor httpContextAccessor, string token, int expiryMinutes)
    {
        var context = httpContextAccessor.HttpContext!;
        context.Response.Cookies.Append(AppConstants.AuthCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            // フロントエンド（別ポート）とバックエンドは異なるオリジンのため、
            // クロスサイトのCookie送信を許可するにはSameSite=Noneが必要。
            // SameSite=NoneはSecure（HTTPS）必須というブラウザの仕様があるため、
            // 開発環境（HTTP）ではSecure=falseのSameSite=Laxにフォールバックする
            // （localhost同士のポート違いはブラウザ上「same-site」扱いなのでLaxでも送信される）。
            Secure = context.Request.IsHttps,
            SameSite = context.Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes),
        });
    }

    // ======================================================
    // DICOMファイルアップロード（GraphQL Multipart Request対応・非同期経路）
    // ======================================================
    // 【なぜGraphQLでファイルを送れるのか】
    // GraphQL自体の仕様には「ファイルを送る」という概念が無い（variablesはJSONで表現できる
    // 値、つまりテキストしか想定していない）。それでもファイルを送れているのは、HotChocolateが
    // 「GraphQL multipart request specification」
    // （https://github.com/jaydenseric/graphql-multipart-request-spec）という業界の非公式仕様を
    // サーバー側で実装しているため。詳細な組み立て方はフロントエンド側の実装を参照。
    //
    // 【引数の型 List<IFile> について】
    // IFile はHotChocolateが提供する「アップロードされた1ファイル」を表すインターフェースで、
    // ファイル名（Name）やサイズ（Length）、中身を読むためのストリーム（OpenReadStream()）を持つ。
    // スキーマ上は `[Upload!]!` という独自スカラー型として表現される。このUploadスカラー自体は
    // GraphQLの標準スカラーには存在せず、HotChocolateが独自拡張として提供しているため、
    // Program.cs の AddGraphQLServer() チェーンに `.AddType<UploadType>()` を追加して
    // 明示的に有効化する必要がある（登録を忘れるとスキーマ生成時にエラーになる）。
    //
    // 【dicom-tool(旧)からの最大の変更点 ―― その場でfo-dicomパースしない】
    // 旧実装はここで直接fo-dicomを使いファイルを解析し、DBへ同期保存していた。
    // dicom-tool-3ではこの処理をTemporalワークフロー（UploadDicomWorkflow）に委譲する
    // （docs/CONTRACT.md 2章）。このMutationがやることは以下の2つだけ:
    //   1. アップロードされた生ファイルを、一意なファイル名で
    //      infra/data/dicom-incoming/（ステージング領域）に書き込む。
    //   2. Temporal Clientでワークフローを起動する（Workflow実装クラスは参照しない。
    //      TaskQueue名・WorkflowType名という「文字列の約束事」だけで疎結合に呼び出す）。
    // fo-dicomによるタグ解析・DBのStudy/Series/SOP upsertは、すべてWorker側の
    // SaveToStorageActivity / RegisterDicomRecordActivityが後から非同期に行う。
    // 戻り値もこれに合わせて「受理しました」を意味するDicomUploadAcceptedResultに変更した
    // （詳細な設計理由はDicomUploadAcceptedResult.csのコメント参照）。
    //
    // 【認可（[Authorize]）を付けていない理由】
    // 元のRESTエンドポイントも認証不要だったため、挙動を変えないよう踏襲している
    // （学習用ツールとしてログイン前でもDICOMファイルを試しに取り込めるようにする意図）。
    public async Task<List<DicomUploadAcceptedResult>> UploadDicomFilesAsync(
        List<IFile> files,
        [Service] IHostEnvironment env,
        [Service] ITemporalClient temporalClient)
    {
        // ステージング領域（受信直後・未確定のファイルを置く場所）。
        // 経路B(このMutation)・経路A(DICOM C-STORE、services/DicomTool.DicomScp)のどちらから来ても
        // 同じフォルダにファイルを置く、という約束事(docs/CONTRACT.md 2章)。
        var incomingPath = StoragePaths.ResolveIncomingPath(env.ContentRootPath);
        Directory.CreateDirectory(incomingPath);

        var results = new List<DicomUploadAcceptedResult>();

        // REST版のときと同じ理由（旧実装踏襲）で、Task.WhenAllによる並列化はせずforeachで直列に処理する。
        // ここでの直列化は「DBの整合性のため」ではなく（DB書き込みはもうここでは行わない）、
        // 単に「同時に大量のワークフローを起動して負荷が偏るのを避ける」学習用の簡略実装。
        foreach (var file in files)
        {
            // ファイル名の衝突を避けるためGUIDで一意なステージングファイル名を発行する。
            // 元のファイル名(file.Name)は表示・監査用にOriginalFileNameとして別途ワークフローへ渡す。
            var stagingFileName = $"{Guid.NewGuid():N}.dcm";
            var stagingFullPath = System.IO.Path.Combine(incomingPath, stagingFileName);

            await using (var source = file.OpenReadStream())
            await using (var destination = File.Create(stagingFullPath))
            {
                await source.CopyToAsync(destination);
            }

            // Workflow IDは「同じ入力で誤って二重起動されていないか」をTemporalが判定するための
            // 一意キーでもある。ここでは1ファイル1起動のため、GUIDで単純に一意性を確保する。
            var workflowId = $"upload-{Guid.NewGuid():N}";
            var input = new UploadDicomWorkflowInput(stagingFileName, file.Name, "GRAPHQL_HTTP");

            // ======================================================
            // 「型なしクライアントAPI」でのワークフロー起動（docs/CONTRACT.md 4章）
            // ======================================================
            // StartWorkflowAsync(workflowTypeName: string, args: ..., options: WorkflowOptions) という
            // 文字列ベースのオーバーロードを使う。ジェネリック版（Workflow実装クラスの型引数を渡す形）
            // も存在するが、そちらを使うとこのApiプロジェクトがWorker側の実装クラスを直接参照する
            // 必要が生じてしまい、「Apiはワークフローの中身を知らない」という設計方針
            // （services/DicomTool.Worker が持つ実装の詳細を知らない）に反する。
            await temporalClient.StartWorkflowAsync(
                TemporalConstants.UploadDicomWorkflowTypeName,
                [input],
                new WorkflowOptions(id: workflowId, taskQueue: TemporalConstants.TaskQueue));

            results.Add(new DicomUploadAcceptedResult
            {
                FileName = file.Name,
                WorkflowId = workflowId,
                Accepted = true,
                Message = "アップロードを受け付けました。取り込み処理はバックグラウンドで実行中です。",
            });
        }

        return results;
    }

    // ログインしている読影医なら誰でも可能な操作。
    [Authorize]
    // ── 画像を既読にする（このプロジェクトの主目的の仮実装） ──
    public async Task<UserSop> MarkInstanceAsReadAsync(
        string sopInstanceUid,
        string userId,
        [Service] DicomDbContext db)
    {
        var sop = await FindSopOrThrowAsync(sopInstanceUid, db);
        sop.IsRead = true;
        sop.ReadAt = DateTimeOffset.UtcNow;
        sop.ReadByUserId = userId;
        await db.SaveChangesAsync();
        return sop;
    }

    [Authorize]
    // ── 画像を未読に戻す（誤って既読にしてしまった場合の取り消し等を想定） ──
    public async Task<UserSop> MarkInstanceAsUnreadAsync(
        string sopInstanceUid,
        [Service] DicomDbContext db)
    {
        var sop = await FindSopOrThrowAsync(sopInstanceUid, db);
        sop.IsRead = false;
        sop.ReadAt = null;
        sop.ReadByUserId = null;
        await db.SaveChangesAsync();
        return sop;
    }

    private static async Task<UserSop> FindSopOrThrowAsync(string sopInstanceUid, DicomDbContext db)
    {
        var sop = await db.UserSops.FirstOrDefaultAsync(s => s.SopInstanceUid == sopInstanceUid);
        return sop ?? throw new GraphQLException(
            $"指定されたSOP Instance UIDの画像が見つかりません: {sopInstanceUid}");
    }

    // ======================================================
    // 変更の保存（並べ替え + インライン編集の統合Mutation）
    // ======================================================
    // フロントエンドは編集内容をローカルにだけ貯めておき、1つの保存ボタンを
    // 押したときにまとめて送る方式（送られてくるのは「実際に変更された行だけ」）。
    //
    // 並べ替え（Order変更）は全ユーザーに見える表示順を変える操作のため、
    // 管理者アカウントのみに許可する。統合後のMutationは1本だが、変更リストの中に
    // Order指定を含む行が1つでもあれば、その回の呼び出し全体を管理者チェック対象にする。
    // フィールド編集（DICOMタグと整合性が取れなくてもいい）はログイン済みなら誰でも呼べる。
    [Authorize]
    public Task<int> SaveStudyChangesAsync(
        List<StudyChangeInput> changes,
        [Service] DicomDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor) =>
        ApplyChangesAsync(db.UserStudies, changes, db, httpContextAccessor, (study, change) =>
        {
            if (change.PatientId is not null) study.PatientId = change.PatientId;
            if (change.PatientName is not null) study.PatientName = change.PatientName;
            if (change.StudyDate is not null) study.StudyDate = change.StudyDate.Value;
            if (change.StudyDescription is not null) study.StudyDescription = change.StudyDescription;
            if (change.Modality is not null) study.Modality = change.Modality;
        });

    [Authorize]
    public Task<int> SaveSeriesChangesAsync(
        List<SeriesChangeInput> changes,
        [Service] DicomDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor) =>
        ApplyChangesAsync(db.UserSeries, changes, db, httpContextAccessor, (series, change) =>
        {
            if (change.SeriesNumber is not null) series.SeriesNumber = change.SeriesNumber;
            if (change.SeriesDescription is not null) series.SeriesDescription = change.SeriesDescription;
            if (change.Modality is not null) series.Modality = change.Modality;
        });

    [Authorize]
    public Task<int> SaveSopChangesAsync(
        List<SopChangeInput> changes,
        [Service] DicomDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor) =>
        ApplyChangesAsync(db.UserSops, changes, db, httpContextAccessor, (sop, change) =>
        {
            if (change.InstanceNumber is not null) sop.InstanceNumber = change.InstanceNumber;
        });

    private static async Task<int> ApplyChangesAsync<TEntity, TChange>(
        DbSet<TEntity> set,
        List<TChange> changes,
        DicomDbContext db,
        IHttpContextAccessor httpContextAccessor,
        Action<TEntity, TChange> applyFields)
        where TEntity : class, IOrderable
        where TChange : IChangeInput
    {
        if (changes.Count == 0) return 0;

        var hasOrderChange = changes.Any(c => c.Order is not null);
        if (hasOrderChange && httpContextAccessor.HttpContext?.User.IsInRole("Admin") != true)
        {
            throw new GraphQLException("並べ替えの保存は管理者のみ可能です。");
        }

        // 学習用プロジェクト規模のデータ量を前提に、対象テーブルを全件メモリに載せてから
        // UIDでの引き当てを行っている（件数が増えた場合はWHERE句での絞り込みを検討）。
        var entities = await set.ToListAsync();
        var byKey = entities.ToDictionary(e => ((IOrderable)e).ReorderKey);

        var matched = 0;
        foreach (var change in changes)
        {
            if (!byKey.TryGetValue(change.Key, out var entity)) continue;
            if (change.Order is not null) entity.Order = change.Order.Value;
            applyFields(entity, change);
            matched++;
        }

        await db.SaveChangesAsync();
        return matched;
    }

    // ======================================================
    // DICOMタグへの復元
    // ======================================================
    // SaveXxxChangesAsyncで編集した値を、実際にアップロードされたDICOMファイルのタグ値に戻す。
    // 「編集前の値」を別カラムに保持しているわけではなく、都度実ファイルを読み直して復元する
    // （DicomTagRevertService.RevertXxxTagsAsync参照）。この機能は確定済みストレージの
    // ファイルを読むだけの読み取り専用処理のため、Temporalワークフロー化はしていない
    // （書き込み処理ではなく、リトライで守るべき副作用が無いため）。
    // 編集権限と対称にするため、ログイン済みなら誰でも呼べる（[Authorize]のみ、Admin限定にはしない）。
    [Authorize]
    public async Task<UserStudy> RevertStudyFieldsAsync(
        string studyInstanceUid,
        [Service] DicomDbContext db,
        [Service] DicomTagRevertService revertService)
    {
        var study = await db.UserStudies
            .Include(s => s.Series)
            .ThenInclude(se => se.Sops)
            .FirstOrDefaultAsync(s => s.StudyInstanceUid == studyInstanceUid)
            ?? throw new GraphQLException($"指定された検査が見つかりません: {studyInstanceUid}");

        var anyFilePath = study.Series.SelectMany(se => se.Sops).Select(sop => sop.FilePath).FirstOrDefault()
            ?? throw new GraphQLException("この検査には画像が1件も無いため、DICOMタグを復元できません。");

        await revertService.RevertStudyTagsAsync(study, anyFilePath);
        await db.SaveChangesAsync();
        return study;
    }

    [Authorize]
    public async Task<UserSeries> RevertSeriesFieldsAsync(
        string seriesInstanceUid,
        [Service] DicomDbContext db,
        [Service] DicomTagRevertService revertService)
    {
        var series = await db.UserSeries
            .Include(se => se.Sops)
            .FirstOrDefaultAsync(se => se.SeriesInstanceUid == seriesInstanceUid)
            ?? throw new GraphQLException($"指定されたシリーズが見つかりません: {seriesInstanceUid}");

        var anyFilePath = series.Sops.Select(sop => sop.FilePath).FirstOrDefault()
            ?? throw new GraphQLException("このシリーズには画像が1件も無いため、DICOMタグを復元できません。");

        await revertService.RevertSeriesTagsAsync(series, anyFilePath);
        await db.SaveChangesAsync();
        return series;
    }

    [Authorize]
    public async Task<UserSop> RevertSopFieldsAsync(
        string sopInstanceUid,
        [Service] DicomDbContext db,
        [Service] DicomTagRevertService revertService)
    {
        var sop = await FindSopOrThrowAsync(sopInstanceUid, db);
        await revertService.RevertSopTagsAsync(sop);
        await db.SaveChangesAsync();
        return sop;
    }

    // ======================================================
    // 削除（DeleteDicomWorkflow経由の非同期削除）
    // ======================================================
    // 【dicom-tool(旧)からの変更点】
    // 旧実装はここで直接「DB即時削除（EF CoreのCascadeで子も削除）+ ディスク上の実ファイル削除」を
    // 同期的に行っていた。dicom-tool-3ではストレージ操作・DB操作の両方をTemporalワークフロー
    // （DeleteDicomWorkflow）に委譲する（docs/CONTRACT.md 3章）。
    // このMutationは「削除対象を特定し、削除に必要な情報（対象種別・UID・ストレージ相対パス）を
    // 組み立ててワークフローを起動する」ところまでしか行わない。DBレコードの削除も
    // Worker側のDeleteRecordActivityが後から行うため、ここではdb.Remove/SaveChangesAsyncを
    // 呼ばない点に注意（呼んでしまうと、ワークフロー失敗時にDBだけ消えてストレージにファイルが
    // 残る「見えない孤児ファイル」が発生し、Temporalのリトライで救えなくなってしまう）。
    // 戻り値もこれに合わせて「受理しました」を意味するDicomDeleteAcceptedResultに変更した。
    // 削除は取り消せない操作のため、旧実装と同様に管理者アカウントのみに限定している。
    [Authorize(Roles = ["Admin"])]
    public async Task<DicomDeleteAcceptedResult> DeleteStudyAsync(
        string studyInstanceUid,
        [Service] DicomDbContext db,
        [Service] ITemporalClient temporalClient)
    {
        var study = await db.UserStudies.FirstOrDefaultAsync(s => s.StudyInstanceUid == studyInstanceUid)
            ?? throw new GraphQLException($"指定された検査が見つかりません: {studyInstanceUid}");

        // Study削除時のストレージ相対パスは "{StudyUid}" ディレクトリそのもの
        // （docs/CONTRACT.md 6章の保存規約 {StudyUID}/{SeriesUID}/{SOPUID}.dcm の最上位階層）。
        var input = new DeleteDicomWorkflowInput(
            DicomDeleteTargetType.Study,
            study.StudyInstanceUid,
            study.StudyInstanceUid);

        return await StartDeleteWorkflowAsync(temporalClient, input);
    }

    [Authorize(Roles = ["Admin"])]
    public async Task<DicomDeleteAcceptedResult> DeleteSeriesAsync(
        string seriesInstanceUid,
        [Service] DicomDbContext db,
        [Service] ITemporalClient temporalClient)
    {
        var series = await db.UserSeries
            .Include(se => se.Study)
            .FirstOrDefaultAsync(se => se.SeriesInstanceUid == seriesInstanceUid)
            ?? throw new GraphQLException($"指定されたシリーズが見つかりません: {seriesInstanceUid}");

        // Series削除時のストレージ相対パスは "{StudyUid}/{SeriesUid}" ディレクトリ。
        var relativeStoragePath = $"{series.Study!.StudyInstanceUid}/{series.SeriesInstanceUid}";
        var input = new DeleteDicomWorkflowInput(
            DicomDeleteTargetType.Series,
            series.SeriesInstanceUid,
            relativeStoragePath);

        return await StartDeleteWorkflowAsync(temporalClient, input);
    }

    [Authorize(Roles = ["Admin"])]
    public async Task<DicomDeleteAcceptedResult> DeleteSopAsync(
        string sopInstanceUid,
        [Service] DicomDbContext db,
        [Service] ITemporalClient temporalClient)
    {
        var sop = await FindSopOrThrowAsync(sopInstanceUid, db);

        // Sop削除時のストレージ相対パスは UserSop.FilePath そのもの（ファイル1つを指す）。
        var input = new DeleteDicomWorkflowInput(
            DicomDeleteTargetType.Sop,
            sop.SopInstanceUid,
            sop.FilePath);

        return await StartDeleteWorkflowAsync(temporalClient, input);
    }

    private static async Task<DicomDeleteAcceptedResult> StartDeleteWorkflowAsync(
        ITemporalClient temporalClient,
        DeleteDicomWorkflowInput input)
    {
        var workflowId = $"delete-{input.TargetType}-{Guid.NewGuid():N}";

        // アップロード時と同様、文字列ベースの疎結合クライアントAPIでワークフローを起動する
        // （docs/CONTRACT.md 4章。DeleteDicomWorkflowの実装クラスはこのApiから参照しない）。
        await temporalClient.StartWorkflowAsync(
            TemporalConstants.DeleteDicomWorkflowTypeName,
            [input],
            new WorkflowOptions(id: workflowId, taskQueue: TemporalConstants.TaskQueue));

        return new DicomDeleteAcceptedResult
        {
            TargetType = input.TargetType.ToString(),
            TargetUid = input.TargetUid,
            WorkflowId = workflowId,
            Accepted = true,
            Message = "削除を受け付けました。ストレージ・DBからの削除はバックグラウンドで実行中です。",
        };
    }
}
