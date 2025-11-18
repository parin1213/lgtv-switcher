using System.Text.Json;
using System.Text.Json.Serialization;

namespace LGTVSwitcher.LgWebOsClient;

public sealed record LgTvResponseEnvelope(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("payload")] JsonElement Payload,
    [property: JsonPropertyName("error")] string? Error);