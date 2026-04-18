namespace LGTVSwitcher.Core.LgWebOs;

public sealed record LgTvRegistrationResponse(string RawJson, LgTvRegistrationStatus Status, string? ClientKey);

public enum LgTvRegistrationStatus
{
    Pending,
    Completed,
}
