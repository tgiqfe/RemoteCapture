namespace RemoteCapture.DesktopDuplication;

/// <summary>
/// モニター情報を表すクラス
/// </summary>
public class MonitorInfo
{
    /// <summary>
    /// デバイス名
    /// </summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// アダプターのインデックス
    /// </summary>
    public int AdapterIndex { get; set; }

    /// <summary>
    /// 出力（モニター）のインデックス
    /// </summary>
    public int OutputIndex { get; set; }

    /// <summary>
    /// プライマリモニターかどうか
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// モニターの幅
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// モニターの高さ
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 左上のX座標
    /// </summary>
    public int Left { get; set; }

    /// <summary>
    /// 左上のY座標
    /// </summary>
    public int Top { get; set; }
}
