using System;

using LGTVSwitcher.Core.Display;

using Xunit;

namespace LGTVSwitcher.Core.Tests;

public sealed class SnapshotEqualityComparerTests
{
    private static readonly MonitorInfo HdmiMonitor = new("dev1", "Primary", true, MonitorConnectionKind.Hdmi);
    private static readonly MonitorInfo DisplayPortMonitor = new("dev2", "Secondary", false, MonitorConnectionKind.DisplayPort);

    [Fact]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var snapshot = CreateSnapshot(preferredOnline: true, edid: "EDID1", monitor: HdmiMonitor);
        var comparer = new SnapshotEqualityComparer();

        Assert.True(comparer.Equals(snapshot, snapshot));
    }

    [Fact]
    public void Equals_NullAndInstance_ReturnsFalse()
    {
        var snapshot = CreateSnapshot(preferredOnline: true, edid: "EDID1", monitor: HdmiMonitor);
        var comparer = new SnapshotEqualityComparer();

        Assert.False(comparer.Equals(snapshot, null));
        Assert.False(comparer.Equals(null, snapshot));
    }

    [Fact]
    public void Equals_DifferentOnlineState_ReturnsFalse()
    {
        var comparer = new SnapshotEqualityComparer();
        var online = CreateSnapshot(preferredOnline: true, edid: "EDID1", monitor: HdmiMonitor);
        var offline = CreateSnapshot(preferredOnline: false, edid: "EDID1", monitor: HdmiMonitor);

        Assert.False(comparer.Equals(online, offline));
    }

    [Fact]
    public void Equals_DifferentEdid_ReturnsFalse()
    {
        var comparer = new SnapshotEqualityComparer();
        var first = CreateSnapshot(preferredOnline: true, edid: "EDID1", monitor: HdmiMonitor);
        var second = CreateSnapshot(preferredOnline: true, edid: "EDID2", monitor: HdmiMonitor);

        Assert.False(comparer.Equals(first, second));
    }

    [Fact]
    public void Equals_DifferentConnectionKind_ReturnsFalse()
    {
        var comparer = new SnapshotEqualityComparer();
        var first = CreateSnapshot(preferredOnline: true, edid: "EDID1", monitor: HdmiMonitor);
        var second = CreateSnapshot(preferredOnline: true, edid: "EDID1", monitor: DisplayPortMonitor);

        Assert.False(comparer.Equals(first, second));
    }

    [Fact]
    public void Equals_DifferentTargetInput_ReturnsFalse_WhenSelectorProvided()
    {
        string? Selector(DisplaySnapshot snapshot) => snapshot.Timestamp.ToUnixTimeMilliseconds().ToString();
        var comparer = new SnapshotEqualityComparer(Selector);
        var first = CreateSnapshot(preferredOnline: true, edid: "EDID1", monitor: HdmiMonitor, timestamp: DateTimeOffset.UnixEpoch.AddSeconds(1));
        var second = CreateSnapshot(preferredOnline: true, edid: "EDID1", monitor: HdmiMonitor, timestamp: DateTimeOffset.UnixEpoch.AddSeconds(2));

        Assert.False(comparer.Equals(first, second));
    }

    [Fact]
    public void Equals_UnknownConnectionAndNullEdid_ReturnsTrue()
    {
        var comparer = new SnapshotEqualityComparer();
        var first = CreateSnapshot(preferredOnline: false, edid: null, monitor: null);
        var second = CreateSnapshot(preferredOnline: false, edid: null, monitor: null);

        Assert.True(comparer.Equals(first, second));
        Assert.Equal(comparer.GetHashCode(first), comparer.GetHashCode(second));
    }

    private static DisplaySnapshot CreateSnapshot(bool preferredOnline, string? edid, MonitorInfo? monitor, DateTimeOffset? timestamp = null)
    {
        var monitors = monitor is null
            ? Array.Empty<MonitorInfo>()
            : new[] { monitor };

        return new DisplaySnapshot(
            timestamp ?? DateTimeOffset.UtcNow,
            monitors,
            monitor,
            preferredOnline,
            edid);
    }
}
