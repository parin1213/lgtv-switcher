using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace LGTVSwitcher.LgWebOsClient;

public sealed class SsdpLgTvDiscoveryService : ILgTvDiscoveryService
{
    private static readonly string[] SearchTargets =
    [
        "urn:lge-com:service:webos-second-screen:1",
        "urn:schemas-upnp-org:device:Basic:1",
        "urn:schemas-upnp-org:device:MediaRenderer:1",
        "urn:schemas-upnp-org:service:dial:1",
        "ssdp:all",
    ];

    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromMilliseconds(2000);
    private const string MulticastAddress = "239.255.255.250";
    private const int MulticastPort = 1900;

    private readonly ILogger<SsdpLgTvDiscoveryService> _logger;

    public SsdpLgTvDiscoveryService(ILogger<SsdpLgTvDiscoveryService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<LgTvDiscoveryResult>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var interfaces = GetCandidateInterfaces();
        if (interfaces.Count == 0)
        {
            _logger.LogWarning("SSDP discovery skipped: no multicast-capable IPv4 interfaces found.");
            return Array.Empty<LgTvDiscoveryResult>();
        }

        var allResponses = new List<ParsedResponse>();
        var dedupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sequence = 0;

        foreach (var nic in interfaces)
        {
            var (responses, nextSequence) = await DiscoverOnInterfaceAsync(nic, dedupKeys, sequence, cancellationToken).ConfigureAwait(false);
            sequence = nextSequence;
            allResponses.AddRange(responses);
        }

        var aggregated = AggregateByAddress(allResponses);
        _logger.LogInformation("SSDP final result: {Count} TV candidates after aggregation.", aggregated.Count);
        return aggregated;
    }

    private async Task<(List<ParsedResponse> Responses, int NextSequence)> DiscoverOnInterfaceAsync(
        LocalInterface nic,
        HashSet<string> dedupKeys,
        int startSequence,
        CancellationToken cancellationToken)
    {
        var collected = new List<ParsedResponse>();
        var sequence = startSequence;

        try
        {
            using var udp = new UdpClient(new IPEndPoint(nic.Address, 0))
            {
                EnableBroadcast = true
            };
            udp.MulticastLoopback = false;
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, nic.Address.GetAddressBytes());

            var localEndpoint = (IPEndPoint)udp.Client.LocalEndPoint!;
            _logger.LogInformation("SSDP discover start on {LocalIp}:{LocalPort} ({NicName})", localEndpoint.Address, localEndpoint.Port, nic.Name);

            foreach (var st in SearchTargets)
            {
                var payload = BuildMSearch(st);
                await udp.SendAsync(payload, payload.Length, MulticastAddress, MulticastPort).ConfigureAwait(false);
            }

            var deadline = DateTime.UtcNow + DiscoveryTimeout;
            var recvCount = 0;
            var accepted = 0;
            var rejected = 0;

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
            }

            UdpReceiveResult response;
            try
            {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(remaining);
                    response = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    _logger.LogDebug(ex, "SSDP receive failed on {LocalIp}", nic.Address);
                    break;
                }

            recvCount++;
            var head = GetFirstLine(response.Buffer);
            _logger.LogDebug("SSDP recv from {Addr}:{Port} -> {LocalIp}:{LocalPort} bytes={Len} head={Head}",
                response.RemoteEndPoint.Address, response.RemoteEndPoint.Port, nic.Address, localEndpoint.Port, response.Buffer.Length, head);

            if (!TryParseResponse(response, out var parsed, out var parseRejectionReason) || parsed is null)
            {
                rejected++;
                _logger.LogDebug("SSDP rejected from {Addr}:{Port} on {LocalIp} reason={Reason}",
                    response.RemoteEndPoint.Address, response.RemoteEndPoint.Port, nic.Address, parseRejectionReason);
                continue;
            }

            _logger.LogDebug("SSDP parsed ST={St} USN={Usn} LOCATION={Location} SERVER={Server}", parsed.St, parsed.Usn, parsed.Location, parsed.Server);

            if (!LooksLikeLgTv(parsed, out var reason))
            {
                rejected++;
                _logger.LogDebug("SSDP rejected from {Addr}:{Port} on {LocalIp} reason={Reason} ST={St} USN={Usn} LOCATION={Location} SERVER={Server}",
                    response.RemoteEndPoint.Address, response.RemoteEndPoint.Port, nic.Address, reason, parsed.St, parsed.Usn, parsed.Location, parsed.Server);
                continue;
            }

                var dedupKey = BuildDedupKey(parsed);
                if (dedupKeys.Add(dedupKey))
                {
                    accepted++;
                    collected.Add(parsed with { Sequence = sequence++ });
                    _logger.LogDebug("Discovered LG TV candidate: USN={Usn}, IP={Ip}, ST={St}, LOCATION={Location}", parsed.Usn, parsed.Address, parsed.St, parsed.Location);
                }
            }

            _logger.LogInformation("SSDP summary on {LocalIp}: sent={Sent} recv={Recv} accepted={Accepted} rejected={Rejected}",
                nic.Address, SearchTargets.Length, recvCount, accepted, rejected);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            _logger.LogWarning(ex, "SSDP discovery failed on {LocalIp}", nic.Address);
        }

        return (collected, sequence);
    }

    private static IReadOnlyList<LgTvDiscoveryResult> AggregateByAddress(IEnumerable<ParsedResponse> responses)
    {
        var results = new List<LgTvDiscoveryResult>();
        foreach (var group in responses.GroupBy(r => r.Address))
        {
            var best = group
                .OrderByDescending(r => GetPriority(r.St))
                .ThenBy(r => r.Sequence)
                .First();

            results.Add(new LgTvDiscoveryResult(group.Key, best.Location, best.Usn, best.Server, best.St));
        }

        return results;
    }

    private static int GetPriority(string? st)
    {
        return st switch
        {
            { } when st.Equals("urn:lge-com:service:webos-second-screen:1", StringComparison.OrdinalIgnoreCase) => 2,
            { } when st.Equals("upnp:rootdevice", StringComparison.OrdinalIgnoreCase) => 1,
            _ => 0
        };
    }

    private static byte[] BuildMSearch(string st)
    {
        var lines = new[]
        {
            "M-SEARCH * HTTP/1.1",
            $"HOST: {MulticastAddress}:{MulticastPort}",
            "MAN: \"ssdp:discover\"",
            "MX: 1",
            $"ST: {st}",
            "USER-AGENT: LGTVSwitcher/1.0",
            string.Empty,
            string.Empty
        };

        return Encoding.UTF8.GetBytes(string.Join("\r\n", lines));
    }

    private static bool TryParseResponse(UdpReceiveResult response, out ParsedResponse? parsed, out string rejectionReason)
    {
        parsed = null;
        rejectionReason = string.Empty;
        var text = Encoding.UTF8.GetString(response.Buffer);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            rejectionReason = "Empty response text";
            return false;
        }

        var firstLine = lines[0];
        if (!firstLine.StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase))
        {
            rejectionReason = $"Unexpected status line '{firstLine}'";
            return false;
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

        var address = response.RemoteEndPoint.Address.ToString();
        parsed = new ParsedResponse(address, st, usn, location, server);
        if (string.IsNullOrWhiteSpace(st) && string.IsNullOrWhiteSpace(usn) && string.IsNullOrWhiteSpace(location))
        {
            rejectionReason = "Missing SSDP headers ST/USN/LOCATION";
            return false;
        }

        rejectionReason = string.Empty;
        return true;
    }

    private static bool LooksLikeLgTv(ParsedResponse parsed, out string reason)
    {
        reason = string.Empty;

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

        reason = "No LG webOS markers in SERVER/ST/USN";
        return false;
    }

    private static string BuildDedupKey(ParsedResponse parsed)
    {
        var usnPart = string.IsNullOrWhiteSpace(parsed.Usn) ? string.Empty : parsed.Usn;
        return $"{parsed.Address}|{usnPart}";
    }

    private static string GetFirstLine(byte[] buffer)
    {
        var text = Encoding.UTF8.GetString(buffer);
        var newlineIndex = text.IndexOf("\r\n", StringComparison.Ordinal);
        return newlineIndex >= 0 ? text[..newlineIndex] : text;
    }

    private static List<LocalInterface> GetCandidateInterfaces()
    {
        var interfaces = new List<LocalInterface>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                !nic.SupportsMulticast)
            {
                continue;
            }

            var properties = nic.GetIPProperties();
            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var address = unicast.Address;
                if (IsLinkLocal(address))
                {
                    continue;
                }

                interfaces.Add(new LocalInterface(nic.Name, address));
            }
        }

        return interfaces;
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length >= 2 && bytes[0] == 169 && bytes[1] == 254;
    }

    private sealed record ParsedResponse(
        string Address,
        string? St,
        string? Usn,
        string? Location,
        string? Server,
        int Sequence = 0);

    private sealed record LocalInterface(string Name, IPAddress Address);
}
