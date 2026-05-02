#nullable disable
using LGTVSwitcher.Core.LgWebOs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LGTVSwitcher.Core.Tests;

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
    public void ParseCurrentInput_InvalidJson_ReturnsNull()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());

        var input = parser.ParseCurrentInput("not-json");

        Assert.Null(input);
    }

    [Fact]
    public void ParseCurrentInput_EmptyPayload_ReturnsNull()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());

        var input = parser.ParseCurrentInput(" ");

        Assert.Null(input);
    }

    [Fact]
    public void ParseCurrentInput_MissingAppId_ReturnsNull()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());
        var payload = """{"returnValue":true}""";

        var input = parser.ParseCurrentInput(payload);

        Assert.Null(input);
    }

    [Fact]
    public void ParseCurrentInput_NestedForegroundAppInfo_ReturnsMappedInput()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());
        var payload = """{"returnValue":true,"foregroundAppInfo":{"appId":"com.webos.app.hdmi10"}}""";

        var input = parser.ParseCurrentInput(payload);

        Assert.Equal("HDMI_10", input);
    }

    [Fact]
    public void ParseCurrentInput_NonHdmiApp_ReturnsRawAppId()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());
        var payload = """{"returnValue":true,"appId":"com.webos.app.livetv"}""";

        var input = parser.ParseCurrentInput(payload);

        Assert.Equal("com.webos.app.livetv", input);
    }

    [Fact]
    public void ParseRegistrationResponse_RegisteredWithClientKey()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());
        var json = """{"type":"registered","payload":{"client-key":"abc123"}}""";

        var result = parser.ParseRegistrationResponse(json);

        Assert.Equal(LgTvRegistrationStatus.Completed, result.Status);
        Assert.Equal("abc123", result.ClientKey);
    }

    [Fact]
    public void ParseRegistrationResponse_ErrorInProgress_Pending()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());
        var json = """{"type":"error","error":"register already in progress"}""";

        var result = parser.ParseRegistrationResponse(json);

        Assert.Equal(LgTvRegistrationStatus.Pending, result.Status);
    }

    [Fact]
    public void ParseRegistrationResponse_ReturnValueFalse_Pending()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());
        var json = """{"type":"response","payload":{"returnValue":false}}""";

        var result = parser.ParseRegistrationResponse(json);

        Assert.Equal(LgTvRegistrationStatus.Pending, result.Status);
    }

    [Fact]
    public void ParseRegistrationResponse_ResponseTrueWithClientKey_Completed()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());
        var json = """{"type":"response","payload":{"returnValue":true,"client-key":"abc123"}}""";

        var result = parser.ParseRegistrationResponse(json);

        Assert.Equal(LgTvRegistrationStatus.Completed, result.Status);
        Assert.Equal("abc123", result.ClientKey);
    }

    [Fact]
    public void ParseRegistrationResponse_ErrorWithoutMessage_Pending()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());
        var json = """{"type":"error"}""";

        var result = parser.ParseRegistrationResponse(json);

        Assert.Equal(LgTvRegistrationStatus.Pending, result.Status);
    }

    [Fact]
    public void ParseRegistrationResponse_Error_Throws()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());
        var json = """{"type":"error","error":"403 access denied"}""";

        Assert.Throws<LgTvRegistrationException>(() => parser.ParseRegistrationResponse(json));
    }

    [Fact]
    public void ParseResponse_InvalidJson_ThrowsCommandException()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());

        Assert.Throws<LgTvCommandException>(() => parser.ParseResponse("not-json", "test-uri"));
    }

    [Fact]
    public void ParseResponse_EmptyJson_ThrowsCommandException()
    {
        var parser = new LgTvResponseParser(new NullLogger<LgTvResponseParser>());

        Assert.Throws<LgTvCommandException>(() => parser.ParseResponse("", "test-uri"));
    }

    [Fact]
    public void ExceptionConstructors_PreserveMessageAndInnerException()
    {
        var inner = new InvalidOperationException("inner");

        var registration = new LgTvRegistrationException("registration", inner);
        var command = new LgTvCommandException("command", inner);

        Assert.Equal("registration", registration.Message);
        Assert.Same(inner, registration.InnerException);
        Assert.Equal("command", command.Message);
        Assert.Same(inner, command.InnerException);
    }
}
