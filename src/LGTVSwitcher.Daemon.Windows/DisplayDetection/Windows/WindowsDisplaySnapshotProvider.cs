using System.Reactive.Linq;
using System.Reactive.Subjects;

using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;

using Microsoft.Extensions.Options;

namespace LGTVSwitcher.Daemon.Windows.DisplayDetection;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class WindowsDisplaySnapshotProvider : IDisplaySnapshotProvider, IAsyncDisposable
{
    private readonly WindowsMonitorDetector _detector;
    private readonly LgTvSwitcherOptions _options;
    private readonly Subject<DisplaySnapshot> _subject = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private bool _started;

    public WindowsDisplaySnapshotProvider(
        WindowsMonitorDetector detector,
        IOptions<LgTvSwitcherOptions> options)
    {
        _detector = detector;
        _options = options.Value;
        _detector.DisplayChanged += OnDisplayChanged;
    }

    public IObservable<DisplaySnapshot> Notifications => _subject.AsObservable();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started) return;

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started) return;
            await _detector.StartAsync(cancellationToken).ConfigureAwait(false);
            _started = true;
        }
        finally
        {
            _startGate.Release();
        }
    }

    private void OnDisplayChanged(object? sender, DisplaySnapshotChangedEventArgs e)
        => _subject.OnNext(BuildSnapshot(e.Snapshots));

    private DisplaySnapshot BuildSnapshot(IReadOnlyList<MonitorSnapshot> monitorSnapshots)
    {
        var preferred = FindPreferredMonitor(monitorSnapshots);
        var key = preferred?.EdidKey ?? preferred?.DeviceName ?? _options.PreferredMonitorName;
        return new DisplaySnapshot(DateTimeOffset.UtcNow, monitorSnapshots, preferred, preferred is not null, key);
    }

    private MonitorSnapshot? FindPreferredMonitor(IReadOnlyList<MonitorSnapshot> snapshots)
    {
        var preferred = _options.PreferredMonitorName;
        if (string.IsNullOrWhiteSpace(preferred)) return null;

        return snapshots.FirstOrDefault(m =>
            (!string.IsNullOrWhiteSpace(m.EdidKey) && m.EdidKey.Contains(preferred, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(m.DeviceName, preferred, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(m.FriendlyName) && m.FriendlyName.Contains(preferred, StringComparison.OrdinalIgnoreCase)));
    }

    public async ValueTask DisposeAsync()
    {
        _detector.DisplayChanged -= OnDisplayChanged;
        _subject.OnCompleted();
        _subject.Dispose();
        await _detector.DisposeAsync().ConfigureAwait(false);
    }
}
