namespace LGTVSwitcher.Core.Display;

/// <summary>
/// ディスプレイスナップショットを Rx 経由で配信するストリーム。
/// </summary>
public interface IDisplaySnapshotStream : IObservable<DisplaySnapshot>
{
    /// <summary>
    /// 新しいスナップショットをストリームに公開する。
    /// </summary>
    void Publish(DisplaySnapshot snapshot);
}
