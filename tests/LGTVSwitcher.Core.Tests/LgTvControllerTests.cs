using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using LGTVSwitcher.Core.LgWebOs;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace LGTVSwitcher.Core.Tests;

public sealed class LgTvControllerTests
{
    [Fact]
    public async Task EnsureConnectedAsync_DelegatesToSession()
    {
        var session = new FakeSession();
        var controller = CreateController(session);

        var result = await controller.EnsureConnectedAsync(CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, session.EnsureConnectedCalls);
    }

    [Fact]
    public async Task SwitchInputAsync_SendsSwitchInputRequest()
    {
        var session = new FakeSession();
        var controller = CreateController(session);

        await controller.SwitchInputAsync("HDMI_4", CancellationToken.None);

        Assert.Equal("ssap://tv/switchInput", session.LastUri);
        var json = JsonSerializer.Serialize(session.LastPayload);
        Assert.Contains("\"inputId\":\"HDMI_4\"", json);
    }

    [Fact]
    public async Task SwitchInputAsync_EmptyInput_ThrowsArgumentException()
    {
        var controller = CreateController(new FakeSession());

        await Assert.ThrowsAsync<ArgumentException>(() => controller.SwitchInputAsync(" ", CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentInputAsync_ParsesForegroundAppPayload()
    {
        var session = new FakeSession
        {
            Response = JsonDocument.Parse("""{"returnValue":true,"appId":"com.webos.app.hdmi2"}""").RootElement.Clone()
        };
        var controller = CreateController(session);

        var input = await controller.GetCurrentInputAsync(CancellationToken.None);

        Assert.Equal("HDMI_2", input);
        Assert.Equal("ssap://com.webos.applicationManager/getForegroundAppInfo", session.LastUri);
        Assert.Null(session.LastPayload);
    }

    [Fact]
    public async Task DisposeAsync_DisposesSession()
    {
        var session = new FakeSession();
        var controller = CreateController(session);

        await controller.DisposeAsync();

        Assert.True(session.Disposed);
    }

    private static LgTvController CreateController(FakeSession session)
        => new(
            session,
            new LgTvResponseParser(NullLogger<LgTvResponseParser>.Instance),
            NullLogger<LgTvController>.Instance);

    private sealed class FakeSession : ILgTvSession
    {
        public int EnsureConnectedCalls { get; private set; }
        public string? LastUri { get; private set; }
        public object? LastPayload { get; private set; }
        public JsonElement? Response { get; set; }
        public bool Disposed { get; private set; }

        public Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            EnsureConnectedCalls++;
            return Task.CompletedTask;
        }

        public Task<JsonElement?> SendRequestAsync(string uri, object? payload, CancellationToken cancellationToken)
        {
            LastUri = uri;
            LastPayload = payload;
            return Task.FromResult(Response);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
