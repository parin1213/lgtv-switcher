namespace LGTVSwitcher.Experimental.DisplayDetection;

public interface IDisplayChangeDetector : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Raised whenever the OS indicates the display topology might have changed（ディスプレイトポロジーが変わった可能性があるときに発火するイベント）。
    /// </summary>
    event EventHandler<DisplaySnapshotChangedEventArgs> DisplayChanged;

    /// <summary>
    /// Starts listening for changes and publishes an initial snapshot immediately（監視を開始して、初期スナップショットを即座に通知する）。
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);
}
