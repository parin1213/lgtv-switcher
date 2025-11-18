using System;
using System.Linq;
using System.Runtime.Versioning;
using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;
using Microsoft.Extensions.Options;

namespace LGTVSwitcher.DisplayDetection.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsMonitorDetector : IDisplayChangeDetector
{
    private readonly WindowsMessagePump _messagePump;
    private readonly IMonitorEnumerator _enumerator;
    private readonly IDisplaySnapshotStream _snapshotStream;
    private readonly LgTvSwitcherOptions _options;
    private readonly object _snapshotGate = new();
    private IReadOnlyList<MonitorSnapshot> _lastSnapshot = Array.Empty<MonitorSnapshot>();
    private bool _started;
    private bool _disposed;

    public WindowsMonitorDetector(
        WindowsMessagePump messagePump,
        IMonitorEnumerator enumerator,
        IDisplaySnapshotStream snapshotStream,
        IOptions<LgTvSwitcherOptions> options)
    {
        _messagePump = messagePump;
        _enumerator = enumerator;
        _snapshotStream = snapshotStream;
        _options = options.Value;
        _messagePump.WindowMessageReceived += OnWindowMessageReceived;
    }

    public event EventHandler<DisplaySnapshotChangedEventArgs>? DisplayChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_started)
        {
            return;
        }

        try
        {
            await _messagePump.StartAsync(cancellationToken).ConfigureAwait(false);
            _started = true;
            PublishSnapshot("initial-startup");
        }
        catch
        {
            _started = false;
            throw;
        }
    }

    private void OnWindowMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        if (e.Kind is WindowMessageKind.DisplayChanged or WindowMessageKind.DeviceChanged)
        {
            PublishSnapshot($"message-{e.Kind}");
        }
    }

    private void PublishSnapshot(string reason)
    {
        var snapshot = _enumerator.EnumerateCurrentMonitors();

        if (!ShouldPublish(snapshot))
        {
            return;
        }

        DisplayChanged?.Invoke(this, new DisplaySnapshotChangedEventArgs(snapshot, reason));

        var displaySnapshot = CreateDisplaySnapshot(snapshot);
        _snapshotStream.Publish(displaySnapshot);
    }

    private bool ShouldPublish(IReadOnlyList<MonitorSnapshot> snapshot)
    {
        lock (_snapshotGate)
        {
            if (_lastSnapshot.Count == snapshot.Count && _lastSnapshot.SequenceEqual(snapshot))
            {
                return false;
            }

            _lastSnapshot = snapshot;
            return true;
        }
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

        var configuredKey = _options.PreferredMonitorName;
        var preferredKey = preferredSnapshot?.EdidKey ?? preferredSnapshot?.DeviceName ?? configuredKey;

        return new DisplaySnapshot(
            timestamp: DateTimeOffset.UtcNow,
            monitors: monitors,
            preferredMonitor: preferredInfo,
            preferredMonitorOnline: preferredSnapshot is not null,
            preferredMonitorEdidKey: preferredKey);
    }

    private MonitorSnapshot? FindPreferredMonitor(IReadOnlyList<MonitorSnapshot> snapshot)
    {
        return snapshot.FirstOrDefault(MatchesPreferredMonitor);
    }

    private bool MatchesPreferredMonitor(MonitorSnapshot monitor)
    {
        var preferredKey = _options.PreferredMonitorName;
        if (string.IsNullOrWhiteSpace(preferredKey))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(monitor.EdidKey) &&
            monitor.EdidKey.Contains(preferredKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(monitor.DeviceName, preferredKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(monitor.FriendlyName, preferredKey, StringComparison.OrdinalIgnoreCase) ||
            monitor.FriendlyName.Contains(preferredKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _messagePump.WindowMessageReceived -= OnWindowMessageReceived;
        await _messagePump.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WindowsMonitorDetector));
        }
    }
}
