using LGTVSwitcher.Core;
using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.Core.Workers;
using LGTVSwitcher.DisplayDetection.Windows;
using LGTVSwitcher.LgWebOsClient;
using LGTVSwitcher.LgWebOsClient.Transport;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;

namespace LGTVSwitcher.Daemon.Windows;

internal static class DaemonHost
{
    public static IHost Build(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .UseContentRoot(AppContext.BaseDirectory)
            .UseSerilog((context, services, loggerConfiguration) =>
            {
                loggerConfiguration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext();
            })
            .ConfigureAppConfiguration((context, config) =>
            {
                AddStateConfiguration(config);
            })
            .ConfigureServices((context, services) => ConfigureServices(context.Configuration, services))
            .UseConsoleLifetime()
            .Build();
    }

    internal static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        services.AddSingleton<IAppPathProvider, WindowsPathProvider>();

        services.Configure<LgTvSwitcherOptions>(configuration.GetSection("LgTvSwitcher"));

        services.AddSingleton<ILgTvClientKeyStore>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FileBasedLgTvClientKeyStore>>();
            var pathProvider = sp.GetRequiredService<IAppPathProvider>();
            var storagePath = pathProvider.GetStateFilePath();
            return new FileBasedLgTvClientKeyStore(storagePath, logger);
        });

        services.AddSingleton<ILgTvTransport, DefaultWebSocketTransport>();
        services.AddSingleton<ILgTvResponseParser, LgTvResponseParser>();
        services.AddSingleton<ILgTvDiscoveryService, SsdpLgTvDiscoveryService>();
        services.AddSingleton<ILgTvController, LgTvController>();

        services.AddSingleton<WindowsMessagePump>();
        services.AddSingleton<IMonitorEnumerator, Win32MonitorEnumerator>();
        services.AddSingleton<IDisplayChangeDetector, WindowsMonitorDetector>();
        services.AddSingleton<IDisplaySnapshotProvider, WindowsDisplaySnapshotProvider>();

        services.AddHostedService<DisplaySyncWorker>();
    }

    internal static void AddStateConfiguration(IConfigurationBuilder config)
    {
        var pathProvider = new WindowsPathProvider();
        var statePath = pathProvider.GetStateFilePath();
        config.AddJsonFile(statePath, optional: true, reloadOnChange: true);
    }
}
