namespace LGTVSwitcher.Core.LgTv;

public interface ILgTvClientKeyStore
{
    Task<LgTvPersistedState> GetStateAsync(CancellationToken cancellationToken);
    Task PersistClientKeyAsync(string clientKey, CancellationToken cancellationToken);
    Task PersistPreferredTvUsnAsync(string preferredTvUsn, CancellationToken cancellationToken);
    Task PersistLastKnownTvAddressAsync(string address, CancellationToken cancellationToken);
}

/// <param name="LastKnownTvAddress">
/// 最後に接続できた TV の IP。SSDP も TvHost の名前解決も効かないとき（Mac が Wi-Fi だけになった等）の接続先に使う。
/// </param>
public sealed record LgTvPersistedState(string? ClientKey = null, string? PreferredTvUsn = null, string? LastKnownTvAddress = null);
