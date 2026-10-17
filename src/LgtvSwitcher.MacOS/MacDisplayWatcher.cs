using LGTVSwitcher.Core.Coordination;
using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace LgtvSwitcher.MacOS;

/// <summary>
/// 外部ディスプレイの構成と優先モニタの入力ソースをポーリングし、変化したときにスナップショットを流す。
/// </summary>
/// <remarks>
/// モニタ側で入力を Mac ⇔ Windows に切り替えても macOS のディスプレイ構成は変わらない（USB-C リンクが残る）ため、
/// CGDisplay の再構成コールバックでは切替を拾えない。DDC の入力ソースを数秒おきに読み、その変化で同期を起こす。
/// 同期時の判定は <c>DisplaySyncWorker</c> がプローブを都度呼んで行う。ここで流す online は同期を起こすための目印。
/// </remarks>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class MacDisplayWatcher : IDisplaySnapshotProvider, ILeadershipEligibility, IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly Subject<DisplaySnapshot> _subject = new();
    private readonly M1ddcClient _m1ddc;
    private readonly IPreferredInputSourceProbe _probe;
    private readonly LgTvSwitcherOptions _options;
    private readonly ILogger<MacDisplayWatcher> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string? _lastKey;

    // 起動直後は判定前なので務められる扱いにし、最初のポーリングで確定させる。
    private volatile bool _eligible = true;

    public MacDisplayWatcher(
        M1ddcClient m1ddc,
        IPreferredInputSourceProbe probe,
        IOptions<LgTvSwitcherOptions> options,
        ILogger<MacDisplayWatcher> logger)
    {
        _m1ddc = m1ddc;
        _probe = probe;
        _options = options.Value;
        _logger = logger;
    }

    public IObservable<DisplaySnapshot> Notifications => _subject.AsObservable();

    /// <summary>優先モニタの表示元を DDC で判定できているときだけ、TV を切り替えるリーダーを務められる。</summary>
    public bool IsEligible => _eligible;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => PollLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                PollOnce();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Display poll failed.");
            }
        }
        while (await WaitNextAsync(timer, ct).ConfigureAwait(false));
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

    private void PollOnce()
    {
        var displays = _m1ddc.ListDisplays() ?? [];
        var preferred = displays.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(_options.PreferredMonitorName) &&
            d.Name.Contains(_options.PreferredMonitorName, StringComparison.OrdinalIgnoreCase));
        var source = preferred is null ? PreferredInputSource.Unknown : _probe.Probe();
        _eligible = source != PreferredInputSource.Unknown;

        var key = string.Join('|', displays.Select(d => d.Uuid)) + "#" + source;
        if (key == _lastKey)
        {
            return;
        }

        _lastKey = key;

        // macOS からは接続方式を取れない。USB-C/Thunderbolt は DP Alt Mode なので DisplayPort とみなす。
        var monitors = displays
            .Select(d => new MonitorSnapshot($"display {d.Index}", d.Name, default, false, MonitorConnectionKind.DisplayPort, d.Uuid))
            .ToArray();
        var preferredSnapshot = preferred is null ? null : monitors.First(m => m.EdidKey == preferred.Uuid);
        var online = source == PreferredInputSource.ThisPc;

        _logger.LogInformation(
            "Display state changed. Displays=[{Displays}], Preferred={Preferred}, InputSource={Source}",
            string.Join(", ", displays.Select(d => d.Name)),
            preferred?.Name ?? "(not found)",
            source);

        _subject.OnNext(new DisplaySnapshot(
            DateTimeOffset.UtcNow,
            monitors,
            preferredSnapshot,
            online,
            preferred?.Uuid));
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
        }

        _cts?.Dispose();
        _subject.Dispose();
    }
}
