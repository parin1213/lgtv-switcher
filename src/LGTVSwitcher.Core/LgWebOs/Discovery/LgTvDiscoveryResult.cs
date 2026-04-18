namespace LGTVSwitcher.Core.LgWebOs;

public sealed record LgTvDiscoveryResult(
    string Address,
    string? Location,
    string? Usn,
    string? Server,
    string? St);
