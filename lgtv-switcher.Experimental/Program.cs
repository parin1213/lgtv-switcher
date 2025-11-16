using System.Text.Json;
using LGTVSwitcher.Experimental.DisplayDetection;
using LGTVSwitcher.Experimental.DisplayDetection.Windows;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("Display detection prototype currently runs on Windows only.");
    return;
}

await using var detector = new WindowsMonitorDetector(new WindowsMessagePump(), new Win32MonitorEnumerator());

detector.DisplayChanged += (_, args) =>
{
    var payload = new
    {
        args.Timestamp,
        args.Reason,
        Monitors = args.Snapshots.Select(snapshot => new
        {
            snapshot.DeviceName,
            snapshot.FriendlyName,
            Bounds = new { snapshot.Bounds.X, snapshot.Bounds.Y, snapshot.Bounds.Width, snapshot.Bounds.Height },
            snapshot.IsPrimary,
            Connection = snapshot.ConnectionKind.ToString(),
        }),
    };

    Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
};

await detector.StartAsync().ConfigureAwait(false);
Console.WriteLine("Listening for monitor topology changes... Press Ctrl+C to exit.");

var done = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    done.TrySetResult();
};

await done.Task.ConfigureAwait(false);
