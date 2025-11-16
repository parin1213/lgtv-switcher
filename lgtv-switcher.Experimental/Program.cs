using System.Runtime.Versioning;
using LGTVSwitcher.Experimental.DisplayDetection;
using LGTVSwitcher.Experimental.DisplayDetection.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("Display detection prototype currently runs on Windows only.");
    return;
}

#pragma warning disable CA1416
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services => ConfigureServices(services))
    .UseConsoleLifetime()
    .Build();

await host.RunAsync().ConfigureAwait(false);

[SupportedOSPlatform("windows")]
static void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<WindowsMessagePump>();
    services.AddSingleton<IMonitorEnumerator, Win32MonitorEnumerator>();
    services.AddSingleton<IDisplayChangeDetector, WindowsMonitorDetector>();
    services.AddHostedService<DisplayChangeLoggingService>();
}
#pragma warning restore CA1416
