using System;
using System.Net;

using Xunit;

namespace LGTVSwitcher.LgWebOsClient.Tests;

public sealed class SsdpResponseParserTests
{
    [Fact]
    public void Parse_ReturnsResult_ForLgResponse()
    {
        var parser = new SsdpResponseParser();
        var remote = new IPEndPoint(IPAddress.Parse("192.168.0.20"), 1900);
        var response = """
HTTP/1.1 200 OK
CACHE-CONTROL: max-age=1800
DATE: Tue, 10 Dec 2024 12:00:00 GMT
EXT:
LOCATION: http://192.168.0.20:3001/ssdp/device-desc.xml
SERVER: Linux/3.14.0 UPnP/1.0 LGE WebOS
ST: urn:lge-com:service:webos-second-screen:1
USN: uuid:abcd::urn:lge-com:service:webos-second-screen:1

""";

        var result = parser.Parse(response, remote);

        Assert.NotNull(result);
        Assert.Equal("192.168.0.20", result!.Address);
        Assert.Equal("http://192.168.0.20:3001/ssdp/device-desc.xml", result.Location);
        Assert.Equal("urn:lge-com:service:webos-second-screen:1", result.St);
        Assert.Equal("uuid:abcd::urn:lge-com:service:webos-second-screen:1", result.Usn);
        Assert.Contains("webos", result.Server, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ReturnsNull_ForNonLgResponse()
    {
        var parser = new SsdpResponseParser();
        var remote = new IPEndPoint(IPAddress.Parse("192.168.0.30"), 1900);
        var response = """
HTTP/1.1 200 OK
LOCATION: http://192.168.0.30/desc.xml
SERVER: Microsoft-Windows/10.0 UPnP/1.0
ST: urn:schemas-upnp-org:device:Basic:1
USN: uuid:foo::urn:schemas-upnp-org:device:Basic:1

""";

        var result = parser.Parse(response, remote);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_ReturnsNull_ForInvalidStatusLine()
    {
        var parser = new SsdpResponseParser();
        var remote = new IPEndPoint(IPAddress.Loopback, 1900);
        var response = """
NOTIFY * HTTP/1.1
LOCATION: http://127.0.0.1/desc.xml

""";

        var result = parser.Parse(response, remote);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("urn:lge-com:service:webos-second-screen:1", 2)]
    [InlineData("upnp:rootdevice", 1)]
    [InlineData("urn:schemas-upnp-org:device:Basic:1", 0)]
    public void GetPriority_ReturnsExpectedValues(string st, int expected)
    {
        var parser = new SsdpResponseParser();

        var priority = parser.GetPriority(st);

        Assert.Equal(expected, priority);
    }
}
