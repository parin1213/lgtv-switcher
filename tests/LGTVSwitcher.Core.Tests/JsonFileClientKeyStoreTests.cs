using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using LGTVSwitcher.Core.LgTv;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace LGTVSwitcher.Core.Tests;

public sealed class JsonFileClientKeyStoreTests
{
    [Fact]
    public async Task GetStateAsync_MissingFile_ReturnsEmptyState()
    {
        var path = CreateTempPath();
        var store = CreateStore(path);

        var state = await store.GetStateAsync(CancellationToken.None);

        Assert.Null(state.ClientKey);
        Assert.Null(state.PreferredTvUsn);
    }

    [Fact]
    public async Task PersistMethods_PreserveExistingValues()
    {
        var path = CreateTempPath();
        var store = CreateStore(path);

        await store.PersistClientKeyAsync("client-1", CancellationToken.None);
        await store.PersistPreferredTvUsnAsync("uuid:tv-1", CancellationToken.None);

        var state = await store.GetStateAsync(CancellationToken.None);

        Assert.Equal("client-1", state.ClientKey);
        Assert.Equal("uuid:tv-1", state.PreferredTvUsn);
    }

    [Fact]
    public async Task GetStateAsync_InvalidJson_ReturnsEmptyState()
    {
        var path = CreateTempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ invalid json", CancellationToken.None);
        var store = CreateStore(path);

        var state = await store.GetStateAsync(CancellationToken.None);

        Assert.Null(state.ClientKey);
        Assert.Null(state.PreferredTvUsn);
    }

    [Fact]
    public async Task GetStateAsync_LegacyNestedSchema_ReturnsPersistedState()
    {
        var path = CreateTempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """{"LgTvSwitcher":{"ClientKey":"legacy-key","PreferredTvUsn":"uuid:legacy"}}""",
            CancellationToken.None);
        var store = CreateStore(path);

        var state = await store.GetStateAsync(CancellationToken.None);

        Assert.Equal("legacy-key", state.ClientKey);
        Assert.Equal("uuid:legacy", state.PreferredTvUsn);
    }

    [Fact]
    public async Task GetStateAsync_NewStateMissing_ReadsLegacyDeviceStateFile()
    {
        var path = CreateTempPath();
        var legacyPath = Path.Combine(Path.GetDirectoryName(path)!, "device-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            legacyPath,
            """{"LgTvSwitcher":{"ClientKey":"legacy-key","PreferredTvUsn":"uuid:legacy"}}""",
            CancellationToken.None);
        var store = CreateStore(path);

        var state = await store.GetStateAsync(CancellationToken.None);

        Assert.Equal("legacy-key", state.ClientKey);
        Assert.Equal("uuid:legacy", state.PreferredTvUsn);
    }

    [Fact]
    public async Task PersistClientKeyAsync_MigratesLegacyDeviceStateToNewStateFile()
    {
        var path = CreateTempPath();
        var legacyPath = Path.Combine(Path.GetDirectoryName(path)!, "device-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            legacyPath,
            """{"LgTvSwitcher":{"ClientKey":"legacy-key","PreferredTvUsn":"uuid:legacy"}}""",
            CancellationToken.None);
        var store = CreateStore(path);

        await store.PersistClientKeyAsync("new-key", CancellationToken.None);

        var state = await store.GetStateAsync(CancellationToken.None);
        Assert.True(File.Exists(path));
        Assert.Equal("new-key", state.ClientKey);
        Assert.Equal("uuid:legacy", state.PreferredTvUsn);
    }

    private static JsonFileClientKeyStore CreateStore(string path)
        => new(path, NullLogger<JsonFileClientKeyStore>.Instance);

    private static string CreateTempPath()
        => Path.Combine(Path.GetTempPath(), "lgtv-switcher-tests", Guid.NewGuid().ToString("N"), "state.json");
}
