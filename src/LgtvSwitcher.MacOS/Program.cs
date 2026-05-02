using LgtvSwitcher.MacOS;
using Microsoft.Extensions.Hosting;

if (!OperatingSystem.IsMacOS())
{
    Console.WriteLine("LgtvSwitcher macOS daemon runs on macOS only.");
    return;
}

using var host = MacDaemonHost.Build(args);
await host.RunAsync();
