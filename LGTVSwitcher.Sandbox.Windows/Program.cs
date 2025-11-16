/*
using System.Runtime.Versioning;
using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.Core.Workers;
using LGTVSwitcher.DisplayDetection.Windows;
using LGTVSwitcher.LgWebOsClient;
using LGTVSwitcher.LgWebOsClient.Transport;
using LGTVSwitcher.Sandbox.Windows.DisplayDetection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("Display detection prototype currently runs on Windows only.");
    return;
}

#pragma warning disable CA1416
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) => ConfigureServices(context.Configuration, services))
    .UseConsoleLifetime()
    .Build();

await host.RunAsync().ConfigureAwait(false);

[SupportedOSPlatform("windows")]
static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
{
    services.Configure<LgTvSwitcherOptions>(configuration.GetSection("LgTvSwitcher"));

    services.AddSingleton<WindowsMessagePump>();
    services.AddSingleton<IMonitorEnumerator, Win32MonitorEnumerator>();
    services.AddSingleton<IDisplayChangeDetector, WindowsMonitorDetector>();
    services.AddSingleton<ILgTvTransport, DefaultWebSocketTransport>();
    services.AddSingleton<ILgTvController, LgTvController>();
    services.AddHostedService<DisplaySyncWorker>();
    services.AddHostedService<DisplayChangeLoggingService>();
}
#pragma warning restore CA1416
*/

using System.IO;
using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.LgWebOsClient;
using LGTVSwitcher.LgWebOsClient.Transport;
using LGTVSwitcher.Sandbox.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var basePath = Directory.GetCurrentDirectory();
var configurationRoot = new ConfigurationBuilder()
    .SetBasePath(basePath)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var options = configurationRoot.GetSection("LgTvSwitcher").Get<LgTvSwitcherOptions>() ?? new LgTvSwitcherOptions();
options.TargetInputId = "HDMI_2";

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(LogLevel.Debug)
        .AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });
});
var lgTvControllerLogger = loggerFactory.CreateLogger<LgTvController>();
var transportLogger = loggerFactory.CreateLogger<DefaultWebSocketTransport>();
var keyStoreLogger = loggerFactory.CreateLogger<FileBasedLgTvClientKeyStore>();
var clientKeyStore = new FileBasedLgTvClientKeyStore(
    Path.Combine(basePath, "appsettings.json"),
    keyStoreLogger);

Console.WriteLine($"Switching LG TV ({options.TvHost}) to input {options.TargetInputId}...");

await using var transport = new DefaultWebSocketTransport(transportLogger);
await using var controller = new LgTvController(
    transport,
    Options.Create(options),
    lgTvControllerLogger,
    clientKeyStore);
try
{
    await controller.SwitchInputAsync(options.TargetInputId, CancellationToken.None).ConfigureAwait(false);
    Console.WriteLine("Switch command sent.");
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to switch input: {ex.Message}");
    throw;
}
