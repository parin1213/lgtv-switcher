namespace LGTVSwitcher.Experimental.DisplayDetection;

/// <summary>
/// Snapshot of a single logical monitor at a specific moment（単一の論理モニタの状態を任意時点で切り取った情報）。
/// </summary>
/// <param name="DeviceName">Win32 device name (例: <c>\\.\DISPLAY1</c> ).</param>
/// <param name="FriendlyName">Human readable label used in logs（ログに出す人間可読名）。</param>
/// <param name="Bounds">Pixel bounds relative to the virtual desktop（仮想デスクトップ基準の座標とサイズ）。</param>
/// <param name="IsPrimary">True if the OS marks the monitor as primary（プライマリディスプレイかどうか）。</param>
/// <param name="ConnectionKind">Coarse classification of how the monitor is connected（接続種別の大まかな分類）。</param>
public sealed record MonitorSnapshot(
    string DeviceName,
    string FriendlyName,
    MonitorBounds Bounds,
    bool IsPrimary,
    MonitorConnectionKind ConnectionKind);

/// <summary>
/// Simple rectangle struct describing the area occupied by the monitor（モニタが占有する矩形領域の情報）。
/// </summary>
/// <param name="X">Left coordinate in pixels（左上X座標）。</param>
/// <param name="Y">Top coordinate in pixels（左上Y座標）。</param>
/// <param name="Width">Width in pixels（幅）。</param>
/// <param name="Height">Height in pixels（高さ）。</param>
public readonly record struct MonitorBounds(int X, int Y, int Width, int Height)
{
    public override string ToString() => $"({X},{Y}) {Width}x{Height}";
}
