using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.LgWebOsClient.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LGTVSwitcher.LgWebOsClient;

public sealed class LgTvController : ILgTvController
{
    private static readonly TimeSpan RegistrationTimeout = TimeSpan.FromMinutes(2);

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

            if (_isRegistered)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(_options.ClientKey))
            {
                _logger.LogInformation("No client-key configured. Accept the pairing prompt on the TV to continue.");
            }

            var registrationResponse = await SendRegistrationAsync(cancellationToken).ConfigureAwait(false);

            if (!TryParseRegistrationResponse(registrationResponse, out var clientKey, out var status))
            {
                throw new LgTvRegistrationException("LG TV registration did not complete successfully.");
            }

            if (status != RegistrationStatus.Registered)
            {
                throw new LgTvRegistrationException("LG TV registration did not succeed. Check the TV prompt and try again.");
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
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task SwitchInputAsync(string inputId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputId))
        {
            throw new ArgumentException("InputId cannot be empty.", nameof(inputId));
        }

        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var envelope = new LgTvRequestEnvelope(
            Guid.NewGuid().ToString(),
            "request",
            LgTvUris.SwitchInput,
            new SwitchInputPayload(inputId));

        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);

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

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new LgTvCommandException($"LG TV returned an empty response for '{LgTvUris.SwitchInput}' ({inputId}) on {_options.TvHost}:{_options.TvPort}.");
        }

        _logger.LogDebug("Received response: {Response}", body);

        if (IsNotRegisteredError(body))
        {
            if (!allowRetry)
            {
                throw new LgTvCommandException(
                    $"LG TV rejected '{LgTvUris.SwitchInput}' for input '{inputId}' on {_options.TvHost}:{_options.TvPort}: client not registered.");
            }

            _logger.LogWarning("LG TV reports the client is not registered. Re-establishing the session.");
            _isRegistered = false;
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            await SendSwitchRequestAsync(payload, inputId, allowRetry: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (IsErrorResponse(body, out var errorMessage))
        {
            throw new LgTvCommandException(
                $"LG TV returned an error for '{LgTvUris.SwitchInput}' (input '{inputId}' on {_options.TvHost}:{_options.TvPort}): {errorMessage}");
        }
    }

    private async Task<string?> SendRegistrationAsync(CancellationToken cancellationToken)
    {
        var manifest = new RegistrationManifest(
            ManifestVersion: 1,
            Permissions:
            [
                "LAUNCH", "LAUNCH_WEBAPP", "CONTROL_INPUT_TEXT", "CONTROL_MOUSE_AND_KEYBOARD",
                "READ_INSTALLED_APPS", "CONTROL_DISPLAY", "CONTROL_POWER", "READ_INPUT_DEVICE_LIST",
                "READ_NETWORK_STATE", "READ_TV_CHANNEL_LIST", "WRITE_NOTIFICATION_TOAST",
                "READ_POWER_STATE", "READ_CURRENT_CHANNEL", "READ_RUNNING_APPS", "READ_UPDATE_INFO"
            ]);

        var registerPayload = new RegistrationPayload(manifest, _options.ClientKey);

        var envelope = new LgTvRequestEnvelope(
            Guid.NewGuid().ToString(),
            "register",
            null,
            registerPayload);

        await _transport.SendAsync(JsonSerializer.SerializeToUtf8Bytes(envelope), cancellationToken).ConfigureAwait(false);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RegistrationTimeout);

        while (true)
        {
            string? response;
            try
            {
                response = await _transport.ReceiveStringAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new LgTvRegistrationException("Timed out waiting for LG TV pairing confirmation.", ex);
            }

            if (string.IsNullOrWhiteSpace(response))
            {
                throw new LgTvRegistrationException("Empty registration response received from LG TV.");
            }

            _logger.LogInformation("Received registration response: {Response}", response);

            if (!TryParseRegistrationResponse(response, out _, out var status))
            {
                throw new LgTvRegistrationException("Failed to parse registration response from LG TV.");
            }

            switch (status)
            {
                case RegistrationStatus.Registered:
                    return response;
                case RegistrationStatus.RequiresPrompt:
                    _logger.LogInformation("Waiting for pairing approval on the TV...");
                    continue;
                case RegistrationStatus.Error:
                    throw new LgTvRegistrationException($"LG TV registration failed: {response}");
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
            var envelope = JsonSerializer.Deserialize<LgTvResponseEnvelope>(json);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
            {
                return false;
            }

            clientKey = envelope.Payload?.ClientKey;

            switch (envelope.Type.ToLowerInvariant())
            {
                case "registered":
                    status = RegistrationStatus.Registered;
                    break;
                case "response":
                    if (string.Equals(envelope.Payload?.PairingType, "PROMPT", StringComparison.OrdinalIgnoreCase))
                    {
                        status = RegistrationStatus.RequiresPrompt;
                    }
                    else if (envelope.Payload?.ReturnValue == true && !string.IsNullOrWhiteSpace(envelope.Payload?.ClientKey))
                    {
                        status = RegistrationStatus.Registered;
                    }
                    else
                    {
                        status = RegistrationStatus.Response;
                    }
                    break;
                case "error":
                    if (!string.IsNullOrWhiteSpace(envelope.Error) &&
                        envelope.Error.Contains("register already in progress", StringComparison.OrdinalIgnoreCase))
                    {
                        status = RegistrationStatus.RequiresPrompt;
                    }
                    else
                    {
                        status = RegistrationStatus.Error;
                    }
                    break;
                default:
                    status = RegistrationStatus.Unknown;
                    break;
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
            var envelope = JsonSerializer.Deserialize<LgTvResponseEnvelope>(json);
            if (envelope is null)
            {
                return false;
            }

            return string.Equals(envelope.Type, "error", StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(envelope.Error) &&
                   envelope.Error.Contains("401", StringComparison.OrdinalIgnoreCase) &&
                   envelope.Error.Contains("not registered", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsErrorResponse(string? json, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<LgTvResponseEnvelope>(json);
            if (envelope is null)
            {
                return false;
            }

            if (string.Equals(envelope.Type, "error", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(envelope.Error))
            {
                errorMessage = envelope.Error;
                return true;
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

    private static class LgTvUris
    {
        public const string SwitchInput = "ssap://tv/switchInput";
    }

    private sealed record LgTvRequestEnvelope(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("uri")] string? Uri,
        [property: JsonPropertyName("payload")] object Payload);

    private sealed record SwitchInputPayload([property: JsonPropertyName("inputId")] string InputId);

    private sealed record RegistrationManifest(
        [property: JsonPropertyName("manifestVersion")] int ManifestVersion,
        [property: JsonPropertyName("permissions")] string[] Permissions);

    private sealed record RegistrationPayload(
        [property: JsonPropertyName("manifest")] RegistrationManifest Manifest,
        [property: JsonPropertyName("client-key")] string? ClientKey);

    private sealed record LgTvResponsePayload(
        [property: JsonPropertyName("client-key")] string? ClientKey,
        [property: JsonPropertyName("pairingType")] string? PairingType,
        [property: JsonPropertyName("returnValue")] bool? ReturnValue);

    private sealed record LgTvResponseEnvelope(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("payload")] LgTvResponsePayload? Payload,
        [property: JsonPropertyName("error")] string? Error);
}
