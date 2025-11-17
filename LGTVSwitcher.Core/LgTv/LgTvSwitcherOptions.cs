namespace LGTVSwitcher.Core.LgTv;

public sealed class LgTvSwitcherOptions
{
    /// <summary>
    /// Hostname or IP address of the LG TV.
    /// </summary>
    public string TvHost { get; set; } = "lgwebostv.local";

    /// <summary>
    /// WebSocket port (defaults to 3000 for webOS TVs).
    /// </summary>
    public int TvPort { get; set; } = 3000;

    /// <summary>
    /// Registered client key. When empty the TV will prompt for pairing.
    /// </summary>
    public string? ClientKey { get; set; }

    /// <summary>
    /// Input ID to switch to when the preferred monitor is online.
    /// </summary>
    public string TargetInputId { get; set; } = "HDMI_1";

    /// <summary>
    /// Optional fallback input when the preferred monitor goes offline.
    /// </summary>
    public string? FallbackInputId { get; set; }

    /// <summary>
    /// Friendly name (or device name) of the monitor that should trigger LG TV switching.
    /// </summary>
    public string PreferredMonitorName { get; set; } = string.Empty;
}