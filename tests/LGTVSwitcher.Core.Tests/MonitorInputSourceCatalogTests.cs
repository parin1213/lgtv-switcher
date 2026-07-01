using LGTVSwitcher.Core.Display;

using Xunit;

namespace LGTVSwitcher.Core.Tests;

public class MonitorInputSourceCatalogTests
{
    [Theory]
    [InlineData("DisplayPort", 0x0F)]
    [InlineData("DP", 0x0F)]
    [InlineData("displayport", 0x0F)] // 大文字小文字は無視
    [InlineData("DisplayPort2", 0x10)]
    [InlineData("HDMI", 0x11)]
    [InlineData("HDMI2", 0x12)]
    [InlineData("USB-C", 0x19)]
    [InlineData("Thunderbolt", 0x19)]
    [InlineData("  HDMI  ", 0x11)] // 前後空白は無視
    public void TryResolve_KnownName_ReturnsCode(string name, int expected)
    {
        var ok = MonitorInputSourceCatalog.TryResolve(name, out var code);
        Assert.True(ok);
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData("0x1B", 0x1B)]
    [InlineData("0X1b", 0x1B)]
    [InlineData("27", 27)]
    public void TryResolve_RawValue_ReturnsCode(string value, int expected)
    {
        var ok = MonitorInputSourceCatalog.TryResolve(value, out var code);
        Assert.True(ok);
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("NotAnInput")]
    public void TryResolve_InvalidValue_ReturnsFalse(string? value)
    {
        var ok = MonitorInputSourceCatalog.TryResolve(value, out var code);
        Assert.False(ok);
        Assert.Equal(0, code);
    }
}
