namespace LGTVSwitcher.Daemon.Windows;

public sealed class WindowsPathProvider
{
    private const string RootFolderName = "LGTVSwitcher";
    private const string StateFileName = "state.json";
    private const string LogsFolderName = "Logs";

    public string GetStateFilePath()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(basePath, RootFolderName, StateFileName);
    }

    public string GetLogsDirectory()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(basePath, RootFolderName, LogsFolderName);
    }
}
