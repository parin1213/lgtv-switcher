using System.IO;

using LGTVSwitcher.Daemon.Windows;

using Xunit;

namespace LGTVSwitcher.DisplayDetection.Windows.Tests;

public sealed class WindowsPathProviderTests
{
    [Fact]
    public void GetStateFilePath_UsesStateJson()
    {
        var provider = new WindowsPathProvider();

        var path = provider.GetStateFilePath();

        Assert.Equal("state.json", Path.GetFileName(path));
        Assert.Contains("LGTVSwitcher", path);
    }

    [Fact]
    public void GetLogsDirectory_UsesLogsFolder()
    {
        var provider = new WindowsPathProvider();

        var path = provider.GetLogsDirectory();

        Assert.Equal("Logs", Path.GetFileName(path));
        Assert.Contains("LGTVSwitcher", path);
    }
}
