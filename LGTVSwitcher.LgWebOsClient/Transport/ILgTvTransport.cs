using System.Net.WebSockets;

namespace LGTVSwitcher.LgWebOsClient.Transport;

public interface ILgTvTransport : IAsyncDisposable
{
    WebSocketState State { get; }

    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    Task<string?> ReceiveStringAsync(CancellationToken cancellationToken);
}
