using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace LGTVSwitcher.LgWebOsClient;

public sealed class SsdpResponseParser : ISsdpResponseParser
{
    public LgTvDiscoveryResult? Parse(string responseText, IPEndPoint remoteEndPoint)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        var lines = responseText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return null;
        }

        var firstLine = lines[0];
        if (!firstLine.StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var index = line.IndexOf(':');
            if (index <= 0)
            {
                continue;
            }

            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (!headers.ContainsKey(key))
            {
                headers[key] = value;
            }
        }

        headers.TryGetValue("ST", out var st);
        headers.TryGetValue("USN", out var usn);
        headers.TryGetValue("LOCATION", out var location);
        headers.TryGetValue("SERVER", out var server);

        if (string.IsNullOrWhiteSpace(st) && string.IsNullOrWhiteSpace(usn) && string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var parsed = new LgTvDiscoveryResult(remoteEndPoint.Address.ToString(), location, usn, server, st);
        return LooksLikeLgTv(parsed) ? parsed : null;
    }

    public int GetPriority(string? st)
    {
        return st switch
        {
            { } when st.Equals("urn:lge-com:service:webos-second-screen:1", StringComparison.OrdinalIgnoreCase) => 2,
            { } when st.Equals("upnp:rootdevice", StringComparison.OrdinalIgnoreCase) => 1,
            _ => 0
        };
    }

    private static bool LooksLikeLgTv(LgTvDiscoveryResult parsed)
    {
        var st = (parsed.St ?? string.Empty).ToLowerInvariant();
        var usn = (parsed.Usn ?? string.Empty).ToLowerInvariant();
        var server = (parsed.Server ?? string.Empty).ToLowerInvariant();

        static bool ContainsAny(string source, params string[] keywords)
            => keywords.Any(k => source.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (ContainsAny(server, "webos", "lg"))
        {
            return true;
        }

        if (ContainsAny(st, "lge", "webos", "dial", "multiscreen", "mediarenderer") ||
            ContainsAny(usn, "lge", "webos", "dial", "multiscreen", "mediarenderer"))
        {
            return true;
        }

        return false;
    }
}
