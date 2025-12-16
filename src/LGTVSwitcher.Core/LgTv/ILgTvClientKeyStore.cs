namespace LGTVSwitcher.Core.LgTv;

public interface ILgTvClientKeyStore
{
    Task PersistClientKeyAsync(string clientKey, CancellationToken cancellationToken);

    Task PersistPreferredTvUsnAsync(string preferredTvUsn, CancellationToken cancellationToken);
}
