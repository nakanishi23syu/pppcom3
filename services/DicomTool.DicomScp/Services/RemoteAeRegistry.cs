namespace DicomTool.DicomScp.Services;

// ==========================================================================================
// RemoteAeRegistry ― C-MOVEの転送先AEタイトルを解決するための、既知リモートAE一覧
// ==========================================================================================
// DICOMのC-MOVE要求には「転送先AEタイトル」という文字列しか含まれない(ホスト/ポートは含まれない)。
// 実務のPACSは、Orthancの「モダリティ一覧」のように「AEタイトル文字列 → 実際の接続先(host:port)」
// の対応表を管理者が事前登録しておき、C-MOVE要求が来るたびにこの表を引いて宛先を決定する。
//
// このプロジェクトではappsettings.jsonの"RemoteAeTitles"セクションにこの対応表を持たせる
// （Orthanc等のような管理画面はまだ無いため、設定ファイル直書きの簡易版）。
// 例:
//   "RemoteAeTitles": { "ORTHANC": "127.0.0.1:4242" }
public interface IRemoteAeRegistry
{
    /// <summary>
    /// AEタイトルからホスト名/ポートを解決する。見つからなければfalseを返す
    /// (C-MOVE応答として "Move Destination Unknown" を返すために呼び出し側が使う)。
    /// </summary>
    bool TryResolve(string aeTitle, out string host, out int port);
}

public sealed class RemoteAeRegistry : IRemoteAeRegistry
{
    private readonly Dictionary<string, (string Host, int Port)> _entries;
    private readonly ILogger<RemoteAeRegistry> _logger;

    public RemoteAeRegistry(IConfiguration configuration, ILogger<RemoteAeRegistry> logger)
    {
        _logger = logger;
        _entries = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);

        var section = configuration.GetSection("RemoteAeTitles");
        foreach (var child in section.GetChildren())
        {
            var aeTitle = child.Key;
            var hostPort = child.Value;
            if (string.IsNullOrWhiteSpace(hostPort))
            {
                continue;
            }

            var parts = hostPort.Split(':', 2);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            {
                _logger.LogWarning(
                    "RemoteAeTitles:{AeTitle} の値 '{Value}' が 'host:port' 形式ではないため読み飛ばします。",
                    aeTitle, hostPort);
                continue;
            }

            _entries[aeTitle] = (parts[0], port);
        }

        _logger.LogInformation(
            "RemoteAeRegistryを初期化しました。登録済みAEタイトル数={Count} ({AeTitles})",
            _entries.Count, string.Join(", ", _entries.Keys));
    }

    public bool TryResolve(string aeTitle, out string host, out int port)
    {
        if (_entries.TryGetValue(aeTitle, out var entry))
        {
            host = entry.Host;
            port = entry.Port;
            return true;
        }

        host = "";
        port = 0;
        return false;
    }
}
