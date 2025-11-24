#nullable disable
using LGTVSwitcher.LgWebOsClient;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LGTVSwitcher.LgWebOsClient.Tests;

public class LgTvResponseParserTests
{
    [Fact]
    public void ParseCurrentInput_HdmiAppId_ReturnsMappedInput()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());
        var payload = """{"returnValue":true,"appId":"com.webos.app.hdmi3"}""";

        var input = parser.ParseCurrentInput(payload);

        Assert.Equal("HDMI_3", input);
    }

    [Fact]
    public void ParseCurrentInput_ReturnValueFalse_ThrowsCommandException()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());
        var payload = """{"returnValue":false}""";

        Assert.Throws<LgTvCommandException>(() => parser.ParseCurrentInput(payload));
    }

    [Fact]
    public void ParseRegistrationResponse_RegisteredWithClientKey()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());
        var json = """{"type":"registered","payload":{"client-key":"abc123"}}""";

        var result = parser.ParseRegistrationResponse(json);

        Assert.Equal(LgTvRegistrationStatus.Registered, result.Status);
        Assert.Equal("abc123", result.ClientKey);
    }

    [Fact]
    public void ParseRegistrationResponse_ErrorInProgress_RequiresPrompt()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());
        var json = """{"type":"error","error":"register already in progress"}""";

        var result = parser.ParseRegistrationResponse(json);

        Assert.Equal(LgTvRegistrationStatus.RequiresPrompt, result.Status);
    }
}
