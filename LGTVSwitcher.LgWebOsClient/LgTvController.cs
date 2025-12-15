using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

using LGTVSwitcher.Core.LgTv;

using Microsoft.Extensions.Logging;

namespace LGTVSwitcher.LgWebOsClient;

public sealed class LgTvController : ILgTvController
{
    private readonly ILgTvSession _session;
    private readonly ILgTvResponseParser _responseParser;
    private readonly ILogger<LgTvController> _logger;

    public LgTvController(
        ILgTvSession session,
        ILgTvResponseParser responseParser,
        ILogger<LgTvController> logger)
    {
        _session = session;
        _responseParser = responseParser;
        _logger = logger;
    }

    public async Task<string?> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        await _session.EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        return null;
    }

    public async Task SwitchInputAsync(string inputId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputId))
        {
            throw new ArgumentException("InputId cannot be empty.", nameof(inputId));
        }

        _logger.LogInformation("Sending switchInput to {InputId}", inputId);
        await _session.SendRequestAsync(
            LgTvUris.SwitchInput,
            new SwitchInputPayload(inputId),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetCurrentInputAsync(CancellationToken cancellationToken)
    {
        var payloadElement = await _session.SendRequestAsync(
            LgTvUris.GetForegroundAppInfo,
            payload: null,
            cancellationToken).ConfigureAwait(false);

        var payloadJson = payloadElement.HasValue ? payloadElement.Value.GetRawText() : null;
        return _responseParser.ParseCurrentInput(payloadJson);
    }

    public ValueTask DisposeAsync()
    {
        return _session.DisposeAsync();
    }

    private static class LgTvUris
    {
        public const string SwitchInput = "ssap://tv/switchInput";
        public const string GetForegroundAppInfo = "ssap://com.webos.applicationManager/getForegroundAppInfo";
    }

    private sealed record SwitchInputPayload([property: JsonPropertyName("inputId")] string InputId);
}
