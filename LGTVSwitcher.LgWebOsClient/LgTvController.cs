using System.Collections.Generic;
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
    private readonly ILgTvClientKeyStore? _clientKeyStore;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _isRegistered;

    public LgTvController(
        ILgTvTransport transport,
        IOptions<LgTvSwitcherOptions> options,
        ILogger<LgTvController> logger,
        ILgTvClientKeyStore? clientKeyStore = null)
    {
        _transport = transport;
        _options = options.Value;
        _logger = logger;
        _clientKeyStore = clientKeyStore;
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
                if (string.IsNullOrWhiteSpace(_options.ClientKey))
                {
                    _logger.LogInformation("No client-key configured. Accept the pairing prompt on the TV to continue.");
                }

                var registrationResponse = await SendRegistrationAsync(cancellationToken).ConfigureAwait(false);

                if (!TryParseRegistrationResponse(registrationResponse, out var clientKey, out var status))
                {
                    throw new InvalidOperationException("LG TV registration did not complete successfully.");
                }

                if (status != RegistrationStatus.Registered)
                {
                    throw new InvalidOperationException("LG TV registration did not succeed. Check the TV prompt and try again.");
                }

                if (!string.IsNullOrWhiteSpace(clientKey))
                {
                    _options.ClientKey = clientKey;
                    _logger.LogInformation("Received client-key from LG TV. Store this value for future runs.");
                    if (_clientKeyStore is not null)
                    {
                        await _clientKeyStore.PersistClientKeyAsync(clientKey, cancellationToken).ConfigureAwait(false);
                    }
                }

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

        await SendSwitchRequestAsync(payload, inputId, allowRetry: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendSwitchRequestAsync(
        ReadOnlyMemory<byte> payload,
        string inputId,
        bool allowRetry,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sending switchInput to {InputId}", inputId);
        await _transport.SendAsync(payload, cancellationToken).ConfigureAwait(false);
        var body = await _transport.ReceiveStringAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Received response: {Response}", body);

        if (IsNotRegisteredError(body))
        {
            if (!allowRetry)
            {
                throw new InvalidOperationException("LG TV rejected the request because the client is not registered.");
            }

            _logger.LogWarning("LG TV reports the client is not registered. Re-establishing the session.");
            _isRegistered = false;
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            await SendSwitchRequestAsync(payload, inputId, allowRetry: false, cancellationToken).ConfigureAwait(false);
        }
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

        var payload = new Dictionary<string, object?>
        {
            ["manifest"] = manifest,
        };

        if (!string.IsNullOrWhiteSpace(_options.ClientKey))
        {
            payload["client-key"] = _options.ClientKey;
        }

        var envelope = new
        {
            id = Guid.NewGuid().ToString(),
            type = "register",
            payload
        };

        _logger.LogInformation("Registering with LG TV");
        await _transport.SendAsync(JsonSerializer.SerializeToUtf8Bytes(envelope), cancellationToken).ConfigureAwait(false);

        while (true)
        {
            var response = await _transport.ReceiveStringAsync(cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response))
            {
                throw new InvalidOperationException("Empty registration response received from LG TV.");
            }

            _logger.LogInformation("Received registration response: {Response}", response);

            if (!TryParseRegistrationResponse(response, out _, out var status))
            {
                throw new InvalidOperationException("Failed to parse registration response from LG TV.");
            }

            switch (status)
            {
                case RegistrationStatus.Registered:
                    return response;
                case RegistrationStatus.RequiresPrompt:
                    _logger.LogInformation("Waiting for the TV user to approve the pairing request...");
                    continue;
                case RegistrationStatus.Error:
                    throw new InvalidOperationException($"LG TV registration failed: {response}");
                default:
                    continue;
            }
        }
    }

    private static bool TryParseRegistrationResponse(string? json, out string? clientKey, out RegistrationStatus status)
    {
        clientKey = null;
        status = RegistrationStatus.Unknown;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeProperty))
            {
                var typeValue = typeProperty.GetString();
                if (string.Equals(typeValue, "registered", StringComparison.OrdinalIgnoreCase))
                {
                    status = RegistrationStatus.Registered;
                }
                else if (string.Equals(typeValue, "response", StringComparison.OrdinalIgnoreCase))
                {
                    status = RegistrationStatus.Response;
                }
                else if (string.Equals(typeValue, "prompt", StringComparison.OrdinalIgnoreCase))
                {
                    status = RegistrationStatus.RequiresPrompt;
                }
                else if (string.Equals(typeValue, "error", StringComparison.OrdinalIgnoreCase))
                {
                    status = RegistrationStatus.Error;
                }
            }

            if (root.TryGetProperty("payload", out var payload))
            {
                if (payload.TryGetProperty("client-key", out var clientKeyProperty))
                {
                    clientKey = clientKeyProperty.GetString();
                }

                if (payload.TryGetProperty("pairingType", out var pairingTypeProperty) &&
                    string.Equals(pairingTypeProperty.GetString(), "PROMPT", StringComparison.OrdinalIgnoreCase))
                {
                    status = RegistrationStatus.RequiresPrompt;
                }

                if (payload.TryGetProperty("returnValue", out var returnValueProperty) &&
                    returnValueProperty.ValueKind == JsonValueKind.True &&
                    payload.TryGetProperty("client-key", out _))
                {
                    status = RegistrationStatus.Registered;
                }
            }

            if (status == RegistrationStatus.Error &&
                root.TryGetProperty("error", out var errorProperty) &&
                errorProperty.GetString()?.Contains("register already in progress", StringComparison.OrdinalIgnoreCase) == true)
            {
                status = RegistrationStatus.RequiresPrompt;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsNotRegisteredError(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

        if (root.TryGetProperty("type", out var typeProperty) &&
            string.Equals(typeProperty.GetString(), "error", StringComparison.OrdinalIgnoreCase) &&
            root.TryGetProperty("error", out var errorProperty))
        {
                var error = errorProperty.GetString();
                if (!string.IsNullOrWhiteSpace(error) &&
                    error.Contains("401", StringComparison.OrdinalIgnoreCase) &&
                    error.Contains("not registered", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    public ValueTask DisposeAsync()
    {
        _connectionLock.Dispose();
        return _transport.DisposeAsync();
    }

    private enum RegistrationStatus
    {
        Unknown,
        Response,
        Registered,
        RequiresPrompt,
        Error,
    }
}
