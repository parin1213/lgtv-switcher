using System.IO;
using System.Net.WebSockets;
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
    private readonly ILgTvResponseParser _responseParser;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _isRegistered;

    public LgTvController(
        ILgTvTransport transport,
        IOptions<LgTvSwitcherOptions> options,
        ILogger<LgTvController> logger,
        ILgTvResponseParser responseParser,
        ILgTvClientKeyStore? clientKeyStore = null)
    {
        _transport = transport;
        _options = options.Value;
        _logger = logger;
        _responseParser = responseParser;
        _clientKeyStore = clientKeyStore;
    }

    public async Task<string?> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_transport.State == WebSocketState.Open && _isRegistered)
        {
            _logger.LogInformation("Already connected and registered with LG TV");
            return null;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_transport.State != WebSocketState.Open)
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

            var registrationResult = await SendRegistrationAsync(cancellationToken).ConfigureAwait(false);

            if (registrationResult.Status != LgTvRegistrationStatus.Registered)
            {
                throw new LgTvRegistrationException("LG TV registration did not succeed. Check the TV prompt and try again.");
            }

            if (!string.IsNullOrWhiteSpace(registrationResult.ClientKey))
            {
                _options.ClientKey = registrationResult.ClientKey;
                _logger.LogInformation("Received client-key from LG TV. Store this value for future runs.");
                if (_clientKeyStore is not null)
                {
                    await _clientKeyStore.PersistClientKeyAsync(registrationResult.ClientKey!, cancellationToken).ConfigureAwait(false);
                }
            }

            _isRegistered = true;
            return registrationResult.RawJson;
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

        _logger.LogInformation("Sending switchInput to {InputId}", inputId);
        await SendRequestAndGetResponsePayloadAsync(
            LgTvUris.SwitchInput,
            new SwitchInputPayload(inputId),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetCurrentInputAsync(CancellationToken cancellationToken)
    {
        var payloadElement = await SendRequestAndGetResponsePayloadAsync(
            LgTvUris.GetForegroundAppInfo,
            payloadObject: null,
            cancellationToken).ConfigureAwait(false);

        var payloadJson = payloadElement.HasValue ? payloadElement.Value.GetRawText() : null;
        return _responseParser.ParseCurrentInput(payloadJson);
    }

    private async Task<JsonElement?> SendRequestAndGetResponsePayloadAsync(
        string uri,
        object? payloadObject,
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var requestEnvelope = new LgTvRequestEnvelope(
            Guid.NewGuid().ToString(),
            "request",
            uri,
            payloadObject);

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(requestEnvelope);

        async Task<string?> SendAndReceiveWithTransportRetryAsync()
        {
            try
            {
                await _transport.SendAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
                return await _transport.ReceiveStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException || ex is IOException)
            {
                _logger.LogWarning(ex, "LG TV transport failed. Re-establishing session and retrying.");
                _isRegistered = false;
                await _transport.DisposeAsync().ConfigureAwait(false);
                await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                await _transport.SendAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
                return await _transport.ReceiveStringAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var responseJson = await SendAndReceiveWithTransportRetryAsync().ConfigureAwait(false);
        var responseEnvelope = _responseParser.ParseResponse(responseJson, uri);

        if (IsNotRegisteredError(responseEnvelope))
        {
            _logger.LogWarning("LG TV reports the client is not registered for {Uri}. Re-establishing the session.", uri);
            _isRegistered = false;
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            responseJson = await SendAndReceiveWithTransportRetryAsync().ConfigureAwait(false);
            responseEnvelope = _responseParser.ParseResponse(responseJson, uri);
        }

        if (IsErrorResponse(responseEnvelope, out var errorMessage))
        {
            throw new LgTvCommandException($"LG TV returned an error for '{uri}' on {_options.TvHost}:{_options.TvPort}: {errorMessage}");
        }

        return responseEnvelope.Payload.ValueKind != JsonValueKind.Undefined
            ? responseEnvelope.Payload.Clone()
            : (JsonElement?)null;
    }

    private static bool IsNotRegisteredError(LgTvResponseEnvelope envelope)
    {
        return string.Equals(envelope.Type, "error", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(envelope.Error) &&
               envelope.Error.Contains("401", StringComparison.OrdinalIgnoreCase) &&
               envelope.Error.Contains("not registered", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsErrorResponse(LgTvResponseEnvelope envelope, out string? errorMessage)
    {
        errorMessage = null;

        if (string.Equals(envelope.Type, "error", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(envelope.Error))
        {
            errorMessage = envelope.Error;
            return true;
        }

        return false;
    }

    private async Task<LgTvRegistrationResponse> SendRegistrationAsync(CancellationToken cancellationToken)
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
        return await SendRegistrationRequestAsync(registerPayload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LgTvRegistrationResponse> SendRegistrationRequestAsync(
        RegistrationPayload registerPayload,
        CancellationToken cancellationToken)
    {
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

            var parsedResponse = _responseParser.ParseRegistrationResponse(response);

            switch (parsedResponse.Status)
            {
                case LgTvRegistrationStatus.Registered:
                    return parsedResponse;
                case LgTvRegistrationStatus.RequiresPrompt:
                    _logger.LogInformation("Waiting for pairing approval on the TV...");
                    continue;
                case LgTvRegistrationStatus.Error:
                    throw new LgTvRegistrationException($"LG TV registration failed: {parsedResponse.RawJson}");
                default:
                    continue;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _connectionLock.Dispose();
        return _transport.DisposeAsync();
    }

    private static class LgTvUris
    {
        public const string SwitchInput = "ssap://tv/switchInput";
        public const string GetForegroundAppInfo = "ssap://com.webos.applicationManager/getForegroundAppInfo";
    }

    private sealed record LgTvRequestEnvelope(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("uri")] string? Uri,
        [property: JsonPropertyName("payload")] object? Payload);

    private sealed record SwitchInputPayload([property: JsonPropertyName("inputId")] string InputId);

    private sealed record RegistrationManifest(
        [property: JsonPropertyName("manifestVersion")] int ManifestVersion,
        [property: JsonPropertyName("permissions")] string[] Permissions);

    private sealed record RegistrationPayload(
        [property: JsonPropertyName("manifest")] RegistrationManifest Manifest,
        [property: JsonPropertyName("client-key")] string? ClientKey);
}