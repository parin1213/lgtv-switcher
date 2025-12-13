using ConsoleAppFramework;

using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.LgWebOsClient;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LGTVSwitcher.Daemon.Windows;

public sealed class DaemonCommands
{
    private readonly IDaemonHostFactory _hostFactory;

    public DaemonCommands(IDaemonHostFactory hostFactory)
    {
        _hostFactory = hostFactory;
    }

    /// <summary>デーモン起動（DisplaySyncWorker 実行）</summary>
    [Command("run")]
    public async Task Run(CancellationToken ct = default)
    {
        using var host = _hostFactory.BuildHost(Array.Empty<string>());
        await host.RunAsync(ct).ConfigureAwait(false);
    }

    /// <summary>SSDP で TV を検出し、必要なら PreferredTvUsn を保存</summary>
    [Command("discover")]
    public async Task Discover(
        bool pair = false,
        string? pairUsn = null,
        string? pairIp = null,
        CancellationToken ct = default)
    {
        using var host = _hostFactory.BuildHost(Array.Empty<string>());
        using var scope = host.Services.CreateScope();

        var discovery = scope.ServiceProvider.GetRequiredService<ILgTvDiscoveryService>();
        var store = scope.ServiceProvider.GetRequiredService<ILgTvClientKeyStore>();

        var results = await discovery.DiscoverAsync(ct).ConfigureAwait(false);

        Console.WriteLine($"SSDP で検出した LGTV 候補: {results.Count} 台");
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            Console.WriteLine($"{i}: IP={r.Address}  USN={r.Usn ?? "(なし)"}  ST={r.St ?? ""}  LOCATION={r.Location ?? ""}");
        }

        var target = SelectPairTarget(results, pairUsn, pairIp, pair);
        if (target is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(target.Usn))
        {
            Console.WriteLine("選択された TV に USN が無いため保存できません。");
            return;
        }

        await store.PersistPreferredTvUsnAsync(target.Usn!, ct).ConfigureAwait(false);
        Console.WriteLine($"PreferredTvUsn を保存しました: {target.Usn} (IP={target.Address})");
    }

    private static LgTvDiscoveryResult? SelectPairTarget(
        IReadOnlyList<LgTvDiscoveryResult> results,
        string? pairUsn,
        string? pairIp,
        bool pairFlag)
    {
        if (results.Count == 0)
        {
            Console.WriteLine("検出結果が空のためペアリング対象が選べません。");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(pairUsn))
        {
            var byUsn = results.FirstOrDefault(r => string.Equals(r.Usn, pairUsn, StringComparison.OrdinalIgnoreCase));
            if (byUsn is null)
            {
                Console.WriteLine($"USN {pairUsn} は検出結果に含まれていません。");
            }
            return byUsn;
        }

        if (!string.IsNullOrWhiteSpace(pairIp))
        {
            var byIp = results.FirstOrDefault(r => string.Equals(r.Address, pairIp, StringComparison.OrdinalIgnoreCase));
            if (byIp is null)
            {
                Console.WriteLine($"IP {pairIp} は検出結果に含まれていません。");
            }
            return byIp;
        }

        if (pairFlag && results.Count == 1)
        {
            return results[0];
        }

        if (pairFlag && results.Count > 1)
        {
            Console.WriteLine("複数台検出されたため自動選択できません。--pair-usn または --pair-ip を指定してください。");
        }

        return null;
    }
}
