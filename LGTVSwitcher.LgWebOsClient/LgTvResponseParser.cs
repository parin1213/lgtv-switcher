using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

namespace LGTVSwitcher.LgWebOsClient;

public sealed class LgTvResponseParser : ILgTvResponseParser
{
    private readonly ILogger<LgTvResponseParser> _logger;

    public LgTvResponseParser(ILogger<LgTvResponseParser> logger)
    {
        _logger = logger;
    }

    public string? ParseCurrentInput(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            _logger.LogWarning("LG TV returned foreground app info response without a payload.");
            return null;
        }

        ForegroundAppInfoPayload? appInfo;
        try
        {
            appInfo = JsonSerializer.Deserialize<ForegroundAppInfoPayload>(payloadJson);
            if (appInfo is null)
            {
                throw new JsonException("Deserialized foreground app info payload is null.");
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize getForegroundAppInfo payload: {Payload}", payloadJson);
            return null;
        }

        if (!appInfo.ReturnValue)
        {
            throw new LgTvCommandException("LG TV rejected getForegroundAppInfo request.");
        }

        var appId = appInfo.GetAppId();

        if (string.IsNullOrWhiteSpace(appId))
        {
            _logger.LogWarning("LG TV returned foreground app info response without an appId: {Payload}", payloadJson);
            return null;
        }

        var mapped = MapAppIdToInputId(appId);
        return mapped ?? appId;
    }

    public LgTvRegistrationResponse ParseRegistrationResponse(string json)
    {
        var envelope = ParseResponse(json, "register");

        string? clientKey = null;
        string? pairingType = null;
        bool? returnValue = null;

        if (envelope.Payload.ValueKind == JsonValueKind.Object)
        {
            if (envelope.Payload.TryGetProperty("client-key", out var clientKeyElement) &&
                clientKeyElement.ValueKind == JsonValueKind.String)
            {
                clientKey = clientKeyElement.GetString();
            }

            if (envelope.Payload.TryGetProperty("pairingType", out var pairingTypeElement) &&
                pairingTypeElement.ValueKind == JsonValueKind.String)
            {
                pairingType = pairingTypeElement.GetString();
            }

            if (envelope.Payload.TryGetProperty("returnValue", out var returnValueElement) &&
                returnValueElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                returnValue = returnValueElement.GetBoolean();
            }
        }

        var status = envelope.Type.ToLowerInvariant() switch
        {
            "registered" => LgTvRegistrationStatus.Registered,
            "response" when string.Equals(pairingType, "PROMPT", StringComparison.OrdinalIgnoreCase)
                => LgTvRegistrationStatus.RequiresPrompt,
            "response" when returnValue == true && !string.IsNullOrWhiteSpace(clientKey)
                => LgTvRegistrationStatus.Registered,
            "response" => LgTvRegistrationStatus.Response,
            "error" when !string.IsNullOrWhiteSpace(envelope.Error) &&
                         envelope.Error.Contains("register already in progress", StringComparison.OrdinalIgnoreCase)
                => LgTvRegistrationStatus.RequiresPrompt,
            "error" => LgTvRegistrationStatus.Error,
            _ => LgTvRegistrationStatus.Unknown,
        };

        return new LgTvRegistrationResponse(json, status, clientKey);
    }

    public LgTvResponseEnvelope ParseResponse(string? json, string requestUri)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new LgTvCommandException($"LG TV returned an empty response for '{requestUri}'.");
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<LgTvResponseEnvelope>(json);
            if (envelope is null)
            {
                throw new JsonException("Envelope deserialized as null.");
            }

            return envelope;
        }
        catch (JsonException ex)
        {
            throw new LgTvCommandException($"Failed to parse response from LG TV for '{requestUri}'.", ex);
        }
    }

    private static string? MapAppIdToInputId(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return null;
        }

        const string hdmiPrefix = "com.webos.app.hdmi";

        if (appId.StartsWith(hdmiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = appId[hdmiPrefix.Length..];
            var digitLength = 0;

            while (digitLength < suffix.Length && char.IsDigit(suffix[digitLength]))
            {
                digitLength++;
            }

            if (digitLength > 0 && int.TryParse(suffix[..digitLength], out var index))
            {
                return $"HDMI_{index}";
            }
        }

        return null;
    }


    private sealed record ForegroundAppInfoPayload(
        [property: JsonPropertyName("returnValue")] bool ReturnValue,
        [property: JsonPropertyName("appId")] string? AppId,
        [property: JsonPropertyName("foregroundAppInfo")] ForegroundAppInfo? ForegroundAppInfo)
    {
        public string? GetAppId() => AppId ?? ForegroundAppInfo?.AppId;
    }

    private sealed record ForegroundAppInfo(
        [property: JsonPropertyName("appId")] string? AppId);
}