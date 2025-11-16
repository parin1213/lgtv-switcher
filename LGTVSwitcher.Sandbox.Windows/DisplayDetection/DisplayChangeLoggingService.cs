using System.Linq;
using System.Text.Json;
using LGTVSwitcher.Core.Display;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LGTVSwitcher.Sandbox.Windows.DisplayDetection;

internal sealed class DisplayChangeLoggingService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IDisplayChangeDetector _detector;
    private readonly ILogger<DisplayChangeLoggingService> _logger;

    public DisplayChangeLoggingService(
        IDisplayChangeDetector detector,
        ILogger<DisplayChangeLoggingService> logger)
    {
        _detector = detector;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _detector.DisplayChanged += OnDisplayChanged;

        try
        {
            await _detector.StartAsync(stoppingToken).ConfigureAwait(false);

            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            _detector.DisplayChanged -= OnDisplayChanged;
        }
    }

    private void OnDisplayChanged(object? sender, DisplaySnapshotChangedEventArgs e)
    {
        var payload = new
        {
            e.Timestamp,
            e.Reason,
            Monitors = e.Snapshots.Select(snapshot => new
            {
                snapshot.DeviceName,
                snapshot.FriendlyName,
                Bounds = new
                {
                    snapshot.Bounds.X,
                    snapshot.Bounds.Y,
                    snapshot.Bounds.Width,
                    snapshot.Bounds.Height,
                },
                snapshot.IsPrimary,
                Connection = snapshot.ConnectionKind.ToString(),
            }),
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        _logger.LogInformation("{Payload}", json);
    }
}
