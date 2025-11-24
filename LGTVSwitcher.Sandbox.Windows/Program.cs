using System.IO;
using System.Runtime.Versioning;

using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.Core.Workers;
using LGTVSwitcher.DisplayDetection.Windows;
using LGTVSwitcher.LgWebOsClient;
using LGTVSwitcher.LgWebOsClient.Transport;
using LGTVSwitcher.Sandbox.Windows;
using LGTVSwitcher.Sandbox.Windows.DisplayDetection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("Display detection prototype currently runs on Windows only.");
    return;
}

#if DEBUG
Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
#endif

#pragma warning disable CA1416
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        var localDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var statePath = Path.Combine(localDataPath, "LGTVSwitcher", "device-state.json");
        config.AddJsonFile(statePath, optional: true, reloadOnChange: true);
    })
    .ConfigureServices((context, services) => ConfigureServices(context.Configuration, services))
    .UseConsoleLifetime()
    .Build();

await host.RunAsync().ConfigureAwait(false);

[SupportedOSPlatform("windows")]
static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
{
    services.Configure<LgTvSwitcherOptions>(configuration.GetSection("LgTvSwitcher"));

    services.AddSingleton<ILgTvClientKeyStore>(sp =>
    {
        var localDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settingsPath = Path.Combine(localDataPath, "LGTVSwitcher", "device-state.json");
        var logger = sp.GetRequiredService<ILogger<FileBasedLgTvClientKeyStore>>();
        return new FileBasedLgTvClientKeyStore(settingsPath, logger);
    });

    services.AddSingleton<ILgTvTransport, DefaultWebSocketTransport>();
    services.AddSingleton<ILgTvResponseParser, LgTvResponseParser>();
    services.AddSingleton<ILgTvController, LgTvController>();

    services.AddSingleton<WindowsMessagePump>();
    services.AddSingleton<IMonitorEnumerator, Win32MonitorEnumerator>();
    services.AddSingleton<IDisplayChangeDetector, WindowsMonitorDetector>();
    services.AddSingleton<IDisplaySnapshotProvider, WindowsDisplaySnapshotProvider>();

    services.AddHostedService<DisplaySyncWorker>();
    services.AddHostedService<DisplayChangeLoggingService>();
}
#pragma warning restore CA1416