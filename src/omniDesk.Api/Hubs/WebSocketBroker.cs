using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;

namespace omniDesk.Api.Hubs;

/// <summary>
/// Spec 007 — facade that bridges Redis Pub/Sub with local <see cref="WidgetConnectionRegistry"/>.
///
/// Two responsibilities:
///   1. <see cref="PublishAsync"/> — fan out an event to a channel (other API nodes pick it up).
///   2. <see cref="SubscribeAsync"/> — register a local socket for a channel; lazily creates
///      one Redis subscriber per channel (refcounted) that forwards each incoming message to
///      every local socket in the registry.
///
/// Messages are JSON envelopes: <c>{ type, payload, timestamp, conversation_id? }</c>.
/// </summary>
public class WebSocketBroker
{
    private readonly IConnectionMultiplexer _redis;
    private readonly WidgetConnectionRegistry _registry;
    private readonly ILogger<WebSocketBroker> _logger;
    private readonly ConcurrentDictionary<string, int> _refCounts = new();
    private readonly ConcurrentDictionary<string, Action<RedisChannel, RedisValue>> _handlers = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public WebSocketBroker(
        IConnectionMultiplexer redis,
        WidgetConnectionRegistry registry,
        ILogger<WebSocketBroker> logger)
    {
        _redis = redis;
        _registry = registry;
        _logger = logger;
    }

    public async Task PublishAsync(string channel, string eventType, object payload, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = eventType,
            payload,
            timestamp = DateTimeOffset.UtcNow,
        }, JsonOpts);

        await _redis.GetSubscriber().PublishAsync(RedisChannel.Literal(channel), json);
        ct.ThrowIfCancellationRequested();
    }

    public async Task<IAsyncDisposable> SubscribeAsync(
        string channel,
        System.Net.WebSockets.WebSocket socket,
        CancellationToken ct)
    {
        var connectionId = _registry.Register(channel, socket);
        await EnsureRedisHandlerAsync(channel);
        return new Subscription(this, channel, connectionId);
    }

    private async Task EnsureRedisHandlerAsync(string channel)
    {
        if (_refCounts.AddOrUpdate(channel, 1, (_, n) => n + 1) > 1) return;

        var sub = _redis.GetSubscriber();
        Action<RedisChannel, RedisValue> handler = (redisChannel, value) =>
        {
            var payload = (string?)value;
            if (string.IsNullOrEmpty(payload)) return;
            _ = _registry.BroadcastLocalAsync(channel, payload, CancellationToken.None);
        };
        _handlers[channel] = handler;
        await sub.SubscribeAsync(RedisChannel.Literal(channel), handler);
    }

    private async Task ReleaseRedisHandlerAsync(string channel)
    {
        if (!_refCounts.TryGetValue(channel, out var current)) return;
        if (current > 1)
        {
            _refCounts[channel] = current - 1;
            return;
        }
        _refCounts.TryRemove(channel, out _);
        if (_handlers.TryRemove(channel, out var handler))
        {
            try { await _redis.GetSubscriber().UnsubscribeAsync(RedisChannel.Literal(channel), handler); }
            catch (Exception ex) { _logger.LogWarning(ex, "Redis unsubscribe failed for {Channel}", channel); }
        }
    }

    private sealed class Subscription : IAsyncDisposable
    {
        private readonly WebSocketBroker _broker;
        private readonly string _channel;
        private readonly Guid _connectionId;
        private bool _disposed;

        public Subscription(WebSocketBroker broker, string channel, Guid connectionId)
        {
            _broker = broker;
            _channel = channel;
            _connectionId = connectionId;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _broker._registry.Unregister(_channel, _connectionId);
            await _broker.ReleaseRedisHandlerAsync(_channel);
        }
    }
}
