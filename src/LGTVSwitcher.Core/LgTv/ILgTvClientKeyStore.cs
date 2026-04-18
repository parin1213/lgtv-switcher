namespace LGTVSwitcher.Core.LgTv;

public interface ILgTvClientKeyStore
{
    Task<LgTvPersistedState> GetStateAsync(CancellationToken cancellationToken);
    Task PersistClientKeyAsync(string clientKey, CancellationToken cancellationToken);
    Task PersistPreferredTvUsnAsync(string preferredTvUsn, CancellationToken cancellationToken);
}

public sealed record LgTvPersistedState(string? ClientKey = null, string? PreferredTvUsn = null);
