using System.Threading;
using System.Threading.Tasks;

namespace LGTVSwitcher.LgWebOsClient;

public interface ILgTvDiscoveryService
{
    Task<IReadOnlyList<LgTvDiscoveryResult>> DiscoverAsync(CancellationToken cancellationToken);
}
