using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using LGTVSwitcher.Core.LgTv;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LGTVSwitcher.Core.Coordination;

/// <summary>
/// もう一方の PC で動くデーモンと状態を交換し、どちらが TV を切り替えるかを決める。
/// </summary>
/// <remarks>
/// 各デーモンは <see cref="LgTvSwitcherOptions.PeerPort"/>（TCP）で自分の状態を 1 行の JSON で返し、
/// 相手には <see cref="LgTvSwitcherOptions.PeerHeartbeatSeconds"/> ごとに問い合わせる。
/// 相手の IP は設定に書かず、同じポート（UDP）へのブロードキャストと TCP の問い合わせの送信元から知る。
/// Mac は Dell の表示先によって有線と Wi-Fi で IP が変わり、どちらも DHCP なので、固定値では追従できない。
/// 片方向しか届かなくても（ブロードキャストが一方にしか届かない等）、届いた側が問い合わせれば選出は成立する。
/// <see cref="LgTvSwitcherOptions.EnablePeerCoordination"/> が false なら通信せず、常にリーダーとして振る舞う。
/// </remarks>
public sealed class PeerCoordinator : BackgroundService, ILeadershipState
{
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(2);
    private const string AnnouncementApp = "lgtv-switcher";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly LgTvSwitcherOptions _options;
    private readonly ILeadershipEligibility _eligibility;
    private readonly ILogger<PeerCoordinator> _logger;
    private readonly Lock _gate = new();
    private readonly string _nodeId = Environment.MachineName;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    private PeerRole _role = PeerRole.Starting;
    private DateTimeOffset? _leaderSince;
    private PeerStatus? _peer;
    private DateTimeOffset _peerSeenAt;
    private IPAddress? _peerAddress;

    public PeerCoordinator(
        IOptions<LgTvSwitcherOptions> options,
        ILeadershipEligibility eligibility,
        ILogger<PeerCoordinator> logger)
    {
        _options = options.Value;
        _eligibility = eligibility;
        _logger = logger;
    }

    private bool Enabled => _options.EnablePeerCoordination;

    public event EventHandler? LeadershipAcquired;

    public bool IsLeader
    {
        get
        {
            if (!Enabled)
            {
                return true;
            }

            lock (_gate)
            {
                return _role == PeerRole.Leader;
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            return;
        }

        _logger.LogInformation(
            "Coordinating with peer daemon on port {Port} as {NodeId}.", _options.PeerPort, _nodeId);

        using var udp = CreateAnnouncementSocket();
        var listenTask = ListenAsync(stoppingToken);
        var announcementTask = udp is null ? Task.CompletedTask : ReceiveAnnouncementsAsync(udp, stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.PeerHeartbeatSeconds)));
        do
        {
            if (udp is not null)
            {
                await AnnounceAsync(udp, stoppingToken).ConfigureAwait(false);
            }

            var peer = await QueryPeerAsync(stoppingToken).ConfigureAwait(false);
            if (peer is not null)
            {
                RecordPeer(peer);
            }

            Evaluate();
        }
        while (await WaitNextAsync(timer, stoppingToken).ConfigureAwait(false));

        udp?.Dispose();
        await Task.WhenAll(listenTask, announcementTask).ConfigureAwait(false);
    }

    private UdpClient? CreateAnnouncementSocket()
    {
        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, _options.PeerPort));
            return udp;
        }
        catch (SocketException ex)
        {
            // 受けられなくても、相手のブロードキャストが届けば相手から問い合わせが来るので選出は成立する。
            _logger.LogWarning("Cannot open peer announcement port {Port}/udp: {Message}", _options.PeerPort, ex.Message);
            return null;
        }
    }

    private async Task AnnounceAsync(UdpClient udp, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new PeerAnnouncement(AnnouncementApp, _nodeId), JsonOptions));
        foreach (var target in GetBroadcastAddresses())
        {
            try
            {
                await udp.SendAsync(payload, new IPEndPoint(target, _options.PeerPort), ct).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                _logger.LogDebug("Peer announcement to {Target} failed: {Message}", target, ex.Message);
            }
        }
    }

    private async Task ReceiveAnnouncementsAsync(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udp.ReceiveAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                _logger.LogDebug("Peer announcement receive failed: {Message}", ex.Message);
                continue;
            }

            PeerAnnouncement? announcement;
            try
            {
                announcement = JsonSerializer.Deserialize<PeerAnnouncement>(received.Buffer, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (announcement is { App: AnnouncementApp } && announcement.NodeId != _nodeId)
            {
                RecordPeerAddress(received.RemoteEndPoint.Address);
            }
        }
    }

    // 各 NIC のサブネット向けブロードキャスト。255.255.255.255 は macOS では主 NIC からしか出ないため、NIC ごとに送る。
    private static IEnumerable<IPAddress> GetBroadcastAddresses()
    {
        var targets = new HashSet<IPAddress> { IPAddress.Broadcast };
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
                LgWebOs.SsdpInterfaceFilter.IsVirtual(nic.Name, nic.Description))
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                {
                    continue;
                }

                var address = unicast.Address.GetAddressBytes();
                var mask = unicast.IPv4Mask.GetAddressBytes();
                var broadcast = new byte[4];
                for (var i = 0; i < 4; i++)
                {
                    broadcast[i] = (byte)(address[i] | ~mask[i]);
                }

                targets.Add(new IPAddress(broadcast));
            }
        }

        return targets;
    }

    private void RecordPeerAddress(IPAddress address)
    {
        lock (_gate)
        {
            if (address.Equals(_peerAddress))
            {
                return;
            }

            _peerAddress = address;
        }

        _logger.LogInformation("Peer daemon found at {Address}.", address);
    }

    private static async Task<bool> WaitNextAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private PeerStatus Snapshot()
    {
        lock (_gate)
        {
            return new PeerStatus(_nodeId, _role, _startedAt, _leaderSince, _eligibility.IsEligible);
        }
    }

    private void RecordPeer(PeerStatus peer)
    {
        lock (_gate)
        {
            _peer = peer;
            _peerSeenAt = DateTimeOffset.UtcNow;
        }
    }

    private void Evaluate()
    {
        var acquired = false;
        lock (_gate)
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.PeerTimeoutSeconds));
            var peer = _peer is not null && DateTimeOffset.UtcNow - _peerSeenAt <= timeout ? _peer : null;

            // 起動直後の 1 回目の問い合わせは間に合わないことがある。相手から連絡が来るのを 2 周期待ってから
            // リーダーを名乗る。待たないと、先に動いていたリーダーと数秒間両方で TV を切り替えてしまう。
            var startupGrace = TimeSpan.FromSeconds(2 * Math.Max(1, _options.PeerHeartbeatSeconds));
            if (_role == PeerRole.Starting && peer is null && DateTimeOffset.UtcNow - _startedAt < startupGrace)
            {
                return;
            }
            var self = new PeerStatus(_nodeId, _role, _startedAt, _leaderSince, _eligibility.IsEligible);
            var next = LeaderElection.Decide(self, peer);
            if (next == _role)
            {
                return;
            }

            _logger.LogInformation(
                "Peer role changed: {From} -> {To} (self eligible = {Eligible}, peer {Peer} = {PeerRole}, peer eligible = {PeerEligible}).",
                _role,
                next,
                self.Eligible,
                peer?.NodeId ?? "(unreachable)",
                peer?.Role.ToString() ?? "-",
                peer?.Eligible.ToString() ?? "-");
            _role = next;
            _leaderSince = next == PeerRole.Leader ? DateTimeOffset.UtcNow : null;
            acquired = next == PeerRole.Leader;
        }

        if (acquired)
        {
            LeadershipAcquired?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<PeerStatus?> QueryPeerAsync(CancellationToken ct)
    {
        IPAddress? host;
        lock (_gate)
        {
            host = _peerAddress;
        }

        if (host is null)
        {
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IoTimeout);
            using var client = new TcpClient();
            await client.ConnectAsync(host, _options.PeerPort, cts.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            await WriteStatusAsync(stream, Snapshot(), cts.Token).ConfigureAwait(false);
            return await ReadStatusAsync(stream, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested)
            {
                return null;
            }

            _logger.LogDebug("Peer {Host}:{Port} unreachable: {Message}", host, _options.PeerPort, ex.Message);
            return null;
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, _options.PeerPort);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            // 待ち受けできなくても、こちらからの問い合わせだけで選出は成立する。
            _logger.LogWarning("Cannot listen on peer port {Port}: {Message}", _options.PeerPort, ex.Message);
            return;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleClientAsync(client, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(IoTimeout);
                await using var stream = client.GetStream();
                var peer = await ReadStatusAsync(stream, cts.Token).ConfigureAwait(false);
                if (peer is not null && peer.NodeId != _nodeId)
                {
                    RecordPeer(peer);
                    if (client.Client.RemoteEndPoint is IPEndPoint remote)
                    {
                        RecordPeerAddress(remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address);
                    }
                }

                await WriteStatusAsync(stream, Snapshot(), cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or JsonException)
            {
                _logger.LogDebug("Peer request failed: {Message}", ex.Message);
            }
        }
    }

    private static async Task WriteStatusAsync(Stream stream, PeerStatus status, CancellationToken ct)
    {
        var line = JsonSerializer.Serialize(status, JsonOptions) + "\n";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(line), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<PeerStatus?> ReadStatusAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(line) ? null : JsonSerializer.Deserialize<PeerStatus>(line, JsonOptions);
    }

    private sealed record PeerAnnouncement(string App, string NodeId);
}
