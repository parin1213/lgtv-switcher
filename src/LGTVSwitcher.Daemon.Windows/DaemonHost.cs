using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.Core.Workers;
using LGTVSwitcher.Core.LgWebOs;
using LGTVSwitcher.Core.LgWebOs.Transport;
using LGTVSwitcher.Daemon.Windows.DisplayDetection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;

namespace LGTVSwitcher.Daemon.Windows;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
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
            .ConfigureServices((context, services) => ConfigureServices(context.Configuration, services))
            .UseConsoleLifetime()
            .Build();
    }

    internal static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        services.AddSingleton<WindowsPathProvider>();

        services.Configure<LgTvSwitcherOptions>(configuration.GetSection("LgTvSwitcher"));

        services.AddSingleton<ILgTvClientKeyStore>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<JsonFileClientKeyStore>>();
            var storagePath = sp.GetRequiredService<WindowsPathProvider>().GetStateFilePath();
            return new JsonFileClientKeyStore(storagePath, logger);
        });

        services.AddSingleton<ILgTvWebSocketTransport, DefaultWebSocketTransport>();
        services.AddSingleton<LgTvResponseParser>();
        services.AddSingleton<SsdpResponseParser>();
        services.AddSingleton<SsdpLgTvDiscoveryService>();
        services.AddSingleton<ILgTvDiscoveryService>(sp => sp.GetRequiredService<SsdpLgTvDiscoveryService>());
        services.AddSingleton<LgTvSession>();
        services.AddSingleton<ILgTvSession>(sp => sp.GetRequiredService<LgTvSession>());
        services.AddSingleton<ILgTvController, LgTvController>();

        services.AddSingleton<WindowsMessagePump>();
        services.AddSingleton<IMonitorEnumerator, Win32MonitorEnumerator>();
        services.AddSingleton<WindowsMonitorDetector>();
        services.AddSingleton<IDisplaySnapshotProvider, WindowsDisplaySnapshotProvider>();
        services.AddSingleton<IPreferredInputSourceProbe, DdcInputSourceProbe>();

        services.AddHostedService<DisplaySyncWorker>();
    }

}
