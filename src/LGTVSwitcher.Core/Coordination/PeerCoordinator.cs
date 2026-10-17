using System.Net;
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
/// 各デーモンは <see cref="LgTvSwitcherOptions.PeerPort"/> で自分の状態を 1 行の JSON で返し、
/// 相手には <see cref="LgTvSwitcherOptions.PeerHeartbeatSeconds"/> ごとに問い合わせる。
/// 相手からの問い合わせも生存確認として扱うため、ファイアウォール等で片方向しか通らなくても選出は成立する。
/// <see cref="LgTvSwitcherOptions.PeerHost"/> が空なら通信せず、常にリーダーとして振る舞う。
/// </remarks>
public sealed class PeerCoordinator : BackgroundService, ILeadershipState
{
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(2);
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

    public PeerCoordinator(
        IOptions<LgTvSwitcherOptions> options,
        ILeadershipEligibility eligibility,
        ILogger<PeerCoordinator> logger)
    {
        _options = options.Value;
        _eligibility = eligibility;
        _logger = logger;
    }

    private bool Enabled => !string.IsNullOrWhiteSpace(_options.PeerHost);

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
            "Coordinating with peer {Host}:{Port} as {NodeId}.", _options.PeerHost, _options.PeerPort, _nodeId);

        var listenTask = ListenAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.PeerHeartbeatSeconds)));
        do
        {
            var peer = await QueryPeerAsync(stoppingToken).ConfigureAwait(false);
            if (peer is not null)
            {
                RecordPeer(peer);
            }

            Evaluate();
        }
        while (await WaitNextAsync(timer, stoppingToken).ConfigureAwait(false));

        await listenTask.ConfigureAwait(false);
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
        }
    }

    private async Task<PeerStatus?> QueryPeerAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IoTimeout);
            using var client = new TcpClient();
            await client.ConnectAsync(_options.PeerHost!, _options.PeerPort, cts.Token).ConfigureAwait(false);
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

            _logger.LogDebug("Peer {Host}:{Port} unreachable: {Message}", _options.PeerHost, _options.PeerPort, ex.Message);
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
                if (peer is not null)
                {
                    RecordPeer(peer);
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
}
