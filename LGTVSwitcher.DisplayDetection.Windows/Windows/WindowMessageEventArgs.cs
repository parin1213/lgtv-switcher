namespace LGTVSwitcher.DisplayDetection.Windows;

public enum WindowMessageKind
{
    Other = 0,
    DisplayChanged,
    DeviceChanged,
}

public sealed class WindowMessageEventArgs(WindowMessageKind kind, uint messageId, nuint wParam, nint lParam) : EventArgs
{
    public WindowMessageKind Kind { get; } = kind;

    public uint MessageId { get; } = messageId;

    public nuint WParam { get; } = wParam;

    public nint LParam { get; } = lParam;
}
