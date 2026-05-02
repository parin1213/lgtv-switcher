using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace LGTVSwitcher.Core.LgTv;

public sealed class JsonFileClientKeyStore : ILgTvClientKeyStore
{
    private const string LegacyStateFileName = "device-state.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;
    private readonly ILogger<JsonFileClientKeyStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileClientKeyStore(string filePath, ILogger<JsonFileClientKeyStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    public async Task<LgTvPersistedState> GetStateAsync(CancellationToken cancellationToken)
    {
        var path = ResolveReadableStatePath();
        if (path is null)
        {
            return new LgTvPersistedState();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return ParseState(json);
        }
        catch (Exception ex) when (ex is JsonException || ex is IOException)
        {
            _logger.LogWarning(ex, "Failed to read state file {Path}; starting with empty state.", path);
            return new LgTvPersistedState();
        }
    }

    public async Task PersistClientKeyAsync(string clientKey, CancellationToken cancellationToken)
        => await PersistStateAsync(clientKey, preferredTvUsn: null, cancellationToken).ConfigureAwait(false);

    public async Task PersistPreferredTvUsnAsync(string preferredTvUsn, CancellationToken cancellationToken)
        => await PersistStateAsync(clientKey: null, preferredTvUsn, cancellationToken).ConfigureAwait(false);

    private async Task PersistStateAsync(string? clientKey, string? preferredTvUsn, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            var updated = current with
            {
                ClientKey = clientKey ?? current.ClientKey,
                PreferredTvUsn = preferredTvUsn ?? current.PreferredTvUsn,
            };

            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(
                    tempPath,
                    JsonSerializer.Serialize(updated, SerializerOptions),
                    cancellationToken).ConfigureAwait(false);

                File.Move(tempPath, _filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            _logger.LogInformation("Persisted client state to {Path}", _filePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string? ResolveReadableStatePath()
    {
        if (File.Exists(_filePath))
        {
            return _filePath;
        }

        var directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var legacyPath = Path.Combine(directory, LegacyStateFileName);
        return File.Exists(legacyPath) ? legacyPath : null;
    }

    private static LgTvPersistedState ParseState(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("LgTvSwitcher", out var legacySection) &&
            legacySection.ValueKind == JsonValueKind.Object)
        {
            return new LgTvPersistedState(
                ReadString(legacySection, "ClientKey"),
                ReadString(legacySection, "PreferredTvUsn"));
        }

        return JsonSerializer.Deserialize<LgTvPersistedState>(json, SerializerOptions) ?? new LgTvPersistedState();
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
