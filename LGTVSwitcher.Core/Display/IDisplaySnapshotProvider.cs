using System.Reactive.Linq;

namespace LGTVSwitcher.Core.Display;

public sealed record DisplaySnapshotNotification(DisplaySnapshot Snapshot, string Reason);

public interface IDisplaySnapshotProvider
{
    IObservable<DisplaySnapshotNotification> Notifications { get; }

    Task StartAsync(CancellationToken cancellationToken);
}
