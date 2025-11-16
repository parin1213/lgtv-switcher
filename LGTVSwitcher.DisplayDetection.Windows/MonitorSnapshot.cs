namespace LGTVSwitcher.DisplayDetection;

/// <summary>
/// Snapshot of a single logical monitor at a specific moment（単一論理モニタの状態を任意時点で切り取った情報）。
/// </summary>
/// <param name="DeviceName">Win32 device name (例: <c>\\.\DISPLAY1</c> ).</param>
/// <param name="FriendlyName">Human readable label used in logs（ログ表記用の名称）。</param>
/// <param name="Bounds">Pixel bounds relative to the virtual desktop（仮想デスクトップ基準の座標/サイズ）。</param>
/// <param name="IsPrimary">True if the OS marks the monitor as primary（プライマリモニタかどうか）。</param>
/// <param name="ConnectionKind">Coarse classification of how the monitor is connected（接続方式の大まかな分類）。</param>
public sealed record MonitorSnapshot(
    string DeviceName,
    string FriendlyName,
    MonitorBounds Bounds,
    bool IsPrimary,
    MonitorConnectionKind ConnectionKind);

/// <summary>
/// Rectangle describing the area occupied by the monitor（モニタが占有する矩形領域）。
/// </summary>
public readonly record struct MonitorBounds(int X, int Y, int Width, int Height)
{
    public override string ToString() => $"({X},{Y}) {Width}x{Height}";
}
