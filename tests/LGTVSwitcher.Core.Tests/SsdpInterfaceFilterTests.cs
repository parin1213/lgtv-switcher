using LGTVSwitcher.Core.LgWebOs;

using Xunit;

namespace LGTVSwitcher.Core.Tests;

public class SsdpInterfaceFilterTests
{
    [Theory]
    [InlineData("vEthernet (WSL (Hyper-V firewall))", "Hyper-V Virtual Ethernet Adapter")]
    [InlineData("Tailscale", "Tailscale Tunnel")]
    [InlineData("VMware Network Adapter VMnet1", "VMware Virtual Ethernet Adapter")]
    [InlineData("Loopback Pseudo-Interface 1", "Software Loopback Interface")]
    [InlineData("イーサネット 2", "TAP-Windows Adapter V9")]
    public void IsVirtual_VirtualAdapters_ReturnsTrue(string name, string description)
    {
        Assert.True(SsdpInterfaceFilter.IsVirtual(name, description));
    }

    [Theory]
    [InlineData("イーサネット", "Realtek PCIe GbE Family Controller")]
    [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6E AX211 160MHz")]
    [InlineData("Ethernet", "Aquantia AQtion 10Gbit Network Adapter")]
    public void IsVirtual_PhysicalAdapters_ReturnsFalse(string name, string description)
    {
        Assert.False(SsdpInterfaceFilter.IsVirtual(name, description));
    }

    [Fact]
    public void IsVirtual_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(SsdpInterfaceFilter.IsVirtual(null, null));
        Assert.False(SsdpInterfaceFilter.IsVirtual("", "   "));
    }
}
