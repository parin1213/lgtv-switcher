using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;

using LgtvSwitcher.MacOS;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (!OperatingSystem.IsMacOS())
{
    Console.WriteLine("LgtvSwitcher macOS daemon runs on macOS only.");
    return 1;
}

var command = args.Length > 0 ? args[0] : "run";
switch (command)
{
    case "run":
    {
        using var host = MacDaemonHost.Build(args.Skip(1).ToArray());
        await host.RunAsync();
        return 0;
    }

    // 優先モニタの現在の入力ソースを DDC で読む（PreferredMonitorThisPcInputSources の確認用）
    case "probe-input":
    {
        using var host = MacDaemonHost.Build([]);
        var m1ddc = host.Services.GetRequiredService<M1ddcClient>();
        var probe = (MacDdcInputSourceProbe)host.Services.GetRequiredService<IPreferredInputSourceProbe>();
        var display = probe.FindPreferredDisplay();
        var raw = display is null ? null : m1ddc.GetInputSource(display.Index);
        Console.WriteLine($"Preferred monitor = {display?.Name ?? "(not found)"}, raw input = {(raw is { } v ? $"0x{v:X2}" : "(unknown)")}");
        Console.WriteLine($"PreferredInputSource = {probe.Probe()}");
        return 0;
    }

    // LG TV の現在の入力 ID を表示（TargetInputId / FallbackInputId の確認用。初回は TV にペアリング確認が出る）
    case "tv-input":
    {
        using var host = MacDaemonHost.Build([]);
        var controller = host.Services.GetRequiredService<ILgTvController>();
        var input = await controller.GetCurrentInputAsync(CancellationToken.None);
        Console.WriteLine($"TV current input = {input ?? "(unknown)"}");
        return 0;
    }

    default:
        Console.Error.WriteLine("usage: LgtvSwitcher.MacOS [run|probe-input|tv-input]");
        return 2;
}
