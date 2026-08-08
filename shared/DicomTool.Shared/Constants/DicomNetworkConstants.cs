namespace DicomTool.Shared.Constants;

// ======================================================
// DicomNetworkConstants — DICOM通信(DIMSE)の識別子
// ======================================================
// docs/CONTRACT.md 5章参照。DICOMのネットワーク通信は「AEタイトル(Application Entity Title)」
// という最大16文字の名前で通信相手を識別する（IPアドレス/ポートだけでなく、この名前も一致
// させないとアソシエーション＝接続を張れない、という仕組みがTCP/IPだけの通信と大きく違う点）。
public static class DicomNetworkConstants
{
    /// <summary>
    /// 自システム(SCP＝受信側)のAEタイトル。他のモダリティやSCUがC-STOREを送ってくる際、
    /// 宛先AEタイトルとしてこの値を指定する必要がある。
    /// </summary>
    public const string OwnAeTitle = "DICOMTOOL3";

    /// <summary>
    /// 自己疎通テスト（ループバックでのC-ECHO/C-STORE送信）用に、このリポジトリ自身が
    /// SCUとして振る舞うときに名乗るAEタイトル。実運用の外部モダリティとは無関係の、
    /// あくまで開発時の動作確認用の名前。
    /// </summary>
    public const string TestScuAeTitle = "DICOMTOOL3SCU";
}
