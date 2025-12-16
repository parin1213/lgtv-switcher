using Microsoft.Extensions.Hosting;

namespace LGTVSwitcher.Daemon.Windows;

internal sealed class DefaultDaemonHostFactory : IDaemonHostFactory
{
    public IHost BuildHost(string[] hostArgs)
    {
        return DaemonHost.Build(hostArgs);
    }
}
