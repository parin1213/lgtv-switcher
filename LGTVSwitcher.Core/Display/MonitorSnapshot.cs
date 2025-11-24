namespace LGTVSwitcher.Core.Display;

/// <summary>
/// 単一の論理モニタが特定の瞬間にどのような状態だったかを表すスナップショット。
/// </summary>
/// <param name="DeviceName">Win32 のデバイス名（例: <c>\\.\DISPLAY1</c>）。</param>
/// <param name="FriendlyName">ログに出力するための人間が判読しやすい名前。</param>
/// <param name="Bounds">仮想デスクトップ基準での位置とサイズ。</param>
/// <param name="IsPrimary">OS がプライマリモニタと認識している場合は true。</param>
/// <param name="ConnectionKind">モニタの接続方式を大まかに分類した値。</param>
/// <param name="EdidKey">EDID / Instance ID など、再接続に強い識別子。</param>
public sealed record MonitorSnapshot(
    string DeviceName,
    string FriendlyName,
    MonitorBounds Bounds,
    bool IsPrimary,
    MonitorConnectionKind ConnectionKind,
    string? EdidKey);

/// <summary>
/// モニタが仮想デスクトップ上で占有する矩形領域。
/// </summary>
public readonly record struct MonitorBounds(int X, int Y, int Width, int Height)
{
    public override string ToString() => $"({X},{Y}) {Width}x{Height}";
}