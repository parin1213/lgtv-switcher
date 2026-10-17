using System.Text;

using LGTVSwitcher.Core.LgTv;

using Microsoft.Extensions.Configuration;

using Xunit;

namespace LGTVSwitcher.Core.Tests;

public class LgTvSwitcherOptionsBindingTests
{
    private static LgTvSwitcherOptions Bind(string json)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();
        var options = new LgTvSwitcherOptions();
        configuration.GetSection("LgTvSwitcher").Bind(options);
        return options;
    }

    [Fact]
    public void ThisPcInputSources_Configured_ReplacesDefault()
    {
        // Mac は USB-C だけを「この PC」とする。既定の DisplayPort が混ざると Windows 表示中も Mac 表示と誤判定する。
        var options = Bind("""{ "LgTvSwitcher": { "PreferredMonitorThisPcInputSources": [ "USB-C" ] } }""");
        Assert.Equal(["USB-C"], options.ResolveThisPcInputSources());
    }

    [Fact]
    public void ThisPcInputSources_NotConfigured_DefaultsToDisplayPort()
    {
        var options = Bind("""{ "LgTvSwitcher": { } }""");
        Assert.Equal(["DisplayPort"], options.ResolveThisPcInputSources());
    }

    [Fact]
    public void ThisPcInputSources_EmptyArray_DisablesDdcJudgement()
    {
        var options = Bind("""{ "LgTvSwitcher": { "PreferredMonitorThisPcInputSources": [] } }""");
        Assert.Empty(options.ResolveThisPcInputSources());
    }
}
