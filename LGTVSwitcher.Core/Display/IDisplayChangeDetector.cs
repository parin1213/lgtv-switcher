namespace LGTVSwitcher.Core.Display;

public interface IDisplayChangeDetector : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Raised whenever the OS indicates the display topology might have changed.
    /// </summary>
    event EventHandler<DisplaySnapshotChangedEventArgs> DisplayChanged;

    /// <summary>
    /// Starts monitoring and emits an initial snapshot immediately.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);
}
