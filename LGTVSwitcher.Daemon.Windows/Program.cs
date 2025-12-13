using ConsoleAppFramework;

using LGTVSwitcher.Daemon.Windows;
using Microsoft.Extensions.DependencyInjection;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("LGTV Switcher Daemon runs on Windows only.");
    return;
}

var normalizedArgs = CliArgs.Normalize(args);

var app = ConsoleApp.Create();
app.ConfigureServices((context, services) =>
{
    services.AddSingleton<IDaemonHostFactory, DefaultDaemonHostFactory>();
});
app.Add<DaemonCommands>();

await app.RunAsync(normalizedArgs);
