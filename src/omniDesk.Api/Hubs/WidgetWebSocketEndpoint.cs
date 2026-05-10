using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Features.LiveChat.Public;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Hubs.Handlers;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Hubs;

/// <summary>
/// Spec 007 — visitor WebSocket endpoint mounted at <c>/ws/widget/{conversation_id}</c>.
///
/// Handshake (in order, fail-fast):
///  1. WidgetToken via <c>?token=</c> query — resolved by <see cref="WidgetTokenAuthHandler"/>
///     populating <c>HttpContext.User</c>. Failure ⇒ close 4401 INVALID_WIDGET_TOKEN.
///  2. Origin header against <c>widget_config.allowed_domains</c>. Failure ⇒ 4403 ORIGIN_NOT_ALLOWED.
///  3. Conversation belongs to this tenant. Failure ⇒ 4404 CONVERSATION_NOT_FOUND.
///  4. Conversation status open AND lgpd_consent_at not null. Failure ⇒ 4409 / 4422.
///
/// Runtime:
///  - Subscribes to Redis channel <c>{slug}:conv:{conv_id}</c> via <see cref="WebSocketBroker"/>.
///  - Reads JSON envelopes; dispatches by <c>type</c> to per-event handlers.
///  - Sends ping every 30s; closes 4408 IDLE_TIMEOUT if no pong in 60s (T076).
/// </summary>
public class WidgetWebSocketEndpoint
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PongTimeout = TimeSpan.FromSeconds(60);

    private readonly WebSocketBroker _broker;
    private readonly AppDbContext _db;
    private readonly ITenantSlugAccessor _slug;
    private readonly MessageSendHandler _messageSend;
    private readonly VisitorTypingHandler _visitorTyping;
    private readonly MessagesReadHandler _messagesRead;
    private readonly MessagesReplayHandler _messagesReplay;
    private readonly ILogger<WidgetWebSocketEndpoint> _logger;

    public WidgetWebSocketEndpoint(
        WebSocketBroker broker,
        AppDbContext db,
        ITenantSlugAccessor slug,
        MessageSendHandler messageSend,
        VisitorTypingHandler visitorTyping,
        MessagesReadHandler messagesRead,
        MessagesReplayHandler messagesReplay,
        ILogger<WidgetWebSocketEndpoint> logger)
    {
        _broker = broker;
        _db = db;
        _slug = slug;
        _messageSend = messageSend;
        _visitorTyping = visitorTyping;
        _messagesRead = messagesRead;
        _messagesReplay = messagesReplay;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext http, Guid conversationId, CancellationToken ct)
    {
        if (!http.WebSockets.IsWebSocketRequest)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (http.User?.Identity?.IsAuthenticated != true)
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var slugClaim = http.User.FindFirst(WidgetTokenAuthHandler.TenantSlugClaim)?.Value;
        var tenantIdClaim = http.User.FindFirst(WidgetTokenAuthHandler.TenantIdClaim)?.Value;
        if (string.IsNullOrEmpty(slugClaim) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var (handshakeError, conversation) = await ValidateHandshakeAsync(http, tenantId, conversationId, ct);

        var ws = await http.WebSockets.AcceptWebSocketAsync();

        if (handshakeError is not null)
        {
            await CloseAsync(ws, handshakeError.Value.Code, handshakeError.Value.Reason);
            return;
        }

        var channel = RedisChannelNames.Conversation(slugClaim, conversation!.Id);
        await using var subscription = await _broker.SubscribeAsync(channel, ws, ct);

        await PumpAsync(ws, conversation!.Id, ct);
    }

    private async Task<(HandshakeError? Error, Conversation? Conversation)> ValidateHandshakeAsync(
        HttpContext http,
        Guid tenantId,
        Guid conversationId,
        CancellationToken ct)
    {
        var conversation = await _db.Conversations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conversation is null)
            return (new HandshakeError(4404, "CONVERSATION_NOT_FOUND"), null);

        var visitor = await _db.Visitors.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == conversation.VisitorId, ct);
        if (visitor is null)
            return (new HandshakeError(4404, "VISITOR_NOT_FOUND"), null);

        var widgetConfig = await _db.WidgetConfigs.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .Select(c => new { c.AllowedDomains, c.IsEnabled })
            .FirstOrDefaultAsync(ct);
        if (widgetConfig is null || !widgetConfig.IsEnabled)
            return (new HandshakeError(4409, "WIDGET_DISABLED"), null);

        if (widgetConfig.AllowedDomains is { Count: > 0 } allowed)
        {
            var origin = http.Request.Headers.Origin.ToString();
            if (string.IsNullOrEmpty(origin) || !IsOriginAllowed(origin, allowed))
                return (new HandshakeError(4403, "ORIGIN_NOT_ALLOWED"), null);
        }

        if (conversation.Status != ConversationStatus.Open)
            return (new HandshakeError(4409, "CONVERSATION_CLOSED"), null);
        if (conversation.LgpdConsentAt is null)
            return (new HandshakeError(4422, "LGPD_CONSENT_REQUIRED"), null);

        return (null, conversation);
    }

    private async Task PumpAsync(WebSocket ws, Guid conversationId, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var lastPong = DateTimeOffset.UtcNow;
        using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var pingTask = Task.Run(async () =>
        {
            while (!pingCts.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                try { await Task.Delay(PingInterval, pingCts.Token); }
                catch (TaskCanceledException) { return; }

                if (DateTimeOffset.UtcNow - lastPong > PongTimeout)
                {
                    await CloseAsync(ws, 4408, "IDLE_TIMEOUT");
                    return;
                }

                await SendAsync(ws, new { type = WidgetEvents.Ping, ts = DateTimeOffset.UtcNow }, pingCts.Token);
            }
        }, pingCts.Token);

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        pingCts.Cancel();
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                ms.Position = 0;
                JsonDocument? doc;
                try { doc = await JsonDocument.ParseAsync(ms, default, ct); }
                catch (JsonException)
                {
                    await SendAsync(ws, new { type = "error", error = new { code = "INVALID_JSON" } }, ct);
                    continue;
                }

                using (doc)
                {
                    var root = doc.RootElement;
                    var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                    var payload = root.TryGetProperty("payload", out var p) ? p : default;

                    switch (type)
                    {
                        case WidgetEvents.MessageSend:
                        {
                            var res = await _messageSend.HandleAsync(conversationId, payload, ct);
                            if (res.IsSuccess)
                                await SendAsync(ws, new { type = "message.send.ack", payload = res.Payload }, ct);
                            else
                                await SendAsync(ws, new { type = "error", error = new { code = res.ErrorMessage } }, ct);
                            break;
                        }
                        case WidgetEvents.VisitorTyping:
                        {
                            await _visitorTyping.HandleAsync(conversationId, ct);
                            break;
                        }
                        case WidgetEvents.MessagesRead:
                        {
                            var n = await _messagesRead.HandleAsync(conversationId, ct);
                            await SendAsync(ws, new { type = "messages.read.ack", payload = new { updated = n } }, ct);
                            break;
                        }
                        case WidgetEvents.MessagesReplay:
                        {
                            Guid? since = null;
                            if (payload.ValueKind == JsonValueKind.Object
                                && payload.TryGetProperty("since_message_id", out var sEl)
                                && Guid.TryParse(sEl.GetString(), out var s))
                                since = s;
                            var msgs = await _messagesReplay.HandleAsync(conversationId, since, ct);
                            foreach (var m in msgs)
                            {
                                await SendAsync(ws, new
                                {
                                    type = WidgetEvents.MessageNew,
                                    payload = new
                                    {
                                        message_id = m.Id,
                                        conversation_id = conversationId,
                                        sender_type = m.SenderType,
                                        sender_id = m.SenderId,
                                        content_type = m.ContentType,
                                        content = m.Content,
                                        attachment_url = m.AttachmentUrl,
                                        created_at = m.CreatedAt,
                                    },
                                }, ct);
                            }
                            await SendAsync(ws, new { type = "messages.replay.done", payload = new { count = msgs.Count } }, ct);
                            break;
                        }
                        case WidgetEvents.Pong:
                        {
                            lastPong = DateTimeOffset.UtcNow;
                            break;
                        }
                        default:
                            await SendAsync(ws, new { type = "error", error = new { code = "UNKNOWN_EVENT", received = type } }, ct);
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            _logger.LogInformation(ex, "WidgetWebSocket closed unexpectedly for conv {ConvId}", conversationId);
        }
        finally
        {
            pingCts.Cancel();
            try { await pingTask; } catch { /* already cancelled */ }
            if (ws.State == WebSocketState.Open)
                await CloseAsync(ws, (ushort)WebSocketCloseStatus.NormalClosure, "bye");
        }
    }

    private static bool IsOriginAllowed(string origin, IReadOnlyList<string> allowed)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        foreach (var entry in allowed)
            if (string.Equals(entry, uri.Host, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static async Task SendAsync(WebSocket ws, object payload, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOpts));
        await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static async Task CloseAsync(WebSocket ws, ushort code, string reason)
    {
        if (ws.State != WebSocketState.Open) return;
        var status = (WebSocketCloseStatus)code;
        try { await ws.CloseAsync(status, reason, CancellationToken.None); }
        catch { /* swallow */ }
    }

    private record HandshakeError(ushort Code, string Reason);
}
