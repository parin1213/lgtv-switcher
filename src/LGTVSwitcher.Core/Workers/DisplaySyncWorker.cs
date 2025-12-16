using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reactive;
using System.Reactive.Linq;

using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LGTVSwitcher.Core.Workers;

public sealed class DisplaySyncWorker : BackgroundService
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(800);

    private readonly IDisplaySnapshotProvider _snapshotProvider;
    private readonly ILgTvController _lgTvController;
    private readonly ILogger<DisplaySyncWorker> _logger;
    private readonly LgTvSwitcherOptions _options;
    private IDisposable? _subscription;

    public DisplaySyncWorker(
        IDisplaySnapshotProvider snapshotProvider,
        ILgTvController lgTvController,
        IOptions<LgTvSwitcherOptions> options,
        ILogger<DisplaySyncWorker> logger)
    {
        _snapshotProvider = snapshotProvider;
        _lgTvController = lgTvController;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _snapshotProvider.StartAsync(stoppingToken).ConfigureAwait(false);

        var comparer = new SnapshotEqualityComparer(GetTargetInput);

        _subscription = _snapshotProvider.Notifications
            .Select(n => n.Snapshot)
            .Buffer(DebounceInterval)
            .Where(buffer => buffer.Count > 0)
            .Select(buffer => buffer[^1])
            .Where(snapshot =>
                !string.IsNullOrWhiteSpace(snapshot.PreferredMonitorEdidKey) &&
                (snapshot.PreferredMonitor is null || snapshot.PreferredMonitor.Connection != MonitorConnectionKind.Unknown))
            .DistinctUntilChanged(comparer)
            .SelectMany(snapshot =>
                Observable.FromAsync(ct => SyncLgTvAsync(snapshot, ct))
                    .Select(_ => Unit.Default)
                    .Catch<Unit, WebSocketException>(ex =>
                    {
                        _logger.LogWarning("LG TV transport failed; reconnecting: {Message}", ex.Message);
                        _logger.LogDebug(ex, "WebSocket exception details.");
                        return Observable.Empty<Unit>();
                    })
                    .Catch<Unit, Exception>(ex =>
                    {
                        _logger.LogWarning(ex, "LG TV sync failed: {Message}", ex.Message);
                        return Observable.Empty<Unit>();
                    }))
            .Subscribe(
                _ => { },
                ex => _logger.LogError(ex, "Display sync pipeline error."));

        stoppingToken.Register(() => _subscription?.Dispose());

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        return base.StopAsync(cancellationToken);
    }

    private async Task SyncLgTvAsync(DisplaySnapshot snapshot, CancellationToken cancellationToken)
    {
        var age = DateTimeOffset.UtcNow - snapshot.Timestamp;
        if (age > TimeSpan.FromSeconds(5))
        {
            _logger.LogDebug("Stale snapshot ({Age}); skipping LG TV sync.", age);
            return;
        }

        var targetInput = GetTargetInput(snapshot);
        if (string.IsNullOrWhiteSpace(targetInput))
        {
            _logger.LogInformation(
                "No input mapping configured for preferred monitor state {State}; skipping.",
                snapshot.PreferredMonitorOnline);
            return;
        }

        string? currentInput = null;
        try
        {
            currentInput = await _lgTvController.GetCurrentInputAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException || ex is HttpRequestException || ex is SocketException)
        {
            _logger.LogWarning("LG TV query skipped due to transport error: {Message}", ex.Message);
            _logger.LogDebug(ex, "Transport exception while querying current input.");
            return;
        }
        catch (Exception ex) when (ex is not WebSocketException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Failed to query current LG TV input; proceeding with switch.");
        }

        if (!string.IsNullOrWhiteSpace(currentInput) &&
            string.Equals(currentInput, targetInput, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("LG TV already set to {Input}; no switch required.", targetInput);
            return;
        }

        _logger.LogInformation("Switching LG TV input to {Input} (preferred monitor online = {State})", targetInput, snapshot.PreferredMonitorOnline);
        try
        {
            await _lgTvController.SwitchInputAsync(targetInput, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException || ex is HttpRequestException || ex is SocketException)
        {
            _logger.LogWarning("LG TV switch skipped due to transport error: {Message}", ex.Message);
            _logger.LogDebug(ex, "Transport exception while switching input.");
        }
    }

    private string? GetTargetInput(DisplaySnapshot snapshot)
        => snapshot.PreferredMonitorOnline ? _options.TargetInputId : _options.FallbackInputId;
}