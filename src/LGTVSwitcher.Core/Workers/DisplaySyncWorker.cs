using System.Threading.Channels;

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
    private volatile DisplaySnapshot? _latestSnapshot;

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
        var channel = Channel.CreateUnbounded<DisplaySnapshot>(new UnboundedChannelOptions { SingleReader = true });

        using var sub = _snapshotProvider.Notifications.Subscribe(snapshot =>
        {
            _latestSnapshot = snapshot;
            channel.Writer.TryWrite(snapshot);
        });

        await _snapshotProvider.StartAsync(stoppingToken).ConfigureAwait(false);

        var periodicTask = _syncInterval.HasValue
            ? RunPeriodicSyncAsync(stoppingToken)
            : Task.CompletedTask;

        await RunDebounceLoopAsync(channel.Reader, stoppingToken).ConfigureAwait(false);
        channel.Writer.TryComplete();

        await periodicTask.ConfigureAwait(false);
    }

    private async Task RunDebounceLoopAsync(ChannelReader<DisplaySnapshot> reader, CancellationToken ct)
    {
        var comparer = new SnapshotEqualityComparer(GetTargetInput);
        DisplaySnapshot? lastProcessed = null;

        while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            DisplaySnapshot? latest = null;
            using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            windowCts.CancelAfter(DebounceInterval);

            try
            {
                await foreach (var s in reader.ReadAllAsync(windowCts.Token).ConfigureAwait(false))
                    latest = s;
            }
            catch (OperationCanceledException) when (windowCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // 800ms window elapsed — process latest
            }

            if (latest is null || ct.IsCancellationRequested) continue;
            if (!IsEligibleSnapshot(latest)) continue;
            if (comparer.Equals(latest, lastProcessed)) continue;

            lastProcessed = latest;
            await TrySyncAsync(latest, ct).ConfigureAwait(false);
        }
    }

    private async Task RunPeriodicSyncAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_syncInterval!.Value);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            var snapshot = _latestSnapshot;
            if (snapshot is null || !IsEligibleSnapshot(snapshot)) continue;

            var refreshed = new DisplaySnapshot(
                DateTimeOffset.UtcNow,
                snapshot.Monitors,
                snapshot.PreferredMonitor,
                snapshot.PreferredMonitorOnline,
                snapshot.PreferredMonitorEdidKey);

            await TrySyncAsync(refreshed, ct).ConfigureAwait(false);
        }
    }

    private async Task TrySyncAsync(DisplaySnapshot snapshot, CancellationToken ct)
    {
        try
        {
            await SyncLgTvAsync(snapshot, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LG TV sync failed.");
        }
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
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (_allowedCurrentInputIds.Length > 0)
            {
                _logger.LogWarning("Failed to query LG TV input; skipping switch (allowed list configured): {Message}", ex.Message);
                return;
            }

            _logger.LogWarning("Failed to query LG TV input; proceeding with switch: {Message}", ex.Message);
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
        await _lgTvController.SwitchInputAsync(targetInput, cancellationToken).ConfigureAwait(false);
    }

    private string? GetTargetInput(DisplaySnapshot snapshot)
        => snapshot.PreferredMonitorOnline ? _options.TargetInputId : _options.FallbackInputId;

    private bool IsCurrentInputAllowed(string? currentInput)
    {
        if (_allowedCurrentInputIds.Length == 0) return true;
        if (string.IsNullOrWhiteSpace(currentInput)) return false;

        foreach (var allowed in _allowedCurrentInputIds)
        {
            if (string.Equals(allowed, currentInput, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsEligibleSnapshot(DisplaySnapshot snapshot)
        => !string.IsNullOrWhiteSpace(snapshot.PreferredMonitorEdidKey) &&
           (snapshot.PreferredMonitor is null || snapshot.PreferredMonitor.ConnectionKind != MonitorConnectionKind.Unknown);
}
