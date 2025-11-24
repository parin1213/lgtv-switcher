using System;
using System.Linq;
using System.Runtime.Versioning;

using LGTVSwitcher.Core.Display;

using Microsoft.Extensions.Logging;

namespace LGTVSwitcher.DisplayDetection.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsMonitorDetector : IDisplayChangeDetector
{
    private readonly WindowsMessagePump _messagePump;
    private readonly IMonitorEnumerator _enumerator;
    private readonly object _snapshotGate = new();
    private IReadOnlyList<MonitorSnapshot> _lastSnapshot = Array.Empty<MonitorSnapshot>();
    private bool _started;
    private bool _disposed;
    private readonly ILogger<WindowsMonitorDetector> _logger;


    public WindowsMonitorDetector(
        WindowsMessagePump messagePump,
        IMonitorEnumerator enumerator,
        ILogger<WindowsMonitorDetector> logger)
    {
        _messagePump = messagePump;
        _enumerator = enumerator;
        _messagePump.WindowMessageReceived += OnWindowMessageReceived;
        _logger = logger;
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

        _logger.LogDebug(
            "Display snapshot changed. Reason={Reason}, Monitors={@Monitors}",
            reason,
            snapshot);

        DisplayChanged?.Invoke(this, new DisplaySnapshotChangedEventArgs(snapshot, reason));
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