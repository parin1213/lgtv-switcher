using System.Linq;
using System.Threading.Channels;

using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LGTVSwitcher.Core.Workers;

public sealed class DisplaySyncWorker : BackgroundService
{
    private readonly IDisplayChangeDetector _detector;
    private readonly ILgTvController _lgTvController;
    private readonly ILogger<DisplaySyncWorker> _logger;
    private readonly LgTvSwitcherOptions _options;
    private readonly Channel<IReadOnlyList<MonitorSnapshot>> _channel =
        Channel.CreateBounded<IReadOnlyList<MonitorSnapshot>>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    private bool? _lastPreferredOnline;

    public DisplaySyncWorker(
        IDisplayChangeDetector detector,
        ILgTvController lgTvController,
        IOptions<LgTvSwitcherOptions> options,
        ILogger<DisplaySyncWorker> logger)
    {
        _detector = detector;
        _lgTvController = lgTvController;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _detector.DisplayChanged += OnDisplayChanged;

        try
        {
            await _detector.StartAsync(stoppingToken).ConfigureAwait(false);

            await foreach (var snapshots in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await HandleSnapshotsAsync(snapshots, stoppingToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _detector.DisplayChanged -= OnDisplayChanged;
        }
    }

    private void OnDisplayChanged(object? sender, DisplaySnapshotChangedEventArgs e)
    {
        if (!_channel.Writer.TryWrite(e.Snapshots))
        {
            _logger.LogWarning("Display snapshot event dropped because the channel is closed.");
        }
    }

    private async Task HandleSnapshotsAsync(IReadOnlyList<MonitorSnapshot> snapshots, CancellationToken cancellationToken)
    {
        var preferredOnline = IsPreferredMonitorPresent(snapshots);

        if (_lastPreferredOnline is not null && preferredOnline == _lastPreferredOnline.Value)
        {
            return;
        }

        _lastPreferredOnline = preferredOnline;

        var targetInput = preferredOnline ? _options.TargetInputId : _options.FallbackInputId;

        if (string.IsNullOrWhiteSpace(targetInput))
        {
            _logger.LogInformation(
                "Preferred monitor online state changed to {State}, but no input mapping is configured for this state.",
                preferredOnline);
            return;
        }

        string? currentInput = null;
        try
        {
            currentInput = await _lgTvController.GetCurrentInputAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Failed to query the current LG TV input; proceeding with switch.");
        }

        if (!string.IsNullOrWhiteSpace(currentInput) &&
            string.Equals(currentInput, targetInput, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("LG TV already set to {Input}; no switch required.", targetInput);
            return;
        }

        try
        {
            _logger.LogInformation("Switching LG TV input to {Input} (preferred monitor online = {State})", targetInput, preferredOnline);
            await _lgTvController.SwitchInputAsync(targetInput, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to switch LG TV input to {Input}", targetInput);
        }
    }

    private bool IsPreferredMonitorPresent(IReadOnlyList<MonitorSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return false;
        }

        var preferredName = _options.PreferredMonitorName;

        if (string.IsNullOrWhiteSpace(preferredName))
        {
            return false;
        }

        return snapshots.Any(snapshot =>
            string.Equals(snapshot.FriendlyName, preferredName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(snapshot.DeviceName, preferredName, StringComparison.OrdinalIgnoreCase) ||
            snapshot.FriendlyName.Contains(preferredName, StringComparison.OrdinalIgnoreCase));
    }
}