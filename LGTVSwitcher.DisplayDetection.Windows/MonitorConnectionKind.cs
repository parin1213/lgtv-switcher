namespace LGTVSwitcher.DisplayDetection;

/// <summary>
/// Light-weight description of how a monitor is connected.
/// Values map to the coarse information that Win32 APIs can provide today.
/// </summary>
public enum MonitorConnectionKind
{
    Unknown = 0,
    Internal,
    DisplayPort,
    Hdmi,
    Usb,
    Wireless,
    Virtual,
}
