using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using LGTVSwitcher.Core.LgTv;
using LGTVSwitcher.Core.LgWebOs;
using LGTVSwitcher.Core.LgWebOs.Transport;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace LGTVSwitcher.Core.Tests;

public sealed class LgTvSessionTests
{
    [Fact]
    public async Task EnsureConnectedAsync_UsesPreferredDiscoveredTvAndPersistsState()
    {
        var transport = new FakeTransport();
        transport.Enqueue("""{"type":"registered","payload":{"client-key":"new-key"}}""");
        var discovery = new FakeDiscovery(
            new LgTvDiscoveryResult("192.168.0.10", "http://192.168.0.10:3001/desc.xml", "uuid:other", "LG", "st"),
            new LgTvDiscoveryResult("192.168.0.20", "http://tv.local:3001/desc.xml", "uuid:preferred", "LG", "st"));
        var store = new FakeClientKeyStore(new LgTvPersistedState("old-key", "uuid:preferred"));
        var options = Options.Create(new LgTvSwitcherOptions());
        var session = CreateSession(transport, discovery, store, options);

        await session.EnsureConnectedAsync(CancellationToken.None);

        Assert.Equal("tv.local", transport.ConnectUris[0].Host);
        Assert.Equal("new-key", options.Value.ClientKey);
        Assert.Equal("uuid:preferred", options.Value.PreferredTvUsn);
        Assert.Equal("new-key", store.PersistedClientKey);
        Assert.Equal("uuid:preferred", store.PersistedPreferredTvUsn);
    }

    [Fact]
    public async Task EnsureConnectedAsync_PreferredTvMissing_DoesNotFallbackToOtherTv()
    {
        var transport = new FakeTransport();
        var discovery = new FakeDiscovery(
            new LgTvDiscoveryResult("192.168.0.10", "http://192.168.0.10:3001/desc.xml", "uuid:other", "LG", "st"));
        var store = new FakeClientKeyStore(new LgTvPersistedState("old-key", "uuid:preferred"));
        var session = CreateSession(transport, discovery, store, Options.Create(new LgTvSwitcherOptions()));

        await Assert.ThrowsAsync<LgTvRegistrationException>(() => session.EnsureConnectedAsync(CancellationToken.None));

        Assert.Empty(transport.ConnectUris);
    }

    [Fact]
    public async Task EnsureConnectedAsync_NoDiscovery_FallsBackToConfiguredHost()
    {
        var transport = new FakeTransport();
        transport.Enqueue("""{"type":"registered","payload":{"client-key":"key-1"}}""");
        var options = Options.Create(new LgTvSwitcherOptions { TvHost = "configured-tv.local" });
        var session = CreateSession(transport, new FakeDiscovery(), new FakeClientKeyStore(), options);

        await session.EnsureConnectedAsync(CancellationToken.None);

        Assert.Equal("configured-tv.local", transport.ConnectUris[0].Host);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var transport = new FakeTransport();
        var session = CreateSession(transport, new FakeDiscovery(), new FakeClientKeyStore(), Options.Create(new LgTvSwitcherOptions()));

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(1, transport.DisposeCalls);
    }

    [Fact]
    public async Task SendRequestAsync_NotRegisteredError_ReconnectsAndRetries()
    {
        var transport = new FakeTransport();
        transport.Enqueue("""{"type":"registered","payload":{"client-key":"key-1"}}""");
        transport.Enqueue("""{"type":"error","error":"401 not registered"}""");
        transport.Enqueue("""{"type":"registered","payload":{"client-key":"key-1"}}""");
        transport.Enqueue("""{"type":"response","payload":{"returnValue":true,"value":42}}""");
        var discovery = new FakeDiscovery(new LgTvDiscoveryResult("192.168.0.20", "http://192.168.0.20:3001/desc.xml", "uuid:tv", "LG", "st"));
        var session = CreateSession(transport, discovery, new FakeClientKeyStore(), Options.Create(new LgTvSwitcherOptions()));

        var payload = await session.SendRequestAsync("ssap://example/test", null, CancellationToken.None);

        Assert.NotNull(payload);
        Assert.Equal(42, payload.Value.GetProperty("value").GetInt32());
        Assert.Equal(2, transport.ConnectUris.Count);
        Assert.Equal(2, transport.RequestSendCount);
    }

    [Fact]
    public async Task SendRequestAsync_ErrorResponse_ThrowsCommandException()
    {
        var transport = new FakeTransport();
        transport.Enqueue("""{"type":"registered","payload":{"client-key":"key-1"}}""");
        transport.Enqueue("""{"type":"error","error":"500 failed"}""");
        var discovery = new FakeDiscovery(new LgTvDiscoveryResult("192.168.0.20", null, "uuid:tv", "LG", "st"));
        var session = CreateSession(transport, discovery, new FakeClientKeyStore(), Options.Create(new LgTvSwitcherOptions()));

        await Assert.ThrowsAsync<LgTvCommandException>(() => session.SendRequestAsync("ssap://example/test", null, CancellationToken.None));
    }

    [Fact]
    public async Task SendRequestAsync_SerializesConcurrentRequests()
    {
        var transport = new FakeTransport();
        transport.Enqueue("""{"type":"registered","payload":{"client-key":"key-1"}}""");
        transport.Enqueue("""{"type":"response","payload":{"returnValue":true,"value":1}}""");
        transport.Enqueue("""{"type":"response","payload":{"returnValue":true,"value":2}}""");
        var discovery = new FakeDiscovery(new LgTvDiscoveryResult("192.168.0.20", null, "uuid:tv", "LG", "st"));
        var session = CreateSession(transport, discovery, new FakeClientKeyStore(), Options.Create(new LgTvSwitcherOptions()));

        var first = session.SendRequestAsync("ssap://example/one", null, CancellationToken.None);
        var second = session.SendRequestAsync("ssap://example/two", null, CancellationToken.None);
        await Task.WhenAll(first, second);

        Assert.Equal(2, transport.RequestSendCount);
        Assert.Equal(1, transport.MaxConcurrentRequests);
    }

    private static LgTvSession CreateSession(
        ILgTvWebSocketTransport transport,
        ILgTvDiscoveryService discovery,
        ILgTvClientKeyStore store,
        IOptions<LgTvSwitcherOptions> options)
        => new(
            transport,
            options,
            NullLogger<LgTvSession>.Instance,
            new LgTvResponseParser(NullLogger<LgTvResponseParser>.Instance),
            discovery,
            store);

    private sealed class FakeTransport : ILgTvWebSocketTransport
    {
        private readonly Queue<string?> _responses = new();

        public WebSocketState State { get; private set; } = WebSocketState.None;
        public List<Uri> ConnectUris { get; } = new();
        public int RequestSendCount { get; private set; }
        public int DisposeCalls { get; private set; }
        public int MaxConcurrentRequests { get; private set; }
        private int _activeRequests;

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            ConnectUris.Add(uri);
            State = WebSocketState.Open;
            return Task.CompletedTask;
        }

        public Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
            if (json.RootElement.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), "request", StringComparison.OrdinalIgnoreCase))
            {
                RequestSendCount++;
                var active = Interlocked.Increment(ref _activeRequests);
                MaxConcurrentRequests = Math.Max(MaxConcurrentRequests, active);
            }

            return Task.CompletedTask;
        }

        public Task<string?> ReceiveStringAsync(CancellationToken cancellationToken)
        {
            var response = _responses.Dequeue();
            if (_activeRequests > 0)
            {
                Interlocked.Decrement(ref _activeRequests);
            }

            return Task.FromResult(response);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            State = WebSocketState.None;
            return ValueTask.CompletedTask;
        }

        public void Enqueue(string? response) => _responses.Enqueue(response);
    }

    private sealed class FakeDiscovery(params LgTvDiscoveryResult[] results) : ILgTvDiscoveryService
    {
        public Task<IReadOnlyList<LgTvDiscoveryResult>> DiscoverAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LgTvDiscoveryResult>>(results);
    }

    private sealed class FakeClientKeyStore(LgTvPersistedState? initialState = null) : ILgTvClientKeyStore
    {
        public string? PersistedClientKey { get; private set; }
        public string? PersistedPreferredTvUsn { get; private set; }

        public Task<LgTvPersistedState> GetStateAsync(CancellationToken cancellationToken)
            => Task.FromResult(initialState ?? new LgTvPersistedState());

        public Task PersistClientKeyAsync(string clientKey, CancellationToken cancellationToken)
        {
            PersistedClientKey = clientKey;
            return Task.CompletedTask;
        }

        public Task PersistPreferredTvUsnAsync(string preferredTvUsn, CancellationToken cancellationToken)
        {
            PersistedPreferredTvUsn = preferredTvUsn;
            return Task.CompletedTask;
        }
    }
}
