using System.IO;
using System.Net.Security;
using System.Net.WebSockets;
using System.Text;

using Microsoft.Extensions.Logging;

namespace LGTVSwitcher.LgWebOsClient.Transport;

public sealed class DefaultWebSocketTransport : ILgTvTransport
{
    private readonly ILogger<DefaultWebSocketTransport> _logger;
    private ClientWebSocket? _client;

    public WebSocketState State => _client?.State ?? WebSocketState.None;

    public DefaultWebSocketTransport(ILogger<DefaultWebSocketTransport> logger)
    {
        _logger = logger;
    }

    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (_client is { State: WebSocketState.Open })
        {
            return;
        }

        await DisposeAsync().ConfigureAwait(false);

        _client = new ClientWebSocket();

        // ここでは LG TV 向けの接続に限り証明書検証を緩和する
        _client.Options.RemoteCertificateValidationCallback =
            (sender, certificate, chain, errors) =>
            {
                if (errors == SslPolicyErrors.None)
                {
                    return true;
                }

                if (uri.Scheme == "wss")
                {
                    _logger.LogWarning("Ignoring TLS certificate error(s) {Errors} for {Uri}", errors, uri);
                    return true;
                }

                return false;
            };

        await _client.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Transport is not connected.");
        }

        await _client.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> ReceiveStringAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            return null;
        }

        var buffer = new ArraySegment<byte>(new byte[4 * 1024]);
        using var memory = new MemoryStream();

        while (true)
        {
            var result = await _client.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            memory.Write(buffer.Array!, buffer.Offset, result.Count);

            if (result.EndOfMessage)
            {
                break;
            }
        }

        var resultString = Encoding.UTF8.GetString(memory.ToArray());
        _logger.LogDebug("Received message: {Message}", resultString);
        return resultString;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            if (_client.State == WebSocketState.Open)
            {
                await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, "disposing", CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            // 終了処理を優先するためここでの例外は無視する
        }
        finally
        {
            _client.Dispose();
            _client = null;
        }
    }
}