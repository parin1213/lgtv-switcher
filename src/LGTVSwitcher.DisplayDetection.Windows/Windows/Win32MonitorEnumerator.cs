using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using LGTVSwitcher.Core.Display;

namespace LGTVSwitcher.DisplayDetection.Windows;

[SupportedOSPlatform("windows")]
public sealed class Win32MonitorEnumerator : IMonitorEnumerator
{

    public IReadOnlyList<MonitorSnapshot> EnumerateCurrentMonitors()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<MonitorSnapshot>();
        }

        var edidNames = LoadEdidFriendlyNames();
        var connectionKinds = LoadConnectionKinds();
        var results = new List<MonitorSnapshot>();
        uint deviceIndex = 0;

        while (true)
        {
            var displayDevice = CreateDisplayDevice();

            if (!NativeMethods.EnumDisplayDevices(null, deviceIndex, ref displayDevice, 0))
            {
                break;
            }

            deviceIndex++;

            if ((displayDevice.StateFlags & NativeMethods.DisplayDeviceStateFlags.AttachedToDesktop) == 0)
            {
                continue;
            }

            var monitorDevice = CreateDisplayDevice();
            NativeMethods.EnumDisplayDevices(displayDevice.DeviceName, 0, ref monitorDevice, 0);

            var devMode = CreateDevMode();
            NativeMethods.EnumDisplaySettings(displayDevice.DeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref devMode);

            var bounds = new MonitorBounds(devMode.dmPositionX, devMode.dmPositionY, (int)devMode.dmPelsWidth, (int)devMode.dmPelsHeight);
            var edidName = TryMatchEdidName(edidNames, monitorDevice.DeviceID);
            var fallbackName = string.IsNullOrWhiteSpace(monitorDevice.DeviceString)
                ? displayDevice.DeviceString
                : monitorDevice.DeviceString;
            var friendlyName = !string.IsNullOrWhiteSpace(edidName)
                ? edidName
                : (fallbackName ?? "Unknown");

            var connectionKind = TryResolveConnectionKind(connectionKinds, monitorDevice.DeviceID, out var resolvedKind)
                ? resolvedKind
                : InferConnectionKind(monitorDevice.DeviceID, friendlyName);

            var snapshot = new MonitorSnapshot(
                DeviceName: string.IsNullOrWhiteSpace(displayDevice.DeviceName) ? $"DISPLAY{deviceIndex}" : displayDevice.DeviceName,
                FriendlyName: friendlyName,
                Bounds: bounds,
                IsPrimary: (displayDevice.StateFlags & NativeMethods.DisplayDeviceStateFlags.PrimaryDevice) != 0,
                ConnectionKind: connectionKind,
                EdidKey: monitorDevice.DeviceID);

            results.Add(snapshot);
        }

        return results;
    }

    private static NativeMethods.DISPLAY_DEVICE CreateDisplayDevice()
    {
        var device = new NativeMethods.DISPLAY_DEVICE
        {
            cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>(),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceID = string.Empty,
            DeviceKey = string.Empty,
        };
        return device;
    }

    private static NativeMethods.DEVMODE CreateDevMode()
    {
        var devMode = new NativeMethods.DEVMODE
        {
            dmDeviceName = string.Empty,
            dmFormName = string.Empty,
            dmSize = (ushort)Marshal.SizeOf<NativeMethods.DEVMODE>(),
        };

        return devMode;
    }

    private static IReadOnlyDictionary<string, string> LoadEdidFriendlyNames()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Dictionary<string, string>(0, StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi",
                "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID WHERE Active = True");

            using var results = searcher.Get();
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var instance in results.Cast<ManagementObject>())
            {
                var instanceName = instance["InstanceName"] as string;
                if (string.IsNullOrWhiteSpace(instanceName))
                {
                    continue;
                }

                if (instance["UserFriendlyName"] is ushort[] rawName)
                {
                    var decoded = DecodeEdidString(rawName);
                    if (!string.IsNullOrWhiteSpace(decoded))
                    {
                        names[instanceName] = decoded;
                    }
                }
            }

            MonitorLog.Write($"Loaded {names.Count} EDID friendly names.");
            return names;
        }
        catch (ManagementException ex)
        {
            MonitorLog.Write($"Failed to query WMI for EDID names: {ex.Message}");
            return new Dictionary<string, string>(0, StringComparer.OrdinalIgnoreCase);
        }
        catch (SystemException ex)
        {
            MonitorLog.Write($"Unexpected error while loading EDID names: {ex.Message}");
            return new Dictionary<string, string>(0, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyDictionary<string, MonitorConnectionKind> LoadConnectionKinds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Dictionary<string, MonitorConnectionKind>(0, StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi",
                "SELECT InstanceName, VideoOutputTechnology FROM WmiMonitorConnectionParams WHERE Active = True");

            using var results = searcher.Get();
            var map = new Dictionary<string, MonitorConnectionKind>(StringComparer.OrdinalIgnoreCase);

            foreach (var instance in results.Cast<ManagementObject>())
            {
                var instanceName = instance["InstanceName"] as string;
                if (string.IsNullOrWhiteSpace(instanceName))
                {
                    continue;
                }

                if (instance["VideoOutputTechnology"] is uint technologyValue)
                {
                    MonitorLog.Write($"Instance '{instanceName}' VideoOutputTechnology = {technologyValue}");

                    if (MapVideoOutputTechnology(technologyValue) is { } connectionKind)
                    {
                        map[instanceName] = connectionKind;
                    }
                }
            }

            MonitorLog.Write($"Loaded {map.Count} video output technologies.");
            return map;
        }
        catch (ManagementException ex)
        {
            MonitorLog.Write($"Failed to query WMI for connection info: {ex.Message}");
            return new Dictionary<string, MonitorConnectionKind>(0, StringComparer.OrdinalIgnoreCase);
        }
        catch (SystemException ex)
        {
            MonitorLog.Write($"Unexpected error while loading connection info: {ex.Message}");
            return new Dictionary<string, MonitorConnectionKind>(0, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string DecodeEdidString(ushort[] data)
    {
        var builder = new StringBuilder(data.Length);
        foreach (var value in data)
        {
            if (value == 0)
            {
                break;
            }
            builder.Append((char)value);
        }

        return builder.ToString();
    }

    private static string? TryMatchEdidName(IReadOnlyDictionary<string, string> edidNames, string? deviceId)
    {
        if (edidNames.Count == 0 || string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        var deviceToken = ExtractVendorToken(deviceId);
        if (string.IsNullOrEmpty(deviceToken))
        {
            return null;
        }

        foreach (var pair in edidNames)
        {
            var instanceToken = ExtractInstanceToken(pair.Key);
            if (instanceToken.Length == 0)
            {
                continue;
            }

            if (deviceToken.Contains(instanceToken, StringComparison.OrdinalIgnoreCase) ||
                instanceToken.Contains(deviceToken, StringComparison.OrdinalIgnoreCase))
            {
                MonitorLog.Write($"Matched device '{deviceId}' with EDID entry '{pair.Key}' => '{pair.Value}'.");
                return pair.Value;
            }
        }

        return null;
    }

    private static bool TryResolveConnectionKind(
        IReadOnlyDictionary<string, MonitorConnectionKind> connectionKinds,
        string? deviceId,
        out MonitorConnectionKind connectionKind)
    {
        connectionKind = MonitorConnectionKind.Unknown;

        if (connectionKinds.Count == 0 || string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        var deviceToken = ExtractVendorToken(deviceId);
        if (string.IsNullOrEmpty(deviceToken))
        {
            return false;
        }

        foreach (var pair in connectionKinds)
        {
            var instanceToken = ExtractInstanceToken(pair.Key);
            if (instanceToken.Length == 0)
            {
                continue;
            }

            if (deviceToken.Contains(instanceToken, StringComparison.OrdinalIgnoreCase) ||
                instanceToken.Contains(deviceToken, StringComparison.OrdinalIgnoreCase))
            {
                connectionKind = pair.Value;
                MonitorLog.Write($"Matched device '{deviceId}' with connection entry '{pair.Key}' => '{connectionKind}'.");
                return true;
            }
        }

        return false;
    }

    private static string ExtractVendorToken(string deviceId)
    {
        var parts = deviceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return parts[1];
        }

        return deviceId;
    }

    private static string ExtractInstanceToken(string instanceName)
    {
        var token = instanceName;
        var underscoreIndex = token.IndexOf('_');
        if (underscoreIndex >= 0)
        {
            token = token[..underscoreIndex];
        }

        return token;
    }

    private static MonitorConnectionKind InferConnectionKind(string? deviceId, string? friendlyName)
    {
        static bool Contains(string? source, string value)
            => !string.IsNullOrEmpty(source) && source.Contains(value, StringComparison.OrdinalIgnoreCase);

        if (Contains(deviceId, "DISPLAYPORT") || Contains(friendlyName, "DISPLAYPORT") || Contains(friendlyName, "DP"))
        {
            return MonitorConnectionKind.DisplayPort;
        }

        if (Contains(deviceId, "HDMI") || Contains(friendlyName, "HDMI"))
        {
            return MonitorConnectionKind.Hdmi;
        }

        if (Contains(friendlyName, "USB") || Contains(deviceId, "USB"))
        {
            return MonitorConnectionKind.Usb;
        }

        if (Contains(friendlyName, "INTERNAL") || Contains(deviceId, "INTERNAL"))
        {
            return MonitorConnectionKind.Internal;
        }

        if (Contains(friendlyName, "WIRELESS") || Contains(deviceId, "MIRA") || Contains(friendlyName, "MIRACAST"))
        {
            return MonitorConnectionKind.Wireless;
        }

        if (Contains(friendlyName, "VIRTUAL"))
        {
            return MonitorConnectionKind.Virtual;
        }

        return MonitorConnectionKind.Unknown;
    }

    private static MonitorConnectionKind MapVideoOutputTechnology(uint value)
    {
        // 参考: https://learn.microsoft.com/ja-jp/windows/win32/wmicoreprov/wmimonitorconnectionparams
        // （D3DKMDT_VIDEO_OUTPUT_TECHNOLOGY の定義に基づくマッピング）
        return value switch
        {
            // D3DKMDT_VOT_HDMI (5) に該当
            5u => MonitorConnectionKind.Hdmi,

            // D3DKMDT_VOT_DISPLAYPORT_EXTERNAL (10) / DISPLAYPORT_EMBEDDED (11) に該当
            10u or 11u => MonitorConnectionKind.DisplayPort,

            // D3DKMDT_VOT_INDIRECT_WIRED (16) に該当（DisplayLink や USB-C ドック等）
            16u => MonitorConnectionKind.Usb,

            // D3DKMDT_VOT_LVDS (6) / INTERNAL (0x80000000) に該当
            6u or 0x80000000u => MonitorConnectionKind.Internal,

            // --- 無線系 ----------------------------------------------------------
            // D3DKMDT_VOT_MIRACAST (15) に該当
            15u => MonitorConnectionKind.Wireless,

            // --- その他 ----------------------------------------------------------
            // VGA/S-Video/Composite/Component/DVI など（現代環境では稀なので Unknown 扱い）
            _ => MonitorConnectionKind.Unknown,
        };
    }

    static class MonitorLog
    {
        [Conditional("MONITOR_ENUM_DEBUG")]
        public static void Write(string message)
            => Debug.WriteLine($"[Win32MonitorEnumerator] {message}");
    }

}