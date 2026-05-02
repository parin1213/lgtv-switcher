using ConsoleAppFramework;

using LGTVSwitcher.Daemon.Windows;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("LGTV Switcher Daemon runs on Windows only.");
    return;
}

var normalizedArgs = CliArgs.Normalize(args);

var app = ConsoleApp.Create();
app.Add<DaemonCommands>();

await app.RunAsync(normalizedArgs);
