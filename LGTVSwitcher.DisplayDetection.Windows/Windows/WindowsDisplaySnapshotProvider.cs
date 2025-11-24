using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;

using Microsoft.Extensions.Options;

namespace LGTVSwitcher.DisplayDetection.Windows;

public sealed class WindowsDisplaySnapshotProvider : IDisplaySnapshotProvider, IAsyncDisposable
{
    private readonly IDisplayChangeDetector _detector;
    private readonly LgTvSwitcherOptions _options;
    private readonly Subject<DisplaySnapshotNotification> _subject = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private bool _started;

    public WindowsDisplaySnapshotProvider(
        IDisplayChangeDetector detector,
        IOptions<LgTvSwitcherOptions> options)
    {
        _detector = detector;
        _options = options.Value;
        _detector.DisplayChanged += OnDisplayChanged;
    }

    public IObservable<DisplaySnapshotNotification> Notifications => _subject.AsObservable();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started)
            {
                return;
            }

            await _detector.StartAsync(cancellationToken).ConfigureAwait(false);
            _started = true;
        }
        finally
        {
            _startGate.Release();
        }
    }

    private void OnDisplayChanged(object? sender, DisplaySnapshotChangedEventArgs e)
    {
        var snapshot = CreateDisplaySnapshot(e.Snapshots);
        _subject.OnNext(new DisplaySnapshotNotification(snapshot, e.Reason));
    }

    private DisplaySnapshot CreateDisplaySnapshot(IReadOnlyList<MonitorSnapshot> monitorSnapshots)
    {
        var monitors = monitorSnapshots
            .Select(m => new MonitorInfo(m.DeviceName, m.FriendlyName, m.IsPrimary, m.ConnectionKind))
            .ToArray();

        var preferredSnapshot = FindPreferredMonitor(monitorSnapshots);
        MonitorInfo? preferredInfo = preferredSnapshot is null
            ? null
            : new MonitorInfo(
                preferredSnapshot.DeviceName,
                preferredSnapshot.FriendlyName,
                preferredSnapshot.IsPrimary,
                preferredSnapshot.ConnectionKind);

        var key = preferredSnapshot?.EdidKey ?? preferredSnapshot?.DeviceName ?? _options.PreferredMonitorName;

        return new DisplaySnapshot(
            DateTimeOffset.UtcNow,
            monitors,
            preferredInfo,
            preferredSnapshot is not null,
            key);
    }

    private MonitorSnapshot? FindPreferredMonitor(IReadOnlyList<MonitorSnapshot> snapshots)
    {
        var preferred = _options.PreferredMonitorName;
        if (string.IsNullOrWhiteSpace(preferred))
        {
            return null;
        }

        return snapshots.FirstOrDefault(monitor =>
            (!string.IsNullOrWhiteSpace(monitor.EdidKey) &&
             monitor.EdidKey.Contains(preferred, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(monitor.DeviceName, preferred, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(monitor.FriendlyName) &&
             monitor.FriendlyName.Contains(preferred, StringComparison.OrdinalIgnoreCase)));
    }

    public async ValueTask DisposeAsync()
    {
        _detector.DisplayChanged -= OnDisplayChanged;
        _subject.OnCompleted();
        _subject.Dispose();
        await _detector.DisposeAsync().ConfigureAwait(false);
    }
}