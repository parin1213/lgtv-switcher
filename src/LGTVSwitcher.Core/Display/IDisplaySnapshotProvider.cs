namespace LGTVSwitcher.Core.Display;

public interface IDisplaySnapshotProvider
{
    IObservable<DisplaySnapshot> Notifications { get; }

    Task StartAsync(CancellationToken cancellationToken);
}