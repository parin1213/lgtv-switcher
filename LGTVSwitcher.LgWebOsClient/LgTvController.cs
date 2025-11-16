using System.Text;
using System.Text.Json;
using System.Threading;
using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.LgWebOsClient.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LGTVSwitcher.LgWebOsClient;

public sealed class LgTvController : ILgTvController
{
    private readonly ILgTvTransport _transport;
    private readonly LgTvSwitcherOptions _options;
    private readonly ILogger<LgTvController> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _isRegistered;

    public LgTvController(
        ILgTvTransport transport,
        IOptions<LgTvSwitcherOptions> options,
        ILogger<LgTvController> logger)
    {
        _transport = transport;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_transport.State == System.Net.WebSockets.WebSocketState.Open && _isRegistered)
        {
            _logger.LogInformation("Already connected and registered with LG TV");
            return null;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_transport.State != System.Net.WebSockets.WebSocketState.Open)
            {
                var uri = new Uri($"wss://{_options.TvHost}:{_options.TvPort}");
                _logger.LogInformation("Connecting to LG TV at {Uri}", uri);
                await _transport.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
                _isRegistered = false;
            }

            if (!_isRegistered)
            {
                var registrationResponse = await SendRegistrationAsync(cancellationToken).ConfigureAwait(false);
                _isRegistered = true;

                return registrationResponse;
            }
        }
        finally
        {
            _connectionLock.Release();
        }

        return null;
    }

    public async Task SwitchInputAsync(string inputId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputId))
        {
            throw new ArgumentException("InputId cannot be empty.", nameof(inputId));
        }

        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = Guid.NewGuid().ToString(),
            type = "request",
            uri = "ssap://tv/switchInput",
            payload = new { inputId }
        });

        _logger.LogInformation("Sending switchInput to {InputId}", inputId);
        await _transport.SendAsync(payload, cancellationToken).ConfigureAwait(false);
        var body = await _transport.ReceiveStringAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Received response: {Response}", body);
    }

    private async Task<string?> SendRegistrationAsync(CancellationToken cancellationToken)
    {
        var manifest = new
        {
            manifestVersion = 1,
            permissions = new[]
            {
                "LAUNCH", "LAUNCH_WEBAPP", "CONTROL_INPUT_TEXT", "CONTROL_MOUSE_AND_KEYBOARD",
                "READ_INSTALLED_APPS", "CONTROL_DISPLAY", "CONTROL_POWER", "READ_INPUT_DEVICE_LIST",
                "READ_NETWORK_STATE", "READ_TV_CHANNEL_LIST", "WRITE_NOTIFICATION_TOAST",
                "READ_POWER_STATE", "READ_CURRENT_CHANNEL", "READ_RUNNING_APPS", "READ_UPDATE_INFO"
            },
        };

        var registerPayload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = Guid.NewGuid().ToString(),
            type = "register",
            payload = new
            {
                manifest,
                clientKey = _options.ClientKey,
            }
        });

        _logger.LogInformation("Registering with LG TV");
        await _transport.SendAsync(registerPayload, cancellationToken).ConfigureAwait(false);

        // Consume the response so the socket stays clean. We don't parse it here but the TV will respond with the status.
        var body = await _transport.ReceiveStringAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Received registration response: {Response}", body);

        return body;
    }

    public ValueTask DisposeAsync()
    {
        _connectionLock.Dispose();
        return _transport.DisposeAsync();
    }
}
