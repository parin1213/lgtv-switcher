using System.Reflection;
using LGTVSwitcher.Core.Display;
using LGTVSwitcher.DisplayDetection.Windows;
using Xunit;

namespace LGTVSwitcher.DisplayDetection.Windows.Tests;

public class Win32MonitorEnumeratorTests
{
    [Theory]
    [InlineData(5u, MonitorConnectionKind.Hdmi)]
    [InlineData(10u, MonitorConnectionKind.DisplayPort)]
    [InlineData(11u, MonitorConnectionKind.DisplayPort)]
    [InlineData(16u, MonitorConnectionKind.Usb)]
    [InlineData(6u, MonitorConnectionKind.Internal)]
    [InlineData(0x80000000u, MonitorConnectionKind.Internal)]
    [InlineData(15u, MonitorConnectionKind.Wireless)]
    [InlineData(0u, MonitorConnectionKind.Unknown)]
    public void MapVideoOutputTechnology_ReturnsExpected(uint value, MonitorConnectionKind expected)
    {
        var method = typeof(Win32MonitorEnumerator)
            .GetMethod("MapVideoOutputTechnology", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (MonitorConnectionKind)method.Invoke(null, new object[] { value })!;

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(@"\\?\DISPLAY#ABC123#4&d0a5d5&0&UID256", @"DISPLAY#ABC123#4&d0a5d5&0&UID256")]
    [InlineData(@"USB\\VID_0408&PID_3001\\5&2b258f8d&0&1", @"VID_0408&PID_3001")]
    public void ExtractVendorToken_ReturnsToken(string deviceId, string expected)
    {
        var method = typeof(Win32MonitorEnumerator)
            .GetMethod("ExtractVendorToken", BindingFlags.NonPublic | BindingFlags.Static)!;

        var token = (string)method.Invoke(null, new object[] { deviceId })!;

        Assert.Equal(expected, token);
    }

    [Theory]
    [InlineData("ABC123_4&d0a5d5&0&UID256", "ABC123")]
    [InlineData("USBVID_0408&PID_3001", "USBVID")]
    public void ExtractInstanceToken_ReturnsToken(string instanceName, string expected)
    {
        var method = typeof(Win32MonitorEnumerator)
            .GetMethod("ExtractInstanceToken", BindingFlags.NonPublic | BindingFlags.Static)!;

        var token = (string)method.Invoke(null, new object[] { instanceName })!;

        Assert.Equal(expected, token);
    }
}
