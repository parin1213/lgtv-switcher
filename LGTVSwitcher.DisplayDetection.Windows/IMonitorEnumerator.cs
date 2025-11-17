using LGTVSwitcher.Core.Display;

namespace LGTVSwitcher.DisplayDetection.Windows;

public interface IMonitorEnumerator
{
    IReadOnlyList<MonitorSnapshot> EnumerateCurrentMonitors();
}