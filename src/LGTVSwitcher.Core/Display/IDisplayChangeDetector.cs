namespace LGTVSwitcher.Core.Display;

public interface IDisplayChangeDetector : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// OS からディスプレイ構成に変化があったと通知されたときに発火するイベント。
    /// </summary>
    event EventHandler<DisplaySnapshotChangedEventArgs> DisplayChanged;

    /// <summary>
    /// 監視を開始し、開始直後に最新のスナップショットを必ず 1 回発行する。
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);
}