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
    private readonly ISsdpResponseParser _parser;

    public SsdpLgTvDiscoveryService(
        ILogger<SsdpLgTvDiscoveryService> logger,
        ISsdpResponseParser parser)
    {
        _logger = logger;
        _parser = parser;
    }

    public async Task<IReadOnlyList<LgTvDiscoveryResult>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var interfaces = GetCandidateInterfaces();
        if (interfaces.Count == 0)
        {
            _logger.LogWarning("SSDP discovery skipped: no multicast-capable IPv4 interfaces found.");
            return Array.Empty<LgTvDiscoveryResult>();
        }

        var allResponses = new List<DiscoveredResponse>();
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

    private async Task<(List<DiscoveredResponse> Responses, int NextSequence)> DiscoverOnInterfaceAsync(
        LocalInterface nic,
        HashSet<string> dedupKeys,
        int startSequence,
        CancellationToken cancellationToken)
    {
        var collected = new List<DiscoveredResponse>();
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

                var responseText = Encoding.UTF8.GetString(response.Buffer);
                var parsed = _parser.Parse(responseText, response.RemoteEndPoint);
                if (parsed is null)
                {
                    rejected++;
                    _logger.LogDebug("SSDP rejected from {Addr}:{Port} on {LocalIp} head={Head}",
                        response.RemoteEndPoint.Address, response.RemoteEndPoint.Port, nic.Address, head);
                    continue;
                }

                _logger.LogDebug("SSDP parsed ST={St} USN={Usn} LOCATION={Location} SERVER={Server}", parsed.St, parsed.Usn, parsed.Location, parsed.Server);

                var dedupKey = BuildDedupKey(parsed);
                if (dedupKeys.Add(dedupKey))
                {
                    accepted++;
                    collected.Add(new DiscoveredResponse(parsed, sequence++));
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

    private IReadOnlyList<LgTvDiscoveryResult> AggregateByAddress(IEnumerable<DiscoveredResponse> responses)
    {
        var results = new List<LgTvDiscoveryResult>();
        foreach (var group in responses.GroupBy(r => r.Result.Address))
        {
            var best = group
                .OrderByDescending(r => _parser.GetPriority(r.Result.St))
                .ThenBy(r => r.Sequence)
                .First();

            results.Add(best.Result);
        }

        return results;
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

    private static string BuildDedupKey(LgTvDiscoveryResult parsed)
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

    private sealed record DiscoveredResponse(LgTvDiscoveryResult Result, int Sequence);

    private sealed record LocalInterface(string Name, IPAddress Address);
}
