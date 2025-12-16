using Microsoft.Extensions.Hosting;

namespace LGTVSwitcher.Daemon.Windows;

public interface IDaemonHostFactory
{
    IHost BuildHost(string[] hostArgs);
}
