namespace LGTVSwitcher.Experimental.DisplayDetection.Windows;

internal enum WindowMessageKind
{
    Other = 0,
    DisplayChanged,
    DeviceChanged,
}

internal sealed class WindowMessageEventArgs(WindowMessageKind kind, uint messageId, nuint wParam, nint lParam) : EventArgs
{
    public WindowMessageKind Kind { get; } = kind;

    public uint MessageId { get; } = messageId;

    public nuint WParam { get; } = wParam;

    public nint LParam { get; } = lParam;
}
