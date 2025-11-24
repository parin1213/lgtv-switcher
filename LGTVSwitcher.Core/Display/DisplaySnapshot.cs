using System.Collections.ObjectModel;

namespace LGTVSwitcher.Core.Display;

/// <summary>
/// Rx パイプラインを通して扱うディスプレイスナップショット。
/// </summary>
public sealed class DisplaySnapshot
{
    public DisplaySnapshot(
        DateTimeOffset timestamp,
        IEnumerable<MonitorInfo> monitors,
        MonitorInfo? preferredMonitor,
        bool preferredMonitorOnline,
        string? preferredMonitorEdidKey)
    {
        Timestamp = timestamp;
        Monitors = monitors is ReadOnlyCollection<MonitorInfo> readOnly
            ? readOnly
            : new ReadOnlyCollection<MonitorInfo>(monitors.ToArray());
        PreferredMonitor = preferredMonitor;
        PreferredMonitorOnline = preferredMonitorOnline;
        PreferredMonitorEdidKey = preferredMonitorEdidKey;
    }

    /// <summary>スナップショットが取得された時刻。</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>観測されたすべてのモニタ情報。</summary>
    public IReadOnlyList<MonitorInfo> Monitors { get; }

    /// <summary>優先モニタの情報（見つからない場合は null）。</summary>
    public MonitorInfo? PreferredMonitor { get; }

    /// <summary>優先モニタがオンラインかどうか。</summary>
    public bool PreferredMonitorOnline { get; }

    /// <summary>優先モニタを識別する EDID / InstanceId キー。</summary>
    public string? PreferredMonitorEdidKey { get; }
}

/// <summary>
/// DisplaySnapshot 同士を比較し、実質的な状態変化のみを検出するための比較器。
/// </summary>
public sealed class SnapshotEqualityComparer : IEqualityComparer<DisplaySnapshot>
{
    private readonly Func<DisplaySnapshot, string?>? _targetInputSelector;

    public SnapshotEqualityComparer(Func<DisplaySnapshot, string?>? targetInputSelector = null)
    {
        _targetInputSelector = targetInputSelector;
    }

    public bool Equals(DisplaySnapshot? x, DisplaySnapshot? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        if (x.PreferredMonitorOnline != y.PreferredMonitorOnline)
        {
            return false;
        }

        if (!string.Equals(x.PreferredMonitorEdidKey, y.PreferredMonitorEdidKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var xConnection = x.PreferredMonitor?.Connection ?? MonitorConnectionKind.Unknown;
        var yConnection = y.PreferredMonitor?.Connection ?? MonitorConnectionKind.Unknown;

        if (xConnection != yConnection)
        {
            return false;
        }

        if (_targetInputSelector is not null)
        {
            var xInput = _targetInputSelector(x);
            var yInput = _targetInputSelector(y);
            if (!string.Equals(xInput, yInput, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(DisplaySnapshot obj)
    {
        unchecked
        {
            var hash = obj.PreferredMonitorOnline.GetHashCode();
            hash = (hash * 397) ^ (obj.PreferredMonitorEdidKey?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0);
            hash = (hash * 397) ^ (obj.PreferredMonitor?.Connection.GetHashCode() ?? MonitorConnectionKind.Unknown.GetHashCode());
            if (_targetInputSelector is not null)
            {
                hash = (hash * 397) ^ (_targetInputSelector(obj)?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0);
            }
            return hash;
        }
    }
}

/// <summary>
/// Rx パイプラインで扱う軽量なモニタ情報。
/// </summary>
public sealed record MonitorInfo(
    string DeviceName,
    string FriendlyName,
    bool IsPrimary,
    MonitorConnectionKind Connection);