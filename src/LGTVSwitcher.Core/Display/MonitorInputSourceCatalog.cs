using System;
using System.Collections.Generic;
using System.Globalization;

namespace LGTVSwitcher.Core.Display;

/// <summary>
/// モニタの映像入力インターフェース名と DDC/CI VCP 0x60(Input Source) の値を相互変換するカタログ。
/// </summary>
/// <remarks>
/// 設定を数値ではなく <c>"DisplayPort"</c> / <c>"HDMI"</c> / <c>"USB-C"</c> のような名前で書けるようにする。
/// DisplayPort/HDMI/DVI/VGA は MCCS 標準コード。USB-C(0x19) はベンダ依存で、値は DELL U2725QE 実測。
/// 別モニタで異なる場合は名前ではなく生の値（<c>"0x1B"</c> や <c>"27"</c>）を指定できる。
/// </remarks>
public static class MonitorInputSourceCatalog
{
    // 名前 → VCP 0x60 コード（大文字小文字・別名を吸収）。
    private static readonly IReadOnlyDictionary<string, int> NameToCode = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
    {
        ["DisplayPort"] = 0x0F,
        ["DisplayPort1"] = 0x0F,
        ["DP"] = 0x0F,
        ["DP1"] = 0x0F,
        ["DisplayPort2"] = 0x10,
        ["DP2"] = 0x10,
        ["HDMI"] = 0x11,
        ["HDMI1"] = 0x11,
        ["HDMI2"] = 0x12,
        ["USB-C"] = 0x19,
        ["USBC"] = 0x19,
        ["USB"] = 0x19,
        ["Thunderbolt"] = 0x19,
        ["TB"] = 0x19,
        ["DVI"] = 0x03,
        ["DVI1"] = 0x03,
        ["DVI2"] = 0x04,
        ["VGA"] = 0x01,
    };

    /// <summary>
    /// 入力インターフェース名または生の値（<c>"0x19"</c>/<c>"25"</c>）を VCP 0x60 コードへ解決する。
    /// </summary>
    public static bool TryResolve(string? value, out int code)
    {
        code = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        if (NameToCode.TryGetValue(trimmed, out code))
        {
            return true;
        }

        if (trimmed.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(trimmed.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code);
        }

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
    }
}
