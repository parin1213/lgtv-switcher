namespace LGTVSwitcher.DisplayDetection;

public interface IMonitorEnumerator
{
    IReadOnlyList<MonitorSnapshot> EnumerateCurrentMonitors();
}
