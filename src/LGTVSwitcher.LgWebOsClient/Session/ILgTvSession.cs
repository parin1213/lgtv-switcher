using System.Text.Json;

namespace LGTVSwitcher.LgWebOsClient;

public interface ILgTvSession : IAsyncDisposable
{
    Task EnsureConnectedAsync(CancellationToken cancellationToken);

    Task<JsonElement?> SendRequestAsync(string uri, object? payload, CancellationToken cancellationToken);
}
