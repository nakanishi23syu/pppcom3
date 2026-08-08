using DicomTool.Shared.Constants;
using FellowOakDicom.Network;

namespace DicomTool.DicomScp.Services;

// ==========================================================================================
// DicomScpHostedService ― ASP.NET Coreのホスト起動と同時にDIMSEリスナーを立ち上げる橋渡し役
// ==========================================================================================
// ASP.NET Core (Generic Host)は「Webサーバーとして待ち受ける」役割と「バックグラウンドで
// 常駐処理を行う」役割を、同じプロセス・同じDIコンテナの中で共存させることができる。
// 後者を担うのが IHostedService (ここではその簡易実装である BackgroundService)。
//
// このクラス自身はDICOMプロトコルを何も知らず、「IDicomServerFactory を使って
// DicomScpService(実際のDIMSEロジック本体)を紐づけたTCPリスナーを起動/停止するだけ」の
// 薄いアダプタである。DicomServerは内部で「1接続=1 DicomScpServiceインスタンス」を
// 生成してくれるため、このクラスはそのライフサイクル管理に一切関与しなくてよい。
public sealed class DicomScpHostedService : BackgroundService
{
    private readonly IDicomServerFactory _dicomServerFactory;
    private readonly ILogger<DicomScpHostedService> _logger;
    private IDicomServer? _server;

    public DicomScpHostedService(IDicomServerFactory dicomServerFactory, ILogger<DicomScpHostedService> logger)
    {
        _dicomServerFactory = dicomServerFactory;
        _logger = logger;
    }

    // BackgroundServiceの標準の流れでは ExecuteAsync が「常駐処理そのもの」を書く場所だが、
    // DicomServer.Create(...) 自体が内部で非同期にTCPリスナーを立ち上げて即座に制御を返す
    // (＝呼び出し元をブロックしない)ため、StartAsyncの中でサーバーを生成するだけでよい。
    // ExecuteAsyncは「サービスが動き続けていること」を表すためだけにTask.CompletedTaskを返す
    // (実際の待受処理はDicomServer内部のバックグラウンドスレッドが行っている)。
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "DICOM SCP(DIMSE)リスナーを起動します。Port={Port}, 自AEタイトル={OwnAeTitle}",
            ServicePorts.DicomScpDimse, DicomNetworkConstants.OwnAeTitle);

        // DicomServerFactory.Create<TProvider>(port) は、以後この11112番ポートへTCP接続してくる
        // すべてのクライアントに対して、DicomScpService(TProvider)の新しいインスタンスを
        // 1接続ごとに生成して処理を委譲する「待受ループ」を裏側で開始する。
        // <DicomScpService> 型引数は「このSCPが何のサービスクラスを提供するか」の実体そのもの。
        _server = _dicomServerFactory.Create<DicomScpService>(ServicePorts.DicomScpDimse);

        return base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DICOM SCP(DIMSE)リスナーを停止します。");
        _server?.Stop();
        _server?.Dispose();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
