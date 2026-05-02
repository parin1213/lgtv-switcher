namespace LgtvSwitcher.MacOS;

public sealed class MacPathProvider
{
    private const string AppName = "LgtvSwitcher";

    public string GetStateFilePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "Application Support", AppName, "state.json");
    }

    public string GetLogsDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "Logs", AppName);
    }
}
