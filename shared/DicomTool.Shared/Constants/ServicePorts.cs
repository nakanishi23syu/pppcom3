namespace DicomTool.Shared.Constants;

// ======================================================
// ServicePorts — 各サービスが使うポート番号の一元管理
// ======================================================
// docs/CONTRACT.md 1章と対になる定数群。appsettings.json / launchSettings.json / nuxt.config.ts /
// docker-compose.yml にはこの値をそのまま数値として書く必要があるため
// （それぞれの設定ファイル形式からC#の定数を直接参照することはできない）、
// 「値を変える時はここを書き換えてから、この定数を参照している各設定ファイルのコメントを辿って
// 手動で追従する」という運用にする。値そのものの「唯一の正」としてこのファイルを扱うこと。
public static class ServicePorts
{
    /// <summary>Backend API (GraphQL, HotChocolate) の待受ポート。</summary>
    public const int BackendApiHttp = 5030;

    /// <summary>Worklist (Nuxt3) の開発サーバーポート。</summary>
    public const int WorklistNuxt = 3100;

    /// <summary>Viewer (Nuxt3) の開発サーバーポート。</summary>
    public const int ViewerNuxt = 3200;

    /// <summary>Timeline (Blazor WebAssembly) の開発サーバーポート。</summary>
    public const int TimelineBlazor = 5230;

    /// <summary>常駐トレイアプリのローカルHTTP APIポート（localhost限定で待受）。</summary>
    public const int TrayAppHttp = 5299;

    /// <summary>DICOM SCPのDIMSE通信ポート（C-ECHO/C-STOREを受け付ける）。</summary>
    public const int DicomScpDimse = 11112;

    /// <summary>DICOM SCPサービスの管理用REST API（Swagger UI含む）のポート。</summary>
    public const int DicomScpManagementHttp = 8090;

    /// <summary>PostgreSQLのポート（docker-compose.yml参照）。</summary>
    public const int PostgresHost = 5432;

    /// <summary>Temporal Serverのフロントエンド(gRPC)ポート。C# SDKはここに接続する。</summary>
    public const int TemporalFrontendGrpc = 7233;

    /// <summary>Temporal Web UIのポート（ブラウザで実行履歴を見る用）。</summary>
    public const int TemporalWebUi = 8233;
}
