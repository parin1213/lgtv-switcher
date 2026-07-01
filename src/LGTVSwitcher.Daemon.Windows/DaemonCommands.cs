using ConsoleAppFramework;

using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.Core.LgWebOs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LGTVSwitcher.Daemon.Windows;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class DaemonCommands
{
    /// <summary>デーモン起動（DisplaySyncWorker 実行）</summary>
    [Command("run")]
    public async Task Run(CancellationToken ct = default)
    {
        using var host = DaemonHost.Build(Array.Empty<string>());
        await host.RunAsync(ct).ConfigureAwait(false);
    }

    /// <summary>優先モニタの現在の入力ソースを DDC/CI で読む（検証用POC）</summary>
    [Command("probe-input")]
    public Task ProbeInput()
    {
        using var host = DaemonHost.Build(Array.Empty<string>());
        var probe = host.Services.GetRequiredService<IPreferredInputSourceProbe>();
        var result = probe.Probe();
        Console.WriteLine($"PreferredInputSource = {result}");
        return Task.CompletedTask;
    }

    /// <summary>SSDP で TV を検出し、必要なら PreferredTvUsn を保存</summary>
    [Command("discover")]
    public async Task Discover(
        bool pair = false,
        string? pairUsn = null,
        string? pairIp = null,
        CancellationToken ct = default)
    {
        using var host = DaemonHost.Build(Array.Empty<string>());
        using var scope = host.Services.CreateScope();

        var discovery = scope.ServiceProvider.GetRequiredService<SsdpLgTvDiscoveryService>();
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
