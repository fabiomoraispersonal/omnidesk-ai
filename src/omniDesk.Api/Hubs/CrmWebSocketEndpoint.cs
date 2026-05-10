using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.LiveChat;

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

    private readonly WebSocketBroker _broker;
    private readonly ITenantSlugAccessor _slug;
    private readonly ILogger<CrmWebSocketEndpoint> _logger;

    public CrmWebSocketEndpoint(
        WebSocketBroker broker,
        ITenantSlugAccessor slug,
        ILogger<CrmWebSocketEndpoint> logger)
    {
        _broker = broker;
        _slug = slug;
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

            await ReadLoopAsync(ws, ct);
            pingCts.Cancel();
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            _logger.LogInformation(ex, "CRM WebSocket closed unexpectedly for user {UserId}", userId);
        }
        finally
        {
            foreach (var s in subscriptions) await s.DisposeAsync();
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch { /* swallow */ }
            }
        }
    }

    private async Task ReadLoopAsync(WebSocket ws, CancellationToken ct)
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
                // V1 inbound frames are limited to pong; everything else is best-effort logged.
                if (!string.Equals(type, "pong", StringComparison.Ordinal))
                    _logger.LogDebug("CRM WS received unhandled type {Type}", type);
            }
            catch (JsonException)
            {
                await SendAsync(ws, new { type = "error", error = new { code = "INVALID_JSON" } }, ct);
            }
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
