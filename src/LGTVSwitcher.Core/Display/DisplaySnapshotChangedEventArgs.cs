using System.Collections.ObjectModel;

namespace LGTVSwitcher.Core.Display;

public sealed class DisplaySnapshotChangedEventArgs(IReadOnlyList<MonitorSnapshot> snapshots, string reason)
    : EventArgs
{
    public IReadOnlyList<MonitorSnapshot> Snapshots { get; } =
        snapshots is ReadOnlyCollection<MonitorSnapshot>
            ? snapshots
            : new ReadOnlyCollection<MonitorSnapshot>(snapshots.ToArray());

    public string Reason { get; } = reason;

    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}