using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.LgWebOsClient.Transport;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LGTVSwitcher.LgWebOsClient;

public sealed class LgTvSession : ILgTvSession
{
    private static readonly TimeSpan RegistrationTimeout = TimeSpan.FromMinutes(2);

    private readonly ILgTvTransport _transport;
    private readonly LgTvSwitcherOptions _options;
    private readonly ILogger<LgTvSession> _logger;
    private readonly ILgTvDiscoveryService _discoveryService;
    private readonly ILgTvClientKeyStore? _clientKeyStore;
    private readonly ILgTvResponseParser _responseParser;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private string? _activeHost;
    private string? _activeUsn;
    private bool _isRegistered;
    private bool _disposed;

    public LgTvSession(
        ILgTvTransport transport,
        IOptions<LgTvSwitcherOptions> options,
        ILogger<LgTvSession> logger,
        ILgTvResponseParser responseParser,
        ILgTvDiscoveryService discoveryService,
        ILgTvClientKeyStore? clientKeyStore = null)
    {
        _transport = transport;
        _options = options.Value;
        _logger = logger;
        _responseParser = responseParser;
        _discoveryService = discoveryService;
        _clientKeyStore = clientKeyStore;
    }

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_transport.State == WebSocketState.Open && _isRegistered)
        {
            _logger.LogInformation("Already connected and registered with LG TV");
            return;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_transport.State == WebSocketState.Open && _isRegistered)
            {
                return;
            }

            var candidates = await ResolveCandidatesAsync(cancellationToken).ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                throw new LgTvRegistrationException("No LG TV discovered via SSDP or configuration.");
            }

            if (string.IsNullOrWhiteSpace(_options.ClientKey))
            {
                _logger.LogInformation("No client-key configured. Accept the pairing prompt on the TV to continue.");
            }

            Exception? lastError = null;

            foreach (var candidate in candidates)
            {
                try
                {
                    await ConnectTransportAsync(candidate, cancellationToken).ConfigureAwait(false);

                    var registrationResult = await SendRegistrationAsync(cancellationToken).ConfigureAwait(false);

                    if (registrationResult.Status != LgTvRegistrationStatus.Registered)
                    {
                        throw new LgTvRegistrationException("LG TV registration did not succeed. Check the TV prompt and try again.");
                    }

                    await PersistRegistrationStateAsync(registrationResult, candidate, cancellationToken).ConfigureAwait(false);

                    _isRegistered = true;
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is LgTvRegistrationException || ex is WebSocketException || ex is IOException)
                {
                    lastError = ex;
                    _logger.LogWarning(ex, "Failed to connect/register with LG TV at {Host}. Trying next candidate.", candidate.Host);
                    _isRegistered = false;
                    _activeHost = null;
                    _activeUsn = null;
                    await _transport.DisposeAsync().ConfigureAwait(false);
                }
            }

            if (lastError is not null)
            {
                throw new LgTvRegistrationException("Failed to register with any discovered LG TV.", lastError);
            }

            throw new LgTvRegistrationException("Failed to register with any discovered LG TV.");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<JsonElement?> SendRequestAsync(string uri, object? payload, CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        var requestEnvelope = new LgTvRequestEnvelope(
            Guid.NewGuid().ToString(),
            "request",
            uri,
            payload);

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
            var hostForLog = _activeHost ?? _options.TvHost;
            throw new LgTvCommandException($"LG TV returned an error for '{uri}' on {hostForLog}:{_options.TvPort}: {errorMessage}");
        }

        return responseEnvelope.Payload.ValueKind != JsonValueKind.Undefined
            ? responseEnvelope.Payload.Clone()
            : (JsonElement?)null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connectionLock.Dispose();
        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ConnectTransportAsync(LgTvEndpoint endpoint, CancellationToken cancellationToken)
    {
        var uri = new Uri($"wss://{endpoint.Host}:{_options.TvPort}");
        _logger.LogInformation("Connecting to LG TV at {Uri}", uri);
        if (_transport.State == WebSocketState.Open)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
        }

        await _transport.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        _activeHost = endpoint.Host;
        _activeUsn = endpoint.Usn;
        _isRegistered = false;
    }

    private async Task<IReadOnlyList<LgTvEndpoint>> ResolveCandidatesAsync(CancellationToken cancellationToken)
    {
        var discovered = await _discoveryService.DiscoverAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(_options.PreferredTvUsn))
        {
            var match = discovered.FirstOrDefault(r =>
                !string.IsNullOrWhiteSpace(r.Usn) &&
                string.Equals(r.Usn, _options.PreferredTvUsn, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return new[] { ToEndpoint(match) };
            }

            _logger.LogWarning("PreferredTvUsn {Usn} was not found via SSDP discovery. Falling back to discovered candidates.", _options.PreferredTvUsn);
        }

        if (discovered.Count > 0)
        {
            return discovered.Select(ToEndpoint).ToList();
        }

        if (!string.IsNullOrWhiteSpace(_options.TvHost))
        {
            _logger.LogWarning("No LG TV discovered via SSDP. Falling back to configured TvHost {Host}.", _options.TvHost);
            return new[] { new LgTvEndpoint(_options.TvHost, null, null) };
        }

        return Array.Empty<LgTvEndpoint>();
    }

    private async Task PersistRegistrationStateAsync(
        LgTvRegistrationResponse registrationResult,
        LgTvEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(registrationResult.ClientKey))
        {
            _options.ClientKey = registrationResult.ClientKey;
            _logger.LogInformation("Received client-key from LG TV. Store this value for future runs.");
            if (_clientKeyStore is not null)
            {
                await _clientKeyStore.PersistClientKeyAsync(registrationResult.ClientKey!, cancellationToken).ConfigureAwait(false);
            }
        }

        if (!string.IsNullOrWhiteSpace(endpoint.Usn))
        {
            _options.PreferredTvUsn = endpoint.Usn;
            if (_clientKeyStore is not null)
            {
                await _clientKeyStore.PersistPreferredTvUsnAsync(endpoint.Usn!, cancellationToken).ConfigureAwait(false);
            }
        }
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

    private static LgTvEndpoint ToEndpoint(LgTvDiscoveryResult result)
    {
        var host = TryGetHostFromLocation(result.Location) ?? result.Address;
        return new LgTvEndpoint(host, result.Usn, result.Location);
    }

    private static string? TryGetHostFromLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        return Uri.TryCreate(location, UriKind.Absolute, out var uri)
            ? uri.Host
            : null;
    }

    private sealed record LgTvEndpoint(string Host, string? Usn, string? Location);

    private sealed record LgTvRequestEnvelope(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("uri")] string? Uri,
        [property: JsonPropertyName("payload")] object? Payload);

    private sealed record RegistrationManifest(
        [property: JsonPropertyName("manifestVersion")] int ManifestVersion,
        [property: JsonPropertyName("permissions")] string[] Permissions);

    private sealed record RegistrationPayload(
        [property: JsonPropertyName("manifest")] RegistrationManifest Manifest,
        [property: JsonPropertyName("client-key")] string? ClientKey);
}
