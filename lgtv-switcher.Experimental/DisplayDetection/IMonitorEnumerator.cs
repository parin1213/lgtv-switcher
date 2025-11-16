namespace LGTVSwitcher.Experimental.DisplayDetection;

internal interface IMonitorEnumerator
{
    IReadOnlyList<MonitorSnapshot> EnumerateCurrentMonitors();
}
