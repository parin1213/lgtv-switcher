using LGTVSwitcher.Core.Display;

using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace LgtvSwitcher.MacOS;

// Phase 3: implement via CGDisplayRegisterReconfigurationCallback
public sealed class MacDisplayWatcher : IDisplaySnapshotProvider, IAsyncDisposable
{
    private readonly Subject<DisplaySnapshot> _subject = new();

    public IObservable<DisplaySnapshot> Notifications => _subject.AsObservable();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException("macOS display watcher is not yet implemented.");
    }

    public ValueTask DisposeAsync()
    {
        _subject.Dispose();
        return ValueTask.CompletedTask;
    }
}
