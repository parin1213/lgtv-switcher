using System.Reactive.Subjects;
using LGTVSwitcher.Core.Display;

namespace LGTVSwitcher.DisplayDetection.Windows;

/// <summary>
/// Win32 監視から発行されるスナップショットを保持する Subject ベースの実装。
/// </summary>
public sealed class DisplaySnapshotStream : IDisplaySnapshotStream, IDisposable
{
    private readonly Subject<DisplaySnapshot> _subject = new();
    private bool _disposed;

    public void Publish(DisplaySnapshot snapshot)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DisplaySnapshotStream));
        }

        _subject.OnNext(snapshot);
    }

    public IDisposable Subscribe(IObserver<DisplaySnapshot> observer)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DisplaySnapshotStream));
        }

        return _subject.Subscribe(observer);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subject.OnCompleted();
        _subject.Dispose();
        GC.SuppressFinalize(this);
    }
}
