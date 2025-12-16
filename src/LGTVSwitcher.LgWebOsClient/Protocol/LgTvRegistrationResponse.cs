namespace LGTVSwitcher.LgWebOsClient;

public sealed record LgTvRegistrationResponse(string RawJson, LgTvRegistrationStatus Status, string? ClientKey);

public enum LgTvRegistrationStatus
{
    Unknown,
    Response,
    Registered,
    RequiresPrompt,
    Error,
}
