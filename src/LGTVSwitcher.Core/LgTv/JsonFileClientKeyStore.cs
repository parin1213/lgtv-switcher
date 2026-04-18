using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace LGTVSwitcher.Core.LgTv;

public sealed class JsonFileClientKeyStore : ILgTvClientKeyStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;
    private readonly ILogger<JsonFileClientKeyStore> _logger;

    public JsonFileClientKeyStore(string filePath, ILogger<JsonFileClientKeyStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    public async Task<LgTvPersistedState> GetStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return new LgTvPersistedState();

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<LgTvPersistedState>(json, SerializerOptions) ?? new LgTvPersistedState();
        }
        catch (Exception ex) when (ex is JsonException || ex is IOException)
        {
            _logger.LogWarning(ex, "Failed to read state file {Path}; starting with empty state.", _filePath);
            return new LgTvPersistedState();
        }
    }

    public async Task PersistClientKeyAsync(string clientKey, CancellationToken cancellationToken)
        => await PersistStateAsync(clientKey, preferredTvUsn: null, cancellationToken).ConfigureAwait(false);

    public async Task PersistPreferredTvUsnAsync(string preferredTvUsn, CancellationToken cancellationToken)
        => await PersistStateAsync(clientKey: null, preferredTvUsn, cancellationToken).ConfigureAwait(false);

    private async Task PersistStateAsync(string? clientKey, string? preferredTvUsn, CancellationToken cancellationToken)
    {
        var current = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        var updated = current with
        {
            ClientKey = clientKey ?? current.ClientKey,
            PreferredTvUsn = preferredTvUsn ?? current.PreferredTvUsn,
        };

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(
            _filePath,
            JsonSerializer.Serialize(updated, SerializerOptions),
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Persisted client state to {Path}", _filePath);
    }
}
