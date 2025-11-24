using System.Runtime.Versioning;

using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.Core.Workers;
using LGTVSwitcher.Daemon.Windows;
using LGTVSwitcher.DisplayDetection.Windows;
using LGTVSwitcher.LgWebOsClient;
using LGTVSwitcher.LgWebOsClient.Transport;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Events;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("LGTV Switcher Daemon runs on Windows only.");
    return;
}

#pragma warning disable CA1416 // Windows only
var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((context, services, loggerConfiguration) =>
    {
        loggerConfiguration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();
    })
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
#pragma warning restore CA1416

[SupportedOSPlatform("windows")]
static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
{
    services.Configure<LgTvSwitcherOptions>(configuration.GetSection("LgTvSwitcher"));

    services.AddSingleton<ILgTvClientKeyStore>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<FileBasedLgTvClientKeyStore>>();
        var localDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var storagePath = Path.Combine(localDataPath, "LGTVSwitcher", "device-state.json");
        return new FileBasedLgTvClientKeyStore(storagePath, logger);
    });

    services.AddSingleton<ILgTvTransport, DefaultWebSocketTransport>();
    services.AddSingleton<ILgTvResponseParser, LgTvResponseParser>();
    services.AddSingleton<ILgTvController, LgTvController>();

    services.AddSingleton<WindowsMessagePump>();
    services.AddSingleton<IMonitorEnumerator, Win32MonitorEnumerator>();
    services.AddSingleton<IDisplayChangeDetector, WindowsMonitorDetector>();
    services.AddSingleton<IDisplaySnapshotProvider, WindowsDisplaySnapshotProvider>();

    services.AddHostedService<DisplaySyncWorker>();
}
