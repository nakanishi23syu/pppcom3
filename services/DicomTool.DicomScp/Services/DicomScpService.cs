using System.Text;
using DicomTool.Shared.Constants;
using DicomTool.Shared.Contracts;
using DicomTool.Shared.Entities;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace DicomTool.DicomScp.Services;

// ==========================================================================================
// DicomScpService ― DICOM SCP(Service Class Provider＝受信側/サーバー)本体
// ==========================================================================================
//
// 【SCU/SCPという用語について】
// DICOMの世界では通信の役割を「クライアント/サーバー」ではなく「SCU(Service Class User)」と
// 「SCP(Service Class Provider)」と呼ぶ。C-STOREというサービスクラスにおいては、
// 画像を送る側(送信元モダリティ等)が「C-STORE SCU」、画像を受け取る側(PACS等)が「C-STORE SCP」となる。
// このクラスは「C-ECHO SCP」でもあり「C-STORE SCP」でもある(=両方のサービスクラスの受信側を兼ねる)。
//
// 【全体の流れ】
// 1台の外部モダリティ(またはこのリポジトリ自身のSCU自己疎通テスト)がTCP接続してくるたびに、
// fo-dicomの DicomServer が「1コネクション = 1インスタンス」の原則でこのクラスの
// 新しいインスタンスを生成する（つまりこのクラスはコネクションごとの状態＝アソシエーションの
// 状況を安全にフィールドへ持ってよい）。
//
// このクラスが実装している4つの型の役割:
//   ・DicomService(基底クラス) … DICOM Upper Layer Protocolの生バイト列の送受信、
//                                 PDU(Protocol Data Unit)の組み立て/解析など「配管」部分をやってくれる。
//   ・IDicomServiceProvider    … アソシエーション確立/解放/切断など、接続そのもののライフサイクルを扱う。
//   ・IDicomCEchoProvider      … C-ECHO(疎通確認)サービスクラスの受信側としての振る舞いを定義する。
//   ・IDicomCStoreProvider     … C-STORE(画像保存)サービスクラスの受信側としての振る舞いを定義する。
public class DicomScpService :
    DicomService, IDicomServiceProvider, IDicomCEchoProvider, IDicomCStoreProvider,
    IDicomCFindProvider, IDicomCMoveProvider
{
    // このSCPが受け入れる転送構文(Transfer Syntax)の一覧。
    // 転送構文とは「DICOMデータセットをバイト列としてどう符号化するか」の取り決めで、
    // 代表的には次の3つ:
    //   ・Implicit VR Little Endian … タグの型(VR: Value Representation)を明示せず、
    //                                   規格書の定義から暗黙に決める。最も古くから存在し、
    //                                   すべてのDICOM機器が最低限サポートを義務付けられている
    //                                   「デフォルト転送構文」。
    //   ・Explicit VR Little Endian … タグごとにVRを明示的にバイト列へ含める。曖昧さがなく現代的。
    //   ・Explicit VR Big Endian    … 上記のバイトオーダーをビッグエンディアンにしたもの
    //                                   (規格上は非推奨扱いだが後方互換のため残っている)。
    // 実務のPACSはこれに加えてJPEG等の圧縮転送構文も並べるが、本プロジェクトは学習用に
    // 「非圧縮のみ」に絞ってシンプルさを優先している。
    private static readonly DicomTransferSyntax[] AcceptedTransferSyntaxes =
    [
        DicomTransferSyntax.ExplicitVRLittleEndian,
        DicomTransferSyntax.ExplicitVRBigEndian,
        DicomTransferSyntax.ImplicitVRLittleEndian,
    ];

    private readonly ILogger<DicomScpService> _appLogger;
    private readonly ITemporalWorkflowStarter _workflowStarter;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IDicomQueryService _queryService;
    private readonly IRemoteAeRegistry _remoteAeRegistry;
    private readonly IDicomClientFactory _dicomClientFactory;

    // ------------------------------------------------------------------------------------
    // コンストラクタ
    // ------------------------------------------------------------------------------------
    // fo-dicomのDI統合(Services/DicomScpHostedService.cs で AddFellowOakDicom() 経由で登録)は、
    // 「stream / fallbackEncoding / log / dependencies」というDicomServer側が用意する4引数に加えて、
    // ASP.NET CoreのDIコンテナに登録済みの任意のサービス(ここではILogger<T>・Temporal起動役・
    // IHostEnvironment・DBクエリ役・リモートAE登録簿・SCUクライアントファクトリ)を
    // コンストラクタインジェクションできる。DicomDbContext(IDicomQueryService経由)はScopedだが、
    // fo-dicomは「1接続ごとに新しいDIスコープ」も一緒に作ってくれるため、Scopedサービスを
    // そのままコンストラクタインジェクションしても安全（=1接続=1トランザクション境界、という
    // ASP.NET CoreのHTTPリクエストと同じ感覚で扱える）。
    public DicomScpService(
        INetworkStream stream,
        Encoding fallbackEncoding,
        ILogger log,
        DicomServiceDependencies dependencies,
        ILogger<DicomScpService> appLogger,
        ITemporalWorkflowStarter workflowStarter,
        IHostEnvironment hostEnvironment,
        IDicomQueryService queryService,
        IRemoteAeRegistry remoteAeRegistry,
        IDicomClientFactory dicomClientFactory)
        : base(stream, fallbackEncoding, log, dependencies)
    {
        _appLogger = appLogger;
        _workflowStarter = workflowStarter;
        _hostEnvironment = hostEnvironment;
        _queryService = queryService;
        _remoteAeRegistry = remoteAeRegistry;
        _dicomClientFactory = dicomClientFactory;
    }

    // ======================================================================================
    // アソシエーション確立: A-ASSOCIATE-RQ を受け取ったときに呼ばれる
    // ======================================================================================
    // 「アソシエーション」とは、DICOMにおけるTCPコネクション上に張られる論理的なセッションのこと。
    // 生のTCP接続が確立しただけではDICOM的にはまだ何もできず、SCU側が最初に送ってくる
    // A-ASSOCIATE-RQ(アソシエーション確立要求)の中身をSCP側が検証し、
    // A-ASSOCIATE-AC(受諾)かA-ASSOCIATE-RJ(拒否)のどちらかを返して初めて、
    // C-ECHOやC-STOREなどの実際のサービスをやり取りできるようになる。
    //
    // A-ASSOCIATE-RQには主に次の情報が入っている:
    //   ・Calling AE Title … 接続してきた側(SCU)が名乗るAEタイトル。
    //   ・Called AE Title  … 接続先として指定されたAEタイトル(＝このSCPが「自分宛てか」を判定する材料)。
    //   ・Presentation Context(プレゼンテーションコンテキスト)の一覧
    //       … 「どのSOP Class(例: C-ECHOやCTイメージ保存)を」「どの転送構文の候補で」やり取りしたいか、
    //         という提案のリスト。1つのアソシエーションの中で複数のSOP Classを同時に提案できる。
    public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        _appLogger.LogInformation(
            "アソシエーション要求を受信しました。CallingAE={CallingAE}, CalledAE={CalledAE}, RemoteHost={RemoteHost}",
            association.CallingAE, association.CalledAE, association.RemoteHost);

        foreach (var pc in association.PresentationContexts)
        {
            _appLogger.LogInformation(
                "  PresentationContext: AbstractSyntax(SOP Class)={AbstractSyntax}, 提案された転送構文=[{TransferSyntaxes}]",
                pc.AbstractSyntax,
                string.Join(", ", pc.GetTransferSyntaxes()));
        }

        // --------------------------------------------------------------------------------
        // 【学習ポイント】AEタイトルが接続可否を左右する
        // --------------------------------------------------------------------------------
        // DICOMはIPアドレス/ポートに加えて「Called AE Title」という文字列も一致させないと
        // 接続を受け付けない、という仕組みを持つ(実務でも「相手のAEタイトルを1文字間違えて
        // 接続できない」というトラブルは非常によくある)。
        // ここではあえて「DicomNetworkConstants.OwnAeTitle と一致しないCalled AEは拒否する」
        // 実装を入れて、この仕組みをコード上で体験できるようにしている
        // (fo-dicom自体はデフォルトでは何もチェックせず全AEタイトルを受け入れるため、
        //  このチェックは呼び出し側=このメソッドで明示的に行う必要がある)。
        if (association.CalledAE != DicomNetworkConstants.OwnAeTitle)
        {
            _appLogger.LogWarning(
                "Called AE Title が自システムのAEタイトル({OwnAeTitle})と一致しないため、アソシエーションを拒否します。要求されたCalledAE={CalledAE}",
                DicomNetworkConstants.OwnAeTitle, association.CalledAE);

            // A-ASSOCIATE-RJ(拒否)を返す。
            //   Result   : Permanent = リトライしても無駄な恒久的拒否(一時的な混雑等ではない)であることを示す。
            //   Source   : ServiceUser = 拒否の原因はアプリケーション層(＝AEタイトル不一致というこちらの都合)側にあることを示す。
            //   Reason   : CalledAENotRecognized = 「あなたが指定したCalled AE Titleにはお心当たりがありません」という理由コード。
            return SendAssociationRejectAsync(
                DicomRejectResult.Permanent,
                DicomRejectSource.ServiceUser,
                DicomRejectReason.CalledAENotRecognized);
        }

        // --------------------------------------------------------------------------------
        // プレゼンテーションコンテキストごとに転送構文をネゴシエーションする。
        // --------------------------------------------------------------------------------
        // AcceptTransferSyntaxes(...) は「SCU側が提案してきた転送構文の候補」と
        // 「このSCPがAcceptedTransferSyntaxesとして対応できる転送構文」の積集合を取り、
        // 最初に一致したものをそのプレゼンテーションコンテキストの「確定転送構文」として採用する
        // (どのSOP Classについても、この後のデータはこの確定した転送構文でエンコードされる)。
        // 共通の転送構文が1つも無ければ、そのプレゼンテーションコンテキストは自動的に
        // 「RejectTransferSyntaxesNotSupported」として個別に拒否扱いになる
        // (アソシエーション全体は拒否されず、そのSOP Classだけ使えない、という粒度で失敗できるのがミソ)。
        foreach (var pc in association.PresentationContexts)
        {
            pc.AcceptTransferSyntaxes(AcceptedTransferSyntaxes);
        }

        // A-ASSOCIATE-AC(受諾)を返す。ここでようやくC-ECHO/C-STORE等の実サービスが開始できる。
        return SendAssociationAcceptAsync(association);
    }

    // ======================================================================================
    // アソシエーション解放: A-RELEASE-RQ を受け取ったときに呼ばれる
    // ======================================================================================
    // SCU側が「もうこのアソシエーションで送るものは無い」と判断すると、TCP接続を素のまま
    // 切るのではなく、A-RELEASE-RQ/RPというお行儀の良い手順(ハンドシェイク)でセッションを
    // 正式に終了させる。ここでは何も特別なことはせず、解放応答を返すだけでよい。
    public Task OnReceiveAssociationReleaseRequestAsync()
    {
        _appLogger.LogInformation("アソシエーション解放要求(A-RELEASE-RQ)を受信しました。解放応答(A-RELEASE-RP)を返します。");
        return SendAssociationReleaseResponseAsync();
    }

    // 通信相手が A-ABORT (異常終了)を送ってきた場合、またはこちらから中断すべきと判断した場合に呼ばれる。
    // ネットワーク断・相手側のクラッシュ等、正常なA-RELEASEを経ない終了パターン。
    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason)
    {
        _appLogger.LogWarning("アソシエーションがAbortされました。Source={Source}, Reason={Reason}", source, reason);
    }

    // TCPコネクションそのものが閉じたときに呼ばれる(正常なA-RELEASE後もAbort後も、最終的にここへ来る)。
    public void OnConnectionClosed(Exception? exception)
    {
        if (exception is null)
        {
            _appLogger.LogInformation("コネクションが正常に閉じられました。");
        }
        else
        {
            _appLogger.LogWarning(exception, "コネクションが異常終了しました。");
        }
    }

    // ======================================================================================
    // C-ECHO ― いわゆる「DICOM Ping」
    // ======================================================================================
    // C-ECHOは、DICOM規格上「Verification SOP Class」というサービスクラスに属する。
    // このサービスの目的はただ1つ、「アソシエーションが正しく確立でき、相手が生きていて、
    // ちゃんとDIMSE応答を返せるか」を確認することだけであり、画像データや患者情報など
    // 一切の実データをやり取りしない(だから「Ping」と呼ばれる)。
    // PACS同士の疎通確認、モダリティの導通試験など、実務でも真っ先に叩かれる基本コマンド。
    // ここでは要求を受けたら即座に DicomStatus.Success を返すだけでよい
    // (Success以外を返す状況は通常想定しない＝「相手が生きているか」の確認なので、
    //  応答を返せている時点でほぼ必ずSuccessになる)。
    public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request)
    {
        _appLogger.LogInformation("C-ECHO要求を受信しました。応答としてDicomStatus.Successを返します。");
        return Task.FromResult(new DicomCEchoResponse(request, DicomStatus.Success));
    }

    // ======================================================================================
    // C-STORE ― 画像(データセット)本体の送信サービス
    // ======================================================================================
    // C-STOREはVerification(C-ECHO)と違い、実際のDICOMデータセット(患者情報・検査情報・
    // 画素データ等をすべて含むファイル1つ分)を丸ごと送りつけてくるサービスクラス。
    // request.File には、送られてきたデータセット全体が fo-dicom の DicomFile として
    // 既にパース済みの状態で渡ってくる(PDUの分割受信・再構成はfo-dicomが面倒を見てくれている)。
    public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
    {
        // --------------------------------------------------------------------------------
        // 【学習ポイント】なぜここでDBに保存せず、ステージングして「起動だけ」するのか
        // --------------------------------------------------------------------------------
        // DICOM(DIMSE)の作法として、C-STORE要求を受けたSCPは「その処理結果(成功/失敗)」を
        // 同じアソシエーションの中でC-STORE応答(C-STORE-RSP)として速やかに返す必要がある。
        // これは「相手(SCU)はこの応答を待って次の画像を送り続ける/中止するかを判断する」
        // 同期的なプロトコルだからであり、応答が遅いと大量の画像を送るモダリティ側の
        // スループットが著しく落ちる(最悪タイムアウトでアソシエーションごと切断される)。
        //
        // 一方で、docs/CONTRACT.md 2章にある「正式ストレージへの配置(タグ解析してStudy/Series/SOP
        // ディレクトリへ移動)」や「PostgreSQLへのレコード登録」は、ディスクI/OやDB接続を伴う
        // 時間のかかる処理であり、かつそれぞれが独立して失敗しうる(ディスクフル、DB接続断等)。
        // これをC-STORE応答を返す前にこの場で同期的にやってしまうと、
        //   ・処理が遅い分だけSCU側の送信がブロックされる
        //   ・DB登録が失敗した場合にDICOM層としてどう応答すべきか設計が難しくなる
        //     (「ファイルは受信できたがDB登録は失敗した」という中途半端な状態をDICOMの
        //      ステータスコード1つでは表現しづらい)
        // という問題が起きる。
        //
        // そこでこのSCPは「受信したファイルをステージング領域にそのまま書き込む」ところまでだけを
        // 自分の責務とし、それが終わった時点で即座にC-STORE応答(Success)を返す。
        // 「タグ解析して正式パスへ配置する」「DBへ登録する」という重い処理は、Temporalワークフロー
        // (UploadDicomWorkflow、実装本体はservices/DicomTool.Worker)へ"起動を依頼するだけ"にして、
        // 実行そのものはアソシエーションの外(非同期)で行う。
        // これにより「DICOM層の応答速度」と「後続処理の信頼性(リトライ等)」をそれぞれ
        // 別の仕組み(DIMSEの応答 / Temporalのワークフロー実行)に分担させている。
        var incomingDir = StoragePaths.ResolveIncomingPath(_hostEnvironment.ContentRootPath);
        Directory.CreateDirectory(incomingDir);

        // SOPInstanceUID(Service-Object Pair Instance UID)は、DICOMデータセット1つ(=1枚の画像や
        // 1系列の非画像オブジェクト)を全世界で一意に識別するID。ファイル名として使うのに最適。
        var sopInstanceUid = request.SOPInstanceUID.UID;
        var stagingFileName = $"{sopInstanceUid}.dcm";
        var stagingFilePath = Path.Combine(incomingDir, stagingFileName);

        _appLogger.LogInformation(
            "C-STORE要求を受信しました。SOPInstanceUID={SopInstanceUid}, SOPClassUID={SopClassUid}, 保存先={StagingFilePath}",
            sopInstanceUid, request.SOPClassUID, stagingFilePath);

        await request.File.SaveAsync(stagingFilePath).ConfigureAwait(false);

        // Temporalへワークフロー起動を"依頼"する。ここで待つのは「起動リクエストがTemporal Serverに
        // 受理されたこと」までであり、UploadDicomWorkflow自体の実行完了(ストレージ確定・DB登録)は待たない。
        var input = new UploadDicomWorkflowInput(
            StagingFileName: stagingFileName,
            OriginalFileName: stagingFileName,
            SourceProtocol: "DICOM_CSTORE");

        try
        {
            await _workflowStarter.StartUploadDicomWorkflowAsync(input).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Temporal Serverが未起動、あるいはWorkerがまだ実装されていない、といった状況でも
            // 「ファイル自体は受信できている(ステージング領域には確かに書き込まれている)」ため、
            // ここでは例外を握りつぶしてログに残すだけにし、C-STORE応答はSuccessのまま返す。
            // (fo-dicom/DICOM層からすると「受信」は成功しているので、これは妥当な判断)
            _appLogger.LogError(
                ex,
                "UploadDicomWorkflowの起動に失敗しました。ファイル自体はステージング領域への保存に成功しています。StagingFileName={StagingFileName}",
                stagingFileName);
        }

        return new DicomCStoreResponse(request, DicomStatus.Success);
    }

    // C-STORE処理中(主にrequest.File保存待ちの間)に例外が起きた場合にfo-dicom内部から呼ばれる
    // (例: 受信途中でストリームが切れた、一時ファイルの書き込みに失敗した等)。
    public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e)
    {
        _appLogger.LogError(e, "C-STORE要求の処理中に例外が発生しました。一時ファイル={TempFileName}", tempFileName);
        return Task.CompletedTask;
    }

    // ======================================================================================
    // C-FIND ― 検索サービス
    // ======================================================================================
    // C-FINDは「1回の要求」に対して「複数件のPending応答(1件ずつ、マッチしたレコード1つ分の
    // タグを載せて)」を返し、最後に1回だけFinal応答(Success)を返す、という応答の形が
    // C-ECHO/C-STOREと大きく異なる(IAsyncEnumerableで表現するのはこのため)。
    // このプロジェクトではSTUDY階層・SERIES階層のみ対応する(PATIENT/IMAGE階層は非対応。
    // docs/dicom-testing-tools/dcmtk.md参照)。検索条件の解釈自体はDicomQueryService(C-MOVEとも
    // 共通)に委譲し、ここでは「DBの検索結果をDICOMの応答データセットに詰め替える」ことに専念する。
    public async IAsyncEnumerable<DicomCFindResponse> OnCFindRequestAsync(DicomCFindRequest request)
    {
        var cancellationToken = CancellationToken.None;
        _appLogger.LogInformation("C-FIND要求を受信しました。Level={Level}", request.Level);

        if (request.Level == DicomQueryRetrieveLevel.Study)
        {
            var studies = await _queryService.FindStudiesAsync(request.Dataset, cancellationToken).ConfigureAwait(false);
            _appLogger.LogInformation("C-FIND(STUDY階層)がマッチしたStudy件数={Count}", studies.Count);
            foreach (var study in studies)
            {
                yield return new DicomCFindResponse(request, DicomStatus.Pending) { Dataset = BuildStudyResponseDataset(study) };
            }
        }
        else if (request.Level == DicomQueryRetrieveLevel.Series)
        {
            var seriesList = await _queryService.FindSeriesAsync(request.Dataset, cancellationToken).ConfigureAwait(false);
            _appLogger.LogInformation("C-FIND(SERIES階層)がマッチしたSeries件数={Count}", seriesList.Count);
            foreach (var series in seriesList)
            {
                yield return new DicomCFindResponse(request, DicomStatus.Pending) { Dataset = BuildSeriesResponseDataset(series) };
            }
        }
        else
        {
            // PATIENT/IMAGE階層は未対応。0件ヒットとして扱い、Successで終える
            // (エラーにはしない＝「対応していない検索軸だが、要求自体は正しく処理できた」という扱い)。
            _appLogger.LogWarning("未対応のQuery/Retrieve階層のC-FIND要求です。Level={Level}", request.Level);
        }

        yield return new DicomCFindResponse(request, DicomStatus.Success);
    }

    // ======================================================================================
    // C-MOVE ― 検索結果を指定した宛先AEへC-STOREで転送させるサービス
    // ======================================================================================
    // C-MOVEはC-FINDと同じ検索条件の書式を使うが、応答としてタグ一覧を返す代わりに、
    // このSCP自身がSCUの顔になって(=自分のAEタイトルを名乗って)、マッチしたファイルを
    // request.DestinationAEへC-STOREで送り届ける、という「受信も送信もする」サービスクラス。
    // 転送先AEタイトルからホスト/ポートを引く手段が必要になるため、RemoteAeRegistry
    // (appsettings.jsonの"RemoteAeTitles"セクション。docs/dicom-testing-tools/dcmtk.md参照)を使う。
    public async IAsyncEnumerable<DicomCMoveResponse> OnCMoveRequestAsync(DicomCMoveRequest request)
    {
        var cancellationToken = CancellationToken.None;
        _appLogger.LogInformation(
            "C-MOVE要求を受信しました。Level={Level}, DestinationAE={DestinationAE}",
            request.Level, request.DestinationAE);

        if (!_remoteAeRegistry.TryResolve(request.DestinationAE, out var destHost, out var destPort))
        {
            _appLogger.LogWarning(
                "C-MOVEの転送先AE '{DestinationAE}' がappsettings.jsonのRemoteAeTitlesに未登録のため、失敗を返します。",
                request.DestinationAE);
            yield return new DicomCMoveResponse(request, DicomStatus.QueryRetrieveMoveDestinationUnknown);
            yield break;
        }

        List<UserSop> sopsToSend;
        if (request.Level == DicomQueryRetrieveLevel.Study)
        {
            var studies = await _queryService.FindStudiesAsync(request.Dataset, cancellationToken).ConfigureAwait(false);
            sopsToSend = studies.SelectMany(s => s.Series).SelectMany(se => se.Sops).ToList();
        }
        else if (request.Level == DicomQueryRetrieveLevel.Series)
        {
            var seriesList = await _queryService.FindSeriesAsync(request.Dataset, cancellationToken).ConfigureAwait(false);
            sopsToSend = seriesList.SelectMany(se => se.Sops).ToList();
        }
        else
        {
            _appLogger.LogWarning("未対応のQuery/Retrieve階層のC-MOVE要求です。Level={Level}", request.Level);
            sopsToSend = [];
        }

        _appLogger.LogInformation("C-MOVE対象のSOPInstance件数={Count}", sopsToSend.Count);

        if (sopsToSend.Count == 0)
        {
            yield return new DicomCMoveResponse(request, DicomStatus.Success)
            {
                Remaining = 0,
                Completed = 0,
                Failures = 0,
            };
            yield break;
        }

        var storageRoot = StoragePaths.ResolveStoragePath(_hostEnvironment.ContentRootPath);

        // 転送先へは、このSCP自身が(受信側ではなく)SCUとして自分のAEタイトルを名乗って接続する。
        // DicomScuTestService.csと同じDicomClientFactory経由の使い方(1アソシエーション内に
        // 複数のC-STORE要求をまとめて積んで、最後に1回SendAsyncする＝連続転送)。
        var client = _dicomClientFactory.Create(
            destHost, destPort, useTls: false,
            callingAe: DicomNetworkConstants.OwnAeTitle,
            calledAe: request.DestinationAE);

        var completed = 0;
        var failures = 0;

        // 【学習ポイント】yield returnはtry/catch(catch節を持つtryブロック)の中には書けない
        // (C#の言語仕様上の制約)。そのため「転送先への接続/送信で例外が起きた」という事実だけを
        // tryの外へ持ち出し、応答をyield returnするのはtry/catchを抜けた後にする。
        // これが無いと、Orthanc等の宛先にファイアウォール等で到達できない場合に例外がそのまま
        // このメソッドの外(fo-dicomの受信ループ)まで伝播し、C-MOVEを要求してきたSCU側との
        // アソシエーションごと強制切断されてしまう(元々のバグ。実際にOrthancへのポートが
        // ファイアウォールで塞がっていた際に発生を確認した)。C-MOVEの作法としては、
        // 転送先に届かなかった場合も「届かなかった」という結果をC-MOVE応答として返すのが正しい。
        Exception? transferException = null;
        try
        {
            foreach (var sop in sopsToSend)
            {
                var filePath = Path.Combine(storageRoot, sop.FilePath);
                if (!File.Exists(filePath))
                {
                    _appLogger.LogWarning(
                        "C-MOVE対象ファイルがストレージに見つかりません。SOPInstanceUID={SopInstanceUid}, Path={FilePath}",
                        sop.SopInstanceUid, filePath);
                    failures++;
                    continue;
                }

                var dicomFile = await DicomFile.OpenAsync(filePath).ConfigureAwait(false);
                var storeRequest = new DicomCStoreRequest(dicomFile)
                {
                    OnResponseReceived = (_, response) =>
                    {
                        if (response.Status == DicomStatus.Success)
                        {
                            completed++;
                        }
                        else
                        {
                            failures++;
                        }
                    },
                };
                await client.AddRequestAsync(storeRequest).ConfigureAwait(false);
            }

            await client.SendAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            transferException = ex;
        }

        if (transferException is not null)
        {
            _appLogger.LogError(
                transferException,
                "C-MOVEの転送先への接続/送信でエラーが発生しました。DestinationAE={DestinationAE} ({Host}:{Port})",
                request.DestinationAE, destHost, destPort);
            yield return new DicomCMoveResponse(request, DicomStatus.QueryRetrieveUnableToPerformSuboperations)
            {
                Remaining = 0,
                Completed = completed,
                Failures = sopsToSend.Count - completed,
            };
            yield break;
        }

        _appLogger.LogInformation(
            "C-MOVEが完了しました。DestinationAE={DestinationAE}, Completed={Completed}, Failures={Failures}",
            request.DestinationAE, completed, failures);

        var finalStatus = failures == 0 ? DicomStatus.Success : DicomStatus.QueryRetrieveSubOpsOneOrMoreFailures;
        yield return new DicomCMoveResponse(request, finalStatus)
        {
            Remaining = 0,
            Completed = completed,
            Failures = failures,
        };
    }

    // ── C-FIND応答データセットの組み立て ──
    // Orthancの実際の応答(docs/dicom-testing-tools/orthanc.md参照)に合わせ、
    // RetrieveAETitle(このSCP自身のAEタイトル＝C-MOVEの転送元として指定する時に使う情報)も含める。
    private static DicomDataset BuildStudyResponseDataset(UserStudy study)
    {
        var dataset = new DicomDataset
        {
            { DicomTag.QueryRetrieveLevel, "STUDY" },
            { DicomTag.RetrieveAETitle, DicomNetworkConstants.OwnAeTitle },
            { DicomTag.PatientID, study.PatientId },
            { DicomTag.PatientName, study.PatientName },
            { DicomTag.StudyInstanceUID, study.StudyInstanceUid },
            { DicomTag.StudyDate, study.StudyDate.ToString("yyyyMMdd") },
            { DicomTag.StudyDescription, study.StudyDescription },
            { DicomTag.AccessionNumber, study.AccessionNumber },
            { DicomTag.ModalitiesInStudy, study.Modality },
            { DicomTag.NumberOfStudyRelatedSeries, study.Series.Count.ToString() },
            { DicomTag.NumberOfStudyRelatedInstances, study.Series.Sum(se => se.Sops.Count).ToString() },
        };
        return dataset;
    }

    private static DicomDataset BuildSeriesResponseDataset(UserSeries series)
    {
        var dataset = new DicomDataset
        {
            { DicomTag.QueryRetrieveLevel, "SERIES" },
            { DicomTag.RetrieveAETitle, DicomNetworkConstants.OwnAeTitle },
            { DicomTag.SeriesInstanceUID, series.SeriesInstanceUid },
            { DicomTag.SeriesNumber, series.SeriesNumber },
            { DicomTag.SeriesDescription, series.SeriesDescription },
            { DicomTag.Modality, series.Modality },
            { DicomTag.NumberOfSeriesRelatedInstances, series.Sops.Count.ToString() },
        };
        if (series.Study is not null)
        {
            dataset.Add(DicomTag.StudyInstanceUID, series.Study.StudyInstanceUid);
        }
        return dataset;
    }
}
