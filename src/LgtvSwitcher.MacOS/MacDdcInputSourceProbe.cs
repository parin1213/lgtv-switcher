using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LgtvSwitcher.MacOS;

/// <summary>
/// m1ddc で優先モニタの VCP 0x60（Input Source）を読み、「今この Mac を映しているか」を判定するプローブ。
/// </summary>
/// <remarks>
/// DELL U2725QE は表示を Windows(DP) へ切り替えても Mac への USB-C リンクを保つため、
/// macOS 側でも列挙有無では判別できない。Windows の <c>DdcInputSourceProbe</c> と同じく都度 DDC で判定する。
/// Mac からの DDC 応答は時々化けるため、<see cref="InputSourceStabilizer"/> で確定させてから返す。
/// 優先モニタが列挙に居ないとき（Mac を外した等）は確定状態を捨てて Unknown を返す。
/// 外した Mac が直前の状態のまま TV を奪い続けないようにするため。
/// </remarks>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class MacDdcInputSourceProbe : IPreferredInputSourceProbe
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(60);

    private readonly M1ddcClient _m1ddc;
    private readonly LgTvSwitcherOptions _options;
    private readonly ILogger<MacDdcInputSourceProbe> _logger;
    private readonly int[] _thisPcCodes;
    private readonly InputSourceStabilizer _stabilizer = new();
    private readonly Lock _gate = new();

    public MacDdcInputSourceProbe(
        M1ddcClient m1ddc,
        IOptions<LgTvSwitcherOptions> options,
        ILogger<MacDdcInputSourceProbe> logger)
    {
        _m1ddc = m1ddc;
        _options = options.Value;
        _logger = logger;
        _thisPcCodes = _options.ResolveThisPcInputSources()
            .Select(name => MonitorInputSourceCatalog.TryResolve(name, out var code) ? code & 0xFF : -1)
            .Where(code => code >= 0)
            .ToArray();
    }

    public PreferredInputSource Probe()
    {
        if (_thisPcCodes.Length == 0 || string.IsNullOrWhiteSpace(_options.PreferredMonitorName) || !_m1ddc.IsAvailable)
        {
            return PreferredInputSource.Unknown;
        }

        lock (_gate)
        {
            var display = FindPreferredDisplay();
            if (display is null)
            {
                _stabilizer.Reset();
                return PreferredInputSource.Unknown;
            }

            var code = InputSourceStabilizer.FindStableValue(ReadInputs(display.Index));
            var observed = code switch
            {
                null => PreferredInputSource.Unknown,
                var c when _thisPcCodes.Contains(c.Value & 0xFF) => PreferredInputSource.ThisPc,
                _ => PreferredInputSource.OtherSource,
            };

            var result = _stabilizer.Apply(observed);
            _logger.LogDebug(
                "DDC input source of '{Monitor}' = {Code} (observed {Observed}, confirmed {Result}).",
                display.Name,
                code is { } v ? $"0x{v & 0xFF:X2}" : "(unstable)",
                observed,
                result);
            return result;
        }
    }

    private IEnumerable<int?> ReadInputs(int displayIndex)
    {
        for (var i = 0; i < InputSourceStabilizer.MaxReads; i++)
        {
            if (i > 0)
            {
                Thread.Sleep(RetryDelay);
            }

            yield return _m1ddc.GetInputSource(displayIndex);
        }
    }

    /// <summary>優先モニタ名に部分一致する外部ディスプレイを返す。</summary>
    internal M1ddcDisplay? FindPreferredDisplay()
        => _m1ddc.ListDisplays()?.FirstOrDefault(d =>
            d.Name.Contains(_options.PreferredMonitorName, StringComparison.OrdinalIgnoreCase));
}
