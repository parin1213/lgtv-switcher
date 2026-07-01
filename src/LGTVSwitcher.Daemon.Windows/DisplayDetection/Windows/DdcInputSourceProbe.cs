using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LGTVSwitcher.Daemon.Windows.DisplayDetection;

/// <summary>
/// DDC/CI (VCP 0x60 = Input Source) を用いて、優先モニタが今どの映像入力を映しているかを判定するプローブ。
/// </summary>
/// <remarks>
/// DELL U2725QE 等の USB-C/KVM モニタは、表示を Mac へ切り替えても Windows への DisplayPort リンクを
/// 生かしたままにする。よって GDI/WMI の列挙では「このPCを映しているか」を判別できない。
/// 本プローブは物理モニタへ DDC/CI で問い合わせ、現在選択中の入力ソースが「このPC」の入力か判定する。
/// 判定は同期呼び出し（DDC は数十ms）で、同期ループの判定時に都度実行する想定。
/// </remarks>
[SupportedOSPlatform("windows")]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class DdcInputSourceProbe : IPreferredInputSourceProbe
{
    private const byte VcpInputSource = 0x60;

    // DDC/CI は I2C 越しのため単発で失敗することがある。短時間リトライで平滑化する。
    private const int ReadAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(120);

    private readonly LgTvSwitcherOptions _options;
    private readonly ILogger<DdcInputSourceProbe> _logger;

    // 「このPCを映している」とみなす VCP 0x60 コード（設定の名前を解決したもの、下位バイトで比較）。
    private readonly int[] _thisPcCodes;

    // 直近に確定した状態。全リトライが失敗したときにこれを維持し、一過性の失敗で判定が揺れないようにする。
    private volatile PreferredInputSource _lastKnown = PreferredInputSource.Unknown;

    public DdcInputSourceProbe(IOptions<LgTvSwitcherOptions> options, ILogger<DdcInputSourceProbe> logger)
    {
        _options = options.Value;
        _logger = logger;
        _thisPcCodes = ResolveThisPcCodes(_options.PreferredMonitorThisPcInputSources);
    }

    private int[] ResolveThisPcCodes(string[]? names)
    {
        if (names is null || names.Length == 0)
        {
            return Array.Empty<int>();
        }

        var codes = new List<int>(names.Length);
        foreach (var name in names)
        {
            if (MonitorInputSourceCatalog.TryResolve(name, out var code))
            {
                codes.Add(code & 0xFF);
            }
            else
            {
                _logger.LogWarning(
                    "PreferredMonitorThisPcInputSources の値 '{Value}' を入力ソースへ解決できません（例: DisplayPort / HDMI / USB-C / 0x1B）。無視します。",
                    name);
            }
        }

        return codes.ToArray();
    }

    public PreferredInputSource Probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return PreferredInputSource.Unknown;
        }

        if (_thisPcCodes.Length == 0)
        {
            // DDC 判定が無効化されている（設定が空、または全て解決不能）。従来の列挙ベースへフォールバック。
            return PreferredInputSource.Unknown;
        }

        var preferredName = _options.PreferredMonitorName;
        if (string.IsNullOrWhiteSpace(preferredName))
        {
            return PreferredInputSource.Unknown;
        }

        for (var attempt = 0; attempt < ReadAttempts; attempt++)
        {
            var current = TryReadPreferredInputSource(preferredName);
            if (current is not null)
            {
                // 一部モニタは上位バイトに付随値を載せるため、入力コードは下位バイトで比較する。
                var code = (int)(current.Value & 0xFF);
                var result = PreferredInputSource.OtherSource;
                foreach (var thisPc in _thisPcCodes)
                {
                    if (thisPc == code)
                    {
                        result = PreferredInputSource.ThisPc;
                        break;
                    }
                }

                _logger.LogDebug(
                    "DDC input source of '{Monitor}' = 0x{Code:X2} ({Result}).", preferredName, code, result);
                _lastKnown = result;
                return result;
            }

            if (attempt < ReadAttempts - 1)
            {
                Thread.Sleep(RetryDelay);
            }
        }

        // 全リトライ失敗。直近の確定値があればそれを維持（一過性の DDC 失敗で TV を奪わないため）。
        if (_lastKnown != PreferredInputSource.Unknown)
        {
            _logger.LogDebug("DDC read failed for '{Monitor}'; reusing last known {State}.", preferredName, _lastKnown);
            return _lastKnown;
        }

        return PreferredInputSource.Unknown;
    }

    private uint? TryReadPreferredInputSource(string preferredName)
    {
        var handles = new List<IntPtr>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) => { handles.Add(h); return true; }, IntPtr.Zero);

        foreach (var hMonitor in handles)
        {
            if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) || count == 0)
            {
                continue;
            }

            var monitors = new NativeMethods.PHYSICAL_MONITOR[count];
            if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
            {
                continue;
            }

            try
            {
                foreach (var monitor in monitors)
                {
                    var description = monitor.szPhysicalMonitorDescription ?? string.Empty;
                    if (description.IndexOf(preferredName, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                            monitor.hPhysicalMonitor, VcpInputSource, out _, out var currentValue, out _))
                    {
                        return currentValue;
                    }

                    _logger.LogDebug(
                        "DDC VCP 0x60 read failed for '{Description}' (err={Error}).",
                        description,
                        Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                NativeMethods.DestroyPhysicalMonitors(count, monitors);
            }
        }

        return null;
    }
}
