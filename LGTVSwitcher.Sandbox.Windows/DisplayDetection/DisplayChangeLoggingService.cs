using System.Reactive.Linq;

using LGTVSwitcher.Core.Display;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LGTVSwitcher.Sandbox.Windows.DisplayDetection;

internal sealed class DisplayChangeLoggingService : BackgroundService
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(500);

    private readonly IDisplaySnapshotProvider _snapshotProvider;
    private readonly ILogger<DisplayChangeLoggingService> _logger;
    private readonly SnapshotEqualityComparer _comparer = new();
    private IDisposable? _subscription;

    public DisplayChangeLoggingService(
        IDisplaySnapshotProvider snapshotProvider,
        ILogger<DisplayChangeLoggingService> logger)
    {
        _snapshotProvider = snapshotProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _snapshotProvider.StartAsync(stoppingToken).ConfigureAwait(false);

        DisplaySnapshot? lastLogged = null;

        _subscription = _snapshotProvider.Notifications
            .Buffer(DebounceInterval)
            .Where(batch => batch.Count > 0)
            .Select(batch => batch[^1])
            .Where(notification =>
            {
                if (lastLogged is not null && _comparer.Equals(lastLogged, notification.Snapshot))
                {
                    return false;
                }

                lastLogged = notification.Snapshot;
                return true;
            })
            .Subscribe(
                LogSnapshot,
                ex => _logger.LogError(ex, "Display snapshot logging pipeline error."));

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

    private void LogSnapshot(DisplaySnapshotNotification notification)
    {
        var level = notification.Reason.Equals("initial-startup", StringComparison.OrdinalIgnoreCase)
            ? LogLevel.Information
            : LogLevel.Debug;

        var snapshot = notification.Snapshot;

        _logger.Log(level,
            "Display snapshot ({Reason}): {@Snapshot}",
            notification.Reason,
            new
            {
                snapshot.Timestamp,
                snapshot.PreferredMonitorOnline,
                snapshot.PreferredMonitorEdidKey,
                PreferredConnection = snapshot.PreferredMonitor?.Connection.ToString(),
            });
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        return base.StopAsync(cancellationToken);
    }
}