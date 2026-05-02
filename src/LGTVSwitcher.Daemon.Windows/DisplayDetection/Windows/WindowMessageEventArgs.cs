namespace LGTVSwitcher.Daemon.Windows.DisplayDetection;

public enum WindowMessageKind
{
    Other = 0,
    DisplayChanged,
    DeviceChanged,
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class WindowMessageEventArgs(WindowMessageKind kind, uint messageId, nuint wParam, nint lParam) : EventArgs
{
    public WindowMessageKind Kind { get; } = kind;

    public uint MessageId { get; } = messageId;

    public nuint WParam { get; } = wParam;

    public nint LParam { get; } = lParam;
}
