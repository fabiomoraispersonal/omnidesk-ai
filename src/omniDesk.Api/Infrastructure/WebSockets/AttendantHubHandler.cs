using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Infrastructure.Authorization;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.WebSockets;

/// <summary>
/// Native WebSocket handler (Constitution ADR-005 — no SignalR).
/// Validates JWT claims for each subscribed channel:
///  - `tenant`            requires tenant_admin or supervisor
///  - `dept:{id}`         requires the caller is a member of the dept OR supervisor
///  - `attendant:self`    always allowed
///  - `attendant:{other}` blocked (403 closes the connection)
/// </summary>
public class AttendantHubHandler
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<AttendantHubHandler> _logger;

    public AttendantHubHandler(IConnectionMultiplexer redis, ILogger<AttendantHubHandler> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context, CancellationToken ct)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (context.User?.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var tenantSlug = context.User.FindFirst("tenant_slug")?.Value;
        if (string.IsNullOrWhiteSpace(tenantSlug))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var ws = await context.WebSockets.AcceptWebSocketAsync();
        var sub = _redis.GetSubscriber();
        var subscribedChannels = new List<string>();
        var sendLock = new SemaphoreSlim(1, 1);

        try
        {
            var buffer = new byte[8 * 1024];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var receive = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (receive.MessageType == WebSocketMessageType.Close) break;
                if (receive.MessageType != WebSocketMessageType.Text) continue;

                var msg = Encoding.UTF8.GetString(buffer, 0, receive.Count);
                SubscribeMessage? parsed;
                try { parsed = JsonSerializer.Deserialize<SubscribeMessage>(msg); }
                catch (JsonException) { continue; }

                if (parsed?.type != "subscribe" || parsed.channels is null) continue;

                foreach (var channelKey in parsed.channels)
                {
                    if (!TryResolveChannel(channelKey, context.User, tenantSlug, out var redisChannel, out var error))
                    {
                        await SendAsync(ws, sendLock, new { type = "error", channel = channelKey, error }, ct);
                        continue;
                    }

                    if (subscribedChannels.Contains(redisChannel)) continue;
                    subscribedChannels.Add(redisChannel);

                    await sub.SubscribeAsync(RedisChannel.Literal(redisChannel), async (_, value) =>
                    {
                        if (ws.State != WebSocketState.Open) return;
                        try
                        {
                            await sendLock.WaitAsync();
                            var bytes = Encoding.UTF8.GetBytes(value!);
                            await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
                        }
                        catch (Exception ex) { _logger.LogWarning(ex, "WebSocket send failed"); }
                        finally { sendLock.Release(); }
                    });

                    await SendAsync(ws, sendLock, new { type = "subscribed", channel = channelKey }, ct);
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (WebSocketException ex) { _logger.LogInformation(ex, "WebSocket closed unexpectedly"); }
        finally
        {
            foreach (var ch in subscribedChannels)
                await sub.UnsubscribeAsync(RedisChannel.Literal(ch));
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
    }

    private static bool TryResolveChannel(
        string channelKey,
        ClaimsPrincipal user,
        string tenantSlug,
        out string redisChannel,
        out string? error)
    {
        redisChannel = string.Empty;
        error = null;

        var role = Roles.Normalize(user.FindFirst("role")?.Value);
        var sub = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(sub, out var userId);

        if (channelKey == "tenant")
        {
            if (role is Roles.TenantAdmin or Roles.Supervisor or Roles.SaasAdmin)
            {
                redisChannel = RedisKeys.WsTenant(tenantSlug);
                return true;
            }
            error = "Channel `tenant` requires tenant_admin/supervisor.";
            return false;
        }

        if (channelKey.StartsWith("dept:", StringComparison.Ordinal))
        {
            if (!Guid.TryParse(channelKey.AsSpan("dept:".Length), out var deptId))
            { error = "Invalid department id."; return false; }

            // Authoritative membership lives in the JWT's dept_ids claim (populated by Spec 004 ClaimsTransformer).
            var deptIds = user.FindFirst("dept_ids")?.Value ?? string.Empty;
            var supervisor = role is Roles.Supervisor or Roles.TenantAdmin;
            var member = deptIds.Split(',').Any(s => Guid.TryParse(s.Trim(), out var g) && g == deptId);
            if (supervisor || member)
            {
                redisChannel = RedisKeys.WsDepartment(tenantSlug, deptId);
                return true;
            }
            error = "Caller is not a member of this department.";
            return false;
        }

        if (channelKey == "attendant:self")
        {
            if (userId == Guid.Empty) { error = "Cannot resolve user id from token."; return false; }
            redisChannel = RedisKeys.WsAttendant(tenantSlug, userId);
            return true;
        }

        if (channelKey.StartsWith("attendant:", StringComparison.Ordinal))
        {
            error = "Subscribing to other attendants is not allowed.";
            return false;
        }

        error = "Unknown channel.";
        return false;
    }

    private static async Task SendAsync(WebSocket ws, SemaphoreSlim sendLock, object obj, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(obj);
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            await sendLock.WaitAsync(ct);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally { sendLock.Release(); }
    }

    private sealed record SubscribeMessage(string? type, string[]? channels);
}
