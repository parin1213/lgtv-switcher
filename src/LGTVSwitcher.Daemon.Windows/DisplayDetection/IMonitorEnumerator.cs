using LGTVSwitcher.Core.Display;

namespace LGTVSwitcher.Daemon.Windows.DisplayDetection;

public interface IMonitorEnumerator
{
    IReadOnlyList<MonitorSnapshot> EnumerateCurrentMonitors();
}