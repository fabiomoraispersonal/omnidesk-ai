using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Authorization;
using omniDesk.Api.Infrastructure.LiveChat;
using StackExchange.Redis;

namespace omniDesk.Api.Hubs;

/// <summary>
/// Spec 007 US3 — attendant WebSocket. Mounted at <c>/ws/crm</c>; auth via the standard
/// JWT pipeline (the route is wired with <c>RequireAuthorization()</c>). Subscribes to
/// the attendant's personal channel <c>{slug}:crm:user:{userId}</c> and to every
/// department channel they're a member of (read from <c>dept_id</c> claims). Inbound
/// frames are limited to typing/read indicators; sending a message uses the REST
/// endpoint instead so the same path is exercised in tests and from the UI.
/// </summary>
public class CrmWebSocketEndpoint
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ActiveTicketTtl = TimeSpan.FromSeconds(60);

    private readonly WebSocketBroker _broker;
    private readonly ITenantSlugAccessor _slug;
    private readonly IConnectionMultiplexer _redis;
    private readonly IAttendantRepository _attendants;
    private readonly ILogger<CrmWebSocketEndpoint> _logger;

    public CrmWebSocketEndpoint(
        WebSocketBroker broker,
        ITenantSlugAccessor slug,
        IConnectionMultiplexer redis,
        IAttendantRepository attendants,
        ILogger<CrmWebSocketEndpoint> logger)
    {
        _broker = broker;
        _slug = slug;
        _redis = redis;
        _attendants = attendants;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext http, CancellationToken ct)
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

        var sub = http.User.FindFirst("sub")?.Value
                  ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(sub, out var userId))
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var deptIds = new List<Guid>();
        foreach (var c in http.User.FindAll("dept_id"))
            if (Guid.TryParse(c.Value, out var d)) deptIds.Add(d);

        var ws = await http.WebSockets.AcceptWebSocketAsync();

        var slug = _slug.Slug;
        var personalChannel = RedisChannelNames.CrmUser(slug, userId);
        var subscriptions = new List<IAsyncDisposable> { await _broker.SubscribeAsync(personalChannel, ws, ct) };
        foreach (var d in deptIds)
            subscriptions.Add(await _broker.SubscribeAsync(RedisChannelNames.CrmDepartment(slug, d), ws, ct));

        // Spec 010 US2 silence-rule support: resolve attendant id once for the lifetime of the
        // socket so we can write {slug}:attendant_active_ticket:{attendantId} when the client
        // signals which ticket it's currently viewing.
        Guid? attendantId = null;
        try
        {
            var att = await _attendants.GetByUserIdAsync(userId, ct);
            attendantId = att?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "CRM WS: attendant lookup failed for user {UserId}; silence-rule disabled for this socket.",
                userId);
        }

        try
        {
            using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = Task.Run(async () =>
            {
                while (!pingCts.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    try { await Task.Delay(PingInterval, pingCts.Token); } catch { return; }
                    await SendAsync(ws, new { type = "ping", ts = DateTimeOffset.UtcNow }, pingCts.Token);
                }
            }, pingCts.Token);

            await ReadLoopAsync(ws, slug, attendantId, ct);
            pingCts.Cancel();
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            _logger.LogInformation(ex, "CRM WebSocket closed unexpectedly for user {UserId}", userId);
        }
        finally
        {
            // Spec 010 US2 — clear active-ticket flag on disconnect so a freshly attached
            // socket on a different tab does not inherit a stale silence-rule lock.
            if (attendantId.HasValue)
            {
                try
                {
                    await _redis.GetDatabase().KeyDeleteAsync(
                        RedisKeys.AttendantActiveTicket(_slug.Slug, attendantId.Value));
                }
                catch { /* swallow */ }
            }

            foreach (var s in subscriptions) await s.DisposeAsync();
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch { /* swallow */ }
            }
        }
    }

    private async Task ReadLoopAsync(WebSocket ws, string slug, Guid? attendantId, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            ms.Position = 0;
            try
            {
                using var doc = await JsonDocument.ParseAsync(ms, default, ct);
                var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;

                switch (type)
                {
                    case "pong":
                        break;

                    // Spec 010 US2 T058 — silence-rule heartbeat. Body: { ticket_id: "<uuid>" | null }.
                    case NotificationEvents.AttendantViewingTicket:
                        if (attendantId is null) break;
                        await HandleViewingTicketAsync(slug, attendantId.Value, doc.RootElement, ct);
                        break;

                    default:
                        _logger.LogDebug("CRM WS received unhandled type {Type}", type);
                        break;
                }
            }
            catch (JsonException)
            {
                await SendAsync(ws, new { type = "error", error = new { code = "INVALID_JSON" } }, ct);
            }
        }
    }

    private async Task HandleViewingTicketAsync(
        string slug, Guid attendantId, JsonElement root, CancellationToken ct)
    {
        var key = RedisKeys.AttendantActiveTicket(slug, attendantId);
        var db = _redis.GetDatabase();

        // If ticket_id absent / null / empty → DEL (attendant left the detail page).
        if (!root.TryGetProperty("ticket_id", out var tProp)
            || tProp.ValueKind == JsonValueKind.Null
            || tProp.ValueKind == JsonValueKind.Undefined)
        {
            try { await db.KeyDeleteAsync(key); } catch { /* swallow */ }
            return;
        }

        var ticketStr = tProp.ValueKind == JsonValueKind.String ? tProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(ticketStr) || !Guid.TryParse(ticketStr, out var ticketId))
        {
            try { await db.KeyDeleteAsync(key); } catch { /* swallow */ }
            return;
        }

        try
        {
            await db.StringSetAsync(key, ticketId.ToString(), ActiveTicketTtl);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "CRM WS: failed to set active-ticket flag for attendant {AttId}.", attendantId);
        }
    }

    private static async Task SendAsync(WebSocket ws, object payload, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOpts));
        try { await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct); }
        catch { /* socket closing */ }
    }
}
