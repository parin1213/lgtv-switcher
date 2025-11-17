using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

using LGTVSwitcher.Core.LgTv;

using Microsoft.Extensions.Logging;

namespace LGTVSwitcher.Sandbox.Windows;

public sealed class FileBasedLgTvClientKeyStore : ILgTvClientKeyStore
{
    private readonly string _filePath;
    private readonly ILogger<FileBasedLgTvClientKeyStore> _logger;

    public FileBasedLgTvClientKeyStore(string filePath, ILogger<FileBasedLgTvClientKeyStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    public async Task PersistClientKeyAsync(string clientKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientKey))
        {
            return;
        }

        JsonNode root;

        if (File.Exists(_filePath))
        {
            var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            root = JsonNode.Parse(json) ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        if (root is not JsonObject rootObject)
        {
            rootObject = new JsonObject();
            root = rootObject;
        }

        var section = rootObject["LgTvSwitcher"] as JsonObject ?? new JsonObject();
        rootObject["LgTvSwitcher"] = section;
        section["ClientKey"] = clientKey;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var updatedJson = root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        await File.WriteAllTextAsync(_filePath, updatedJson, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Persisted client-key to {Path}", _filePath);
    }
}