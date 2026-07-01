using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading.Channels;

using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.Core.LgWebOs;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LGTVSwitcher.Core.Workers;

public sealed class DisplaySyncWorker : BackgroundService
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(800);

    private readonly IDisplaySnapshotProvider _snapshotProvider;
    private readonly ILgTvController _lgTvController;
    private readonly IPreferredInputSourceProbe _inputSourceProbe;
    private readonly ILogger<DisplaySyncWorker> _logger;
    private readonly LgTvSwitcherOptions _options;
    private readonly string[] _allowedCurrentInputIds;
    private readonly TimeSpan? _syncInterval;
    private volatile DisplaySnapshot? _latestSnapshot;

    // TV 到達不可を Warning で通知したか。スリープ/オフ中に毎サイクル警告を出さないための遷移フラグ。
    private bool _tvUnavailableLogged;

    public DisplaySyncWorker(
        IDisplaySnapshotProvider snapshotProvider,
        ILgTvController lgTvController,
        IPreferredInputSourceProbe inputSourceProbe,
        IOptions<LgTvSwitcherOptions> options,
        ILogger<DisplaySyncWorker> logger)
    {
        _snapshotProvider = snapshotProvider;
        _lgTvController = lgTvController;
        _inputSourceProbe = inputSourceProbe;
        _logger = logger;
        _options = options.Value;
        _allowedCurrentInputIds = _options.AllowedCurrentInputIds?
            .Select(id => id?.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .OfType<string>()
            .ToArray()
            ?? Array.Empty<string>();
        _syncInterval = _options.SyncIntervalSeconds > 0
            ? TimeSpan.FromSeconds(_options.SyncIntervalSeconds)
            : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = Channel.CreateUnbounded<DisplaySnapshot>(new UnboundedChannelOptions { SingleReader = true });

        using var sub = _snapshotProvider.Notifications.Subscribe(snapshot =>
        {
            _latestSnapshot = snapshot;
            channel.Writer.TryWrite(snapshot);
        });

        await _snapshotProvider.StartAsync(stoppingToken).ConfigureAwait(false);

        var periodicTask = _syncInterval.HasValue
            ? RunPeriodicSyncAsync(stoppingToken)
            : Task.CompletedTask;

        await RunDebounceLoopAsync(channel.Reader, stoppingToken).ConfigureAwait(false);
        channel.Writer.TryComplete();

        await periodicTask.ConfigureAwait(false);
    }

    private async Task RunDebounceLoopAsync(ChannelReader<DisplaySnapshot> reader, CancellationToken ct)
    {
        var comparer = new SnapshotEqualityComparer(GetTargetInput);
        DisplaySnapshot? lastProcessed = null;

        while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            DisplaySnapshot? latest = null;
            using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            windowCts.CancelAfter(DebounceInterval);

            try
            {
                await foreach (var s in reader.ReadAllAsync(windowCts.Token).ConfigureAwait(false))
                    latest = s;
            }
            catch (OperationCanceledException) when (windowCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // 800ms window elapsed — process latest
            }

            if (latest is null || ct.IsCancellationRequested) continue;
            if (!IsEligibleSnapshot(latest)) continue;
            if (comparer.Equals(latest, lastProcessed)) continue;

            lastProcessed = latest;
            await TrySyncAsync(latest, ct).ConfigureAwait(false);
        }
    }

    private async Task RunPeriodicSyncAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_syncInterval!.Value);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            var snapshot = _latestSnapshot;
            if (snapshot is null || !IsEligibleSnapshot(snapshot)) continue;

            var refreshed = new DisplaySnapshot(
                DateTimeOffset.UtcNow,
                snapshot.Monitors,
                snapshot.PreferredMonitor,
                snapshot.PreferredMonitorOnline,
                snapshot.PreferredMonitorEdidKey);

            await TrySyncAsync(refreshed, ct).ConfigureAwait(false);
        }
    }

    private async Task TrySyncAsync(DisplaySnapshot snapshot, CancellationToken ct)
    {
        try
        {
            await SyncLgTvAsync(snapshot, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (IsTvUnavailable(ex))
        {
            LogTvUnavailable(ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LG TV sync failed.");
        }
    }

    // TV 到達不可（ネットワーク断・未検出/オフライン）は正常運用でも起きる。
    // 遷移時に一度だけ Warning を出し、以降は Debug に留めてログ肥大を防ぐ。
    private void LogTvUnavailable(Exception ex)
    {
        if (!_tvUnavailableLogged)
        {
            _logger.LogWarning("LG TV is unreachable (asleep/offline?); will keep retrying quietly: {Message}", ex.Message);
            _tvUnavailableLogged = true;
        }
        else
        {
            _logger.LogDebug("LG TV still unreachable: {Message}", ex.Message);
        }

        _logger.LogDebug(ex, "LG TV unavailable exception details.");
    }

    private void MarkTvReachable()
    {
        if (_tvUnavailableLogged)
        {
            _logger.LogInformation("LG TV is reachable again.");
            _tvUnavailableLogged = false;
        }
    }

    private async Task SyncLgTvAsync(DisplaySnapshot snapshot, CancellationToken cancellationToken)
    {
        var age = DateTimeOffset.UtcNow - snapshot.Timestamp;
        if (age > TimeSpan.FromSeconds(5))
        {
            _logger.LogDebug("Stale snapshot ({Age}); skipping LG TV sync.", age);
            return;
        }

        // 優先モニタが「今このPCの入力を映しているか」を DDC/CI で都度確認する。
        // マルチ入力モニタは表示を別PC(Mac)へ移してもリンクを維持するため、列挙有無だけでは判別できない。
        var showing = _inputSourceProbe.Probe();
        var effectiveOnline = showing switch
        {
            PreferredInputSource.ThisPc => true,
            PreferredInputSource.OtherSource => false,
            _ => snapshot.PreferredMonitorOnline,
        };

        var targetInput = effectiveOnline ? _options.TargetInputId : _options.FallbackInputId;
        if (string.IsNullOrWhiteSpace(targetInput))
        {
            _logger.LogDebug(
                "No input mapping configured for preferred monitor state {State} (input source={Source}); skipping.",
                effectiveOnline,
                showing);
            return;
        }

        string? currentInput = null;
        try
        {
            currentInput = await _lgTvController.GetCurrentInputAsync(cancellationToken).ConfigureAwait(false);
            MarkTvReachable();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsTvUnavailable(ex))
        {
            // TV がオフライン/未検出のときは切替も失敗するため、静かにスキップする。
            LogTvUnavailable(ex);
            return;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (_allowedCurrentInputIds.Length > 0)
            {
                _logger.LogWarning("Failed to query LG TV input; skipping switch (allowed list configured): {Message}", ex.Message);
                return;
            }

            _logger.LogWarning("Failed to query LG TV input; proceeding with switch: {Message}", ex.Message);
        }

        if (!IsCurrentInputAllowed(currentInput))
        {
            _logger.LogDebug(
                "LG TV current input {Input} is not in allowed list; skipping switch.",
                string.IsNullOrWhiteSpace(currentInput) ? "(unknown)" : currentInput);
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentInput) &&
            string.Equals(currentInput, targetInput, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("LG TV already set to {Input}; no switch required.", targetInput);
            return;
        }

        _logger.LogInformation("Switching LG TV input to {Input} (preferred monitor online = {State}, input source = {Source})", targetInput, effectiveOnline, showing);
        await _lgTvController.SwitchInputAsync(targetInput, cancellationToken).ConfigureAwait(false);
    }

    private string? GetTargetInput(DisplaySnapshot snapshot)
        => snapshot.PreferredMonitorOnline ? _options.TargetInputId : _options.FallbackInputId;

    private bool IsCurrentInputAllowed(string? currentInput)
    {
        if (_allowedCurrentInputIds.Length == 0) return true;
        if (string.IsNullOrWhiteSpace(currentInput)) return false;

        foreach (var allowed in _allowedCurrentInputIds)
        {
            if (string.Equals(allowed, currentInput, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsEligibleSnapshot(DisplaySnapshot snapshot)
        => !string.IsNullOrWhiteSpace(snapshot.PreferredMonitorEdidKey) &&
           (snapshot.PreferredMonitor is null || snapshot.PreferredMonitor.ConnectionKind != MonitorConnectionKind.Unknown);

    // TV 到達不可とみなす例外（ネットワーク断、または未検出/登録失敗＝オフライン）。
    private static bool IsTvUnavailable(Exception ex)
    {
        if (IsNetworkException(ex))
        {
            return true;
        }

        if (ex is AggregateException aggregate)
        {
            return aggregate.InnerExceptions.Any(IsTvUnavailable);
        }

        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is LgTvRegistrationException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNetworkException(Exception ex)
    {
        if (ex is AggregateException aggregateException)
        {
            return aggregateException.InnerExceptions.Any(IsNetworkException);
        }

        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is WebSocketException or HttpRequestException or SocketException or IOException)
            {
                return true;
            }
        }

        return false;
    }
}
