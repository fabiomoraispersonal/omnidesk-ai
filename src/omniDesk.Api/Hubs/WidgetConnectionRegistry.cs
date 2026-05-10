using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace omniDesk.Api.Hubs;

/// <summary>
/// Spec 007 — per-process registry of live <see cref="WebSocket"/> connections grouped by
/// pub/sub channel. Used in tandem with <see cref="WebSocketBroker"/>: when a Redis message
/// lands on channel X, the broker iterates the registry to deliver it to local sockets.
///
/// One channel typically maps to a single visitor (channel = <c>{slug}:conv:{id}</c>) or
/// to a single CRM user (channel = <c>{slug}:crm:user:{id}</c>), but multiple sockets per
/// channel are supported (e.g. visitor opens two browser tabs).
/// </summary>
public class WidgetConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, ConnectionEntry>> _byChannel = new();

    public Guid Register(string channel, WebSocket socket)
    {
        var id = Guid.NewGuid();
        var bucket = _byChannel.GetOrAdd(channel, _ => new ConcurrentDictionary<Guid, ConnectionEntry>());
        bucket[id] = new ConnectionEntry(socket, new SemaphoreSlim(1, 1));
        return id;
    }

    public void Unregister(string channel, Guid connectionId)
    {
        if (!_byChannel.TryGetValue(channel, out var bucket)) return;
        if (bucket.TryRemove(connectionId, out var entry))
            entry.SendLock.Dispose();
        if (bucket.IsEmpty) _byChannel.TryRemove(channel, out _);
    }

    public int LocalSubscriberCount(string channel)
        => _byChannel.TryGetValue(channel, out var bucket) ? bucket.Count : 0;

    public async Task BroadcastLocalAsync(string channel, string json, CancellationToken ct)
    {
        if (!_byChannel.TryGetValue(channel, out var bucket)) return;
        var bytes = Encoding.UTF8.GetBytes(json);

        foreach (var (id, entry) in bucket)
        {
            if (entry.Socket.State != WebSocketState.Open) continue;
            try
            {
                await entry.SendLock.WaitAsync(ct);
                try
                {
                    await entry.Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
                }
                finally
                {
                    entry.SendLock.Release();
                }
            }
            catch (Exception)
            {
                // Drop dead sockets silently — handler loop will tear down on its own.
                bucket.TryRemove(id, out _);
            }
        }
    }

    private sealed record ConnectionEntry(WebSocket Socket, SemaphoreSlim SendLock);
}
