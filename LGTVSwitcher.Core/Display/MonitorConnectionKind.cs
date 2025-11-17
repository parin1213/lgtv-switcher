namespace LGTVSwitcher.Core.Display;

/// <summary>
/// モニタの接続方式を大まかに分類する値。
/// Win32 API から取得できる情報の粒度に合わせた定義。
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