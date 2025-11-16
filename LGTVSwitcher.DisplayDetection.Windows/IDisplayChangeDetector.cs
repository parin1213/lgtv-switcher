namespace LGTVSwitcher.DisplayDetection;

public interface IDisplayChangeDetector : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Raised whenever the OS indicates the display topology might have changed（ディスプレイトポロジーが変わった可能性があるときに発火する）。
    /// </summary>
    event EventHandler<DisplaySnapshotChangedEventArgs> DisplayChanged;

    /// <summary>
    /// Starts monitoring and publishes an initial snapshot immediately（監視を開始して、初回スナップショットを即座に通知する）。
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);
}
