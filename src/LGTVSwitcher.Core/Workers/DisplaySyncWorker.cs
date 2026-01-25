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
    private readonly string[] _allowedCurrentInputIds;
    private readonly TimeSpan? _syncInterval;
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
        _allowedCurrentInputIds = _options.AllowedCurrentInputIds?
            .Select(id => id?.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .OfType<string>()
            .ToArray()
            ?? Array.Empty<string>();
        _syncInterval = _options.SyncIntervalSeconds > 0
            ? TimeSpan.FromSeconds(_options.SyncIntervalSeconds)
            : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var comparer = new SnapshotEqualityComparer(GetTargetInput);

        var snapshotStream = _snapshotProvider.Notifications
            .Select(n => n.Snapshot)
            .Publish()
            .RefCount();

        var displayChanges = snapshotStream
            .Buffer(DebounceInterval)
            .Where(buffer => buffer.Count > 0)
            .Select(buffer => buffer[^1])
            .Where(IsEligibleSnapshot)
            .DistinctUntilChanged(comparer);

        var periodicSnapshots = CreatePeriodicSnapshots(snapshotStream);

        _subscription = displayChanges
            .Merge(periodicSnapshots)
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

        await _snapshotProvider.StartAsync(stoppingToken).ConfigureAwait(false);

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
            if (_allowedCurrentInputIds.Length > 0)
            {
                _logger.LogWarning(ex, "Failed to query current LG TV input; allowed list is configured so skipping switch.");
                return;
            }

            _logger.LogWarning(ex, "Failed to query current LG TV input; proceeding with switch.");
        }

        if (!IsCurrentInputAllowed(currentInput))
        {
            _logger.LogDebug(
                "LG TV current input {Input} is not in allowed list; skipping switch.",
                string.IsNullOrWhiteSpace(currentInput) ? "(unknown)" : currentInput);
            return;
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

    private bool IsCurrentInputAllowed(string? currentInput)
    {
        if (_allowedCurrentInputIds.Length == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(currentInput))
        {
            return false;
        }

        foreach (var allowed in _allowedCurrentInputIds)
        {
            if (string.Equals(allowed, currentInput, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private IObservable<DisplaySnapshot> CreatePeriodicSnapshots(IObservable<DisplaySnapshot> snapshotStream)
    {
        if (_syncInterval is null)
        {
            return Observable.Empty<DisplaySnapshot>();
        }

        return Observable.Interval(_syncInterval.Value)
            .WithLatestFrom(snapshotStream, (_, snapshot) => snapshot)
            .Select(RefreshSnapshotTimestamp)
            .Where(IsEligibleSnapshot);
    }

    private static DisplaySnapshot RefreshSnapshotTimestamp(DisplaySnapshot snapshot)
        => new(
            DateTimeOffset.UtcNow,
            snapshot.Monitors,
            snapshot.PreferredMonitor,
            snapshot.PreferredMonitorOnline,
            snapshot.PreferredMonitorEdidKey);

    private static bool IsEligibleSnapshot(DisplaySnapshot snapshot)
        => !string.IsNullOrWhiteSpace(snapshot.PreferredMonitorEdidKey) &&
           (snapshot.PreferredMonitor is null || snapshot.PreferredMonitor.Connection != MonitorConnectionKind.Unknown);
}
