using System;

namespace LGTVSwitcher.Core.LgWebOs;

/// <summary>
/// SSDP 探索の対象から除外すべき仮想/トンネル系ネットワークインターフェースを判定する。
/// </summary>
/// <remarks>
/// WSL の vEthernet や Tailscale/VPN などの仮想アダプタは LG TV が居る物理 LAN とは別セグメントで、
/// マルチキャスト探索しても応答が返らない（recv=0）。毎サイクル無駄打ちして CPU とログを消費するため除外する。
/// 名前・説明の部分一致（大文字小文字無視）で判定する保守的なヒューリスティック。
/// </remarks>
public static class SsdpInterfaceFilter
{
    private static readonly string[] VirtualNameHints =
    [
        "wsl",
        "vethernet",
        "hyper-v",
        "virtual",
        "vmware",
        "virtualbox",
        "tailscale",
        "zerotier",
        "docker",
        "loopback",
        "vpn",
        "tap-windows",
        "bluetooth",
    ];

    /// <summary>
    /// NIC の名前または説明が仮想/トンネル系を示す場合 true。
    /// </summary>
    public static bool IsVirtual(string? name, string? description)
        => ContainsHint(name) || ContainsHint(description);

    private static bool ContainsHint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var hint in VirtualNameHints)
        {
            if (value.Contains(hint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
