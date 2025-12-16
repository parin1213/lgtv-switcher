#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;

using LGTVSwitcher.Core.Display;

using Microsoft.Extensions.Logging;

using Xunit;

namespace LGTVSwitcher.DisplayDetection.Windows.Tests;

public sealed class WindowsMonitorDetectorTests
{
    [Fact]
    public void ShouldPublish_TracksChangesBySnapshotContent()
    {
        var pump = new WindowsMessagePump();
        var enumerator = new FakeEnumerator();
        var logger = new NullLogger<WindowsMonitorDetector>();
        var detector = new WindowsMonitorDetector(pump, enumerator, logger);

        var snapshot1 = new[]
        {
            new MonitorSnapshot("dev1", "Primary", new MonitorBounds(0, 0, 1920, 1080), true, MonitorConnectionKind.Hdmi, "EDID1")
        };
        var snapshot1Duplicate = new[]
        {
            new MonitorSnapshot("dev1", "Primary", new MonitorBounds(0, 0, 1920, 1080), true, MonitorConnectionKind.Hdmi, "EDID1")
        };
        var snapshot2 = new[]
        {
            new MonitorSnapshot("dev1", "Primary", new MonitorBounds(0, 0, 1920, 1080), true, MonitorConnectionKind.Hdmi, "EDID2")
        };

        Assert.True(InvokeShouldPublish(detector, snapshot1));
        Assert.False(InvokeShouldPublish(detector, snapshot1Duplicate));
        Assert.True(InvokeShouldPublish(detector, snapshot2));

        detector.Dispose();
    }

    private static bool InvokeShouldPublish(WindowsMonitorDetector detector, IReadOnlyList<MonitorSnapshot> snapshot)
    {
        var method = typeof(WindowsMonitorDetector).GetMethod(
            "ShouldPublish",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (bool)method.Invoke(detector, new object[] { snapshot })!;
    }

    private sealed class FakeEnumerator : IMonitorEnumerator
    {
        public IReadOnlyList<MonitorSnapshot> EnumerateCurrentMonitors() => Array.Empty<MonitorSnapshot>();
    }

    private sealed class NullLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
