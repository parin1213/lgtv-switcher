using System.Collections.ObjectModel;

namespace LGTVSwitcher.Core.Display;

public sealed class DisplaySnapshot
{
    public DisplaySnapshot(
        DateTimeOffset timestamp,
        IEnumerable<MonitorSnapshot> monitors,
        MonitorSnapshot? preferredMonitor,
        bool preferredMonitorOnline,
        string? preferredMonitorEdidKey)
    {
        Timestamp = timestamp;
        Monitors = monitors is ReadOnlyCollection<MonitorSnapshot> readOnly
            ? readOnly
            : new ReadOnlyCollection<MonitorSnapshot>(monitors.ToArray());
        PreferredMonitor = preferredMonitor;
        PreferredMonitorOnline = preferredMonitorOnline;
        PreferredMonitorEdidKey = preferredMonitorEdidKey;
    }

    public DateTimeOffset Timestamp { get; }
    public IReadOnlyList<MonitorSnapshot> Monitors { get; }
    public MonitorSnapshot? PreferredMonitor { get; }
    public bool PreferredMonitorOnline { get; }
    public string? PreferredMonitorEdidKey { get; }
}

public sealed class SnapshotEqualityComparer : IEqualityComparer<DisplaySnapshot>
{
    private readonly Func<DisplaySnapshot, string?>? _targetInputSelector;

    public SnapshotEqualityComparer(Func<DisplaySnapshot, string?>? targetInputSelector = null)
    {
        _targetInputSelector = targetInputSelector;
    }

    public bool Equals(DisplaySnapshot? x, DisplaySnapshot? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        if (x.PreferredMonitorOnline != y.PreferredMonitorOnline) return false;
        if (!string.Equals(x.PreferredMonitorEdidKey, y.PreferredMonitorEdidKey, StringComparison.OrdinalIgnoreCase)) return false;

        var xConnection = x.PreferredMonitor?.ConnectionKind ?? MonitorConnectionKind.Unknown;
        var yConnection = y.PreferredMonitor?.ConnectionKind ?? MonitorConnectionKind.Unknown;
        if (xConnection != yConnection) return false;

        if (_targetInputSelector is not null)
        {
            var xInput = _targetInputSelector(x);
            var yInput = _targetInputSelector(y);
            if (!string.Equals(xInput, yInput, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    public int GetHashCode(DisplaySnapshot obj)
    {
        unchecked
        {
            var hash = obj.PreferredMonitorOnline.GetHashCode();
            hash = (hash * 397) ^ (obj.PreferredMonitorEdidKey?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0);
            hash = (hash * 397) ^ (obj.PreferredMonitor?.ConnectionKind.GetHashCode() ?? MonitorConnectionKind.Unknown.GetHashCode());
            if (_targetInputSelector is not null)
                hash = (hash * 397) ^ (_targetInputSelector(obj)?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0);
            return hash;
        }
    }
}