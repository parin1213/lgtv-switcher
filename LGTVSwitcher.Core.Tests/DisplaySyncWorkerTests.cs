using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using LGTVSwitcher.Core.Display;
using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.Core.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LGTVSwitcher.Core.Tests;

public class DisplaySyncWorkerTests
{
    [Fact]
    public async Task OnlineSnapshot_ShouldSwitchToTargetInput()
    {
        var provider = new FakeSnapshotProvider();
        var controller = new FakeLgTvController();
        var options = Options.Create(new LgTvSwitcherOptions
        {
            TargetInputId = "HDMI_4",
            FallbackInputId = "HDMI_2",
            PreferredMonitorName = "TEST",
        });

        using var worker = new DisplaySyncWorker(provider, controller, options, NullLogger<DisplaySyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        provider.Publish(new DisplaySnapshotNotification(CreateSnapshot(online: true), "test-online"));

        await Task.Delay(1200); // debounce 800ms + margin

        Assert.Single(controller.SwitchCalls, call => call == "HDMI_4");

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StaleSnapshot_ShouldBeIgnored()
    {
        var provider = new FakeSnapshotProvider();
        var controller = new FakeLgTvController();
        var options = Options.Create(new LgTvSwitcherOptions
        {
            TargetInputId = "HDMI_4",
            FallbackInputId = "HDMI_2",
            PreferredMonitorName = "TEST",
        });

        using var worker = new DisplaySyncWorker(provider, controller, options, NullLogger<DisplaySyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        provider.Publish(new DisplaySnapshotNotification(CreateSnapshot(online: true, ageSeconds: 6), "stale"));

        await Task.Delay(1200);

        Assert.Empty(controller.SwitchCalls);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OfflineSnapshot_ShouldSwitchToFallbackInput()
    {
        var provider = new FakeSnapshotProvider();
        var controller = new FakeLgTvController();
        var options = Options.Create(new LgTvSwitcherOptions
        {
            TargetInputId = "HDMI_4",
            FallbackInputId = "HDMI_2",
            PreferredMonitorName = "TEST",
        });

        using var worker = new DisplaySyncWorker(provider, controller, options, NullLogger<DisplaySyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        provider.Publish(new DisplaySnapshotNotification(CreateSnapshot(online: false), "test-offline"));

        await Task.Delay(1200);

        Assert.Single(controller.SwitchCalls, call => call == "HDMI_2");

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AlreadyOnTargetInput_ShouldNotCallSwitch()
    {
        var provider = new FakeSnapshotProvider();
        var controller = new FakeLgTvController { CurrentInput = "HDMI_4" };
        var options = Options.Create(new LgTvSwitcherOptions
        {
            TargetInputId = "HDMI_4",
            FallbackInputId = "HDMI_2",
            PreferredMonitorName = "TEST",
        });

        using var worker = new DisplaySyncWorker(provider, controller, options, NullLogger<DisplaySyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        provider.Publish(new DisplaySnapshotNotification(CreateSnapshot(online: true), "test-redundant"));

        await Task.Delay(1200);

        Assert.Empty(controller.SwitchCalls);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MissingEdidKey_ShouldBeIgnored()
    {
        var provider = new FakeSnapshotProvider();
        var controller = new FakeLgTvController();
        var options = Options.Create(new LgTvSwitcherOptions
        {
            TargetInputId = "HDMI_4",
            FallbackInputId = "HDMI_2",
            PreferredMonitorName = "TEST",
        });

        using var worker = new DisplaySyncWorker(provider, controller, options, NullLogger<DisplaySyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        provider.Publish(new DisplaySnapshotNotification(CreateSnapshot(online: true, edidKey: null), "missing-edid"));

        await Task.Delay(1200);

        Assert.Empty(controller.SwitchCalls);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task UnknownConnection_ShouldBeIgnored()
    {
        var provider = new FakeSnapshotProvider();
        var controller = new FakeLgTvController();
        var options = Options.Create(new LgTvSwitcherOptions
        {
            TargetInputId = "HDMI_4",
            FallbackInputId = "HDMI_2",
            PreferredMonitorName = "TEST",
        });

        using var worker = new DisplaySyncWorker(provider, controller, options, NullLogger<DisplaySyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        provider.Publish(new DisplaySnapshotNotification(
            CreateSnapshot(online: true, connection: MonitorConnectionKind.Unknown), "unknown-connection"));

        await Task.Delay(1200);

        Assert.Empty(controller.SwitchCalls);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DuplicateSnapshots_ShouldSwitchOnlyOnce()
    {
        var provider = new FakeSnapshotProvider();
        var controller = new FakeLgTvController();
        var options = Options.Create(new LgTvSwitcherOptions
        {
            TargetInputId = "HDMI_4",
            FallbackInputId = "HDMI_2",
            PreferredMonitorName = "TEST",
        });

        using var worker = new DisplaySyncWorker(provider, controller, options, NullLogger<DisplaySyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        var snapshot = CreateSnapshot(online: true);
        provider.Publish(new DisplaySnapshotNotification(snapshot, "dup-1"));
        provider.Publish(new DisplaySnapshotNotification(snapshot, "dup-2"));

        await Task.Delay(1200);

        Assert.Single(controller.SwitchCalls, call => call == "HDMI_4");

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task QueryTransportError_ShouldSkipSwitch()
    {
        var provider = new FakeSnapshotProvider();
        var controller = new FakeLgTvController
        {
            QueryException = new WebSocketException()
        };
        var options = Options.Create(new LgTvSwitcherOptions
        {
            TargetInputId = "HDMI_4",
            FallbackInputId = "HDMI_2",
            PreferredMonitorName = "TEST",
        });

        using var worker = new DisplaySyncWorker(provider, controller, options, NullLogger<DisplaySyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        provider.Publish(new DisplaySnapshotNotification(CreateSnapshot(online: true), "query-ex"));

        await Task.Delay(1200);

        Assert.Empty(controller.SwitchCalls);

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SwitchTransportError_ShouldNotStopPipeline()
    {
        var provider = new FakeSnapshotProvider();
        var controller = new FakeLgTvController
        {
            SwitchException = new WebSocketException()
        };
        var options = Options.Create(new LgTvSwitcherOptions
        {
            TargetInputId = "HDMI_4",
            FallbackInputId = "HDMI_2",
            PreferredMonitorName = "TEST",
        });

        using var worker = new DisplaySyncWorker(provider, controller, options, NullLogger<DisplaySyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        provider.Publish(new DisplaySnapshotNotification(CreateSnapshot(online: true), "switch-ex"));
        await Task.Delay(900);
        controller.SwitchException = null;
        provider.Publish(new DisplaySnapshotNotification(CreateSnapshot(online: false, ageSeconds: 0), "switch-ok"));

        await Task.Delay(1500);

        // 1 回目は例外でスキップ、2 回目（オフライン通知）は成功して 1 件だけ記録される想定
        Assert.Single(controller.SwitchCalls, call => call == "HDMI_2");

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_ShouldInvokeProviderStart()
    {
        var provider = new FakeSnapshotProvider();
        var controller = new FakeLgTvController();
        var options = Options.Create(new LgTvSwitcherOptions
        {
            TargetInputId = "HDMI_4",
            FallbackInputId = "HDMI_2",
            PreferredMonitorName = "TEST",
        });

        using var worker = new DisplaySyncWorker(provider, controller, options, NullLogger<DisplaySyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        Assert.True(provider.Started);

        await worker.StopAsync(CancellationToken.None);
    }

    private static DisplaySnapshot CreateSnapshot(bool online, int ageSeconds = 0, string? edidKey = "EDID-TEST", MonitorConnectionKind connection = MonitorConnectionKind.Hdmi)
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(-ageSeconds);
        var monitor = new MonitorInfo("DEV1", "TEST", true, connection);
        return new DisplaySnapshot(
            timestamp: now,
            monitors: new[] { monitor },
            preferredMonitor: online ? monitor : null,
            preferredMonitorOnline: online,
            preferredMonitorEdidKey: edidKey);
    }

    private sealed class FakeSnapshotProvider : IDisplaySnapshotProvider
    {
        private readonly Subject<DisplaySnapshotNotification> _subject = new();
        public bool Started { get; private set; }

        public IObservable<DisplaySnapshotNotification> Notifications => _subject;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public void Publish(DisplaySnapshotNotification notification)
            => _subject.OnNext(notification);
    }

    private sealed class FakeLgTvController : ILgTvController
    {
        public List<string> SwitchCalls { get; } = new();
        public string? CurrentInput { get; set; }
        public Exception? QueryException { get; set; }
        public Exception? SwitchException { get; set; }

        public Task<string?> EnsureConnectedAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<string?> GetCurrentInputAsync(CancellationToken cancellationToken)
        {
            if (QueryException is not null)
            {
                throw QueryException;
            }

            return Task.FromResult(CurrentInput);
        }

        public Task SwitchInputAsync(string inputId, CancellationToken cancellationToken)
        {
            if (SwitchException is not null)
            {
                throw SwitchException;
            }

            SwitchCalls.Add(inputId);
            return Task.CompletedTask;
        }
    }
}
