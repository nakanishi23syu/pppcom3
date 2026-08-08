// ======================================================
// Constants/ExternalServiceUrls.cs — 他マイクロサービス（別オリジンのフロントエンド）へのURL定数
// ======================================================
// 唯一の正は docs/CONTRACT.md 1章 と shared/DicomTool.Shared/Constants/ServicePorts.cs。
// 本来ならその ServicePorts クラスをそのまま参照したいところだが、DicomTool.Shared は
// EF Core + Npgsql（PostgreSQLへのTCPソケット通信）を抱えたサーバー専用のクラスライブラリであり、
// ブラウザのWASMサンドボックス内で動く Blazor WebAssembly からは参照できない
// （参照するとビルドが壊れるか、ソケットAPI呼び出しの箇所で実行時に失敗する）。
// そのため、このプロジェクトではポート番号をこの1ファイルにだけベタ書きし、
// 「ServicePorts.cs の値を変えたら、このファイルも手で追従する」という運用にする
// （docs/CONTRACT.md 1章と同じ考え方）。

namespace DicomTool.Timeline.Constants;

public static class ExternalServiceUrls
{
    /// <summary>Worklist(Nuxt3)のベースURL。ServicePorts.WorklistNuxt(3100)に対応。
    /// タイムライン画面右上の「検査一覧に戻る」リンクの遷移先として使う。</summary>
    public const string WorklistBaseUrl = "http://localhost:3100";

    /// <summary>Viewer(Nuxt3)のベースURL。ServicePorts.ViewerNuxt(3200)に対応。</summary>
    public const string ViewerBaseUrl = "http://localhost:3200";

    /// <summary>
    /// 指定したSeriesをViewerで開くためのURL。
    /// ======================================================
    /// Viewer(frontend/viewer)の実際のページルーティングは `app/pages/[seriesInstanceUID].vue`
    /// （トップレベル、`/viewer`接頭辞なし）で確定している。統合時にこの1箇所だけ実際の
    /// ルーティングへ合わせて修正した（設計時点では未確定だったため、この定数クラスに
    /// 集約しておいたことで影響範囲をここだけに限定できた）。
    /// </summary>
    public static string ViewerSeriesUrl(string seriesInstanceUid) =>
        $"{ViewerBaseUrl}/{Uri.EscapeDataString(seriesInstanceUid)}";
}
