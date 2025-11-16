namespace LGTVSwitcher.Core.LgTv;

public interface ILgTvController : IAsyncDisposable
{
    Task<string?> EnsureConnectedAsync(CancellationToken cancellationToken);

    Task SwitchInputAsync(string inputId, CancellationToken cancellationToken);
}
