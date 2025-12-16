using System.Net;

namespace LGTVSwitcher.LgWebOsClient;

public interface ISsdpResponseParser
{
    LgTvDiscoveryResult? Parse(string responseText, IPEndPoint remoteEndPoint);

    int GetPriority(string? st);
}
