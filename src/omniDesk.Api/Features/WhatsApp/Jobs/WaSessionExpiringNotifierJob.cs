using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Infrastructure.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WhatsApp;
using StackExchange.Redis;

namespace omniDesk.Api.Features.WhatsApp.Jobs;

/// <summary>
/// Spec 008 FR-022/FR-023 — emite eventos WS quando a janela de 24h da Meta
/// está prestes a expirar (≤ 1h restante) ou já expirou.
///
/// Cron <c>*/5 * * * *</c> (a cada 5 minutos — research R5). Idempotente via flags
/// Redis (<c>WaExpiringEmitted</c> TTL 1h; <c>WaExpiredEmitted</c> TTL 24h) — não
/// reemite eventos para a mesma conversa em janelas consecutivas.
///
/// Itera sobre todos os tenants ativos. Per-tenant sweep query barata (índice parcial
/// <c>idx_conversations_wa_session_expiring</c>).
/// </summary>
public sealed class WaSessionExpiringNotifierJob
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const int ExpiringThresholdMinutes = 60;

    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeProvider _clock;
    private readonly ILogger<WaSessionExpiringNotifierJob> _logger;

    public WaSessionExpiringNotifierJob(
        AppDbContext db,
        IConnectionMultiplexer redis,
        TimeProvider clock,
        ILogger<WaSessionExpiringNotifierJob> logger)
    {
        _db = db;
        _redis = redis;
        _clock = clock;
        _logger = logger;
    }

    [Queue("wa-session-sweep")]
    public async Task RunAsync(CancellationToken ct)
    {
        var tenants = await _db.Tenants.AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active)
            .Select(t => new { t.Slug })
            .ToListAsync(ct);

        var emitted = 0;
        foreach (var tenant in tenants)
        {
            try
            {
                emitted += await SweepTenantAsync(tenant.Slug, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WaSessionExpiringNotifier: sweep failed for tenant {Slug}.", tenant.Slug);
            }
        }

        if (emitted > 0)
            _logger.LogInformation("WaSessionExpiringNotifier: emitted {Count} events.", emitted);
    }

    private async Task<int> SweepTenantAsync(string slug, CancellationToken ct)
    {
        // Index parcial idx_conversations_wa_session_expiring filtra
        // (channel=whatsapp AND status=open) — sweep barato mesmo em volume.
        var now = _clock.GetUtcNow();
        var soonThreshold = now.AddMinutes(ExpiringThresholdMinutes);

        var candidates = await _db.Conversations
            .Where(c => c.Channel == ChannelType.WhatsApp
                     && c.Status == ConversationStatus.Open
                     && c.WaSessionExpiresAt != null)
            .Where(c => c.WaSessionExpiresAt!.Value <= soonThreshold)
            .Select(c => new
            {
                c.Id,
                c.WaSessionExpiresAt,
                c.AttendantId,
                c.DepartmentId,
            })
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        var redisDb = _redis.GetDatabase();
        var sub = _redis.GetSubscriber();
        var emitted = 0;

        foreach (var conv in candidates)
        {
            var expiresAt = conv.WaSessionExpiresAt!.Value;
            var isExpired = expiresAt <= now;

            if (isExpired)
            {
                var flagKey = RedisKeys.WaExpiredEmitted(slug, conv.Id);
                var firstTime = await redisDb.StringSetAsync(
                    flagKey, "1", TimeSpan.FromHours(24), When.NotExists);
                if (!firstTime) continue;

                var payload = JsonSerializer.Serialize(new
                {
                    type = WhatsAppCrmEvents.WaSessionExpired,
                    payload = new
                    {
                        conversation_id = conv.Id,
                        expired_at = expiresAt,
                    },
                }, JsonOpts);

                await PublishToCrmAsync(sub, slug, conv.AttendantId, conv.DepartmentId, payload);
                emitted++;
            }
            else
            {
                var flagKey = RedisKeys.WaExpiringEmitted(slug, conv.Id);
                var firstTime = await redisDb.StringSetAsync(
                    flagKey, "1", TimeSpan.FromHours(1), When.NotExists);
                if (!firstTime) continue;

                var minutesRemaining = (int)Math.Max(1, (expiresAt - now).TotalMinutes);
                var payload = JsonSerializer.Serialize(new
                {
                    type = WhatsAppCrmEvents.WaSessionExpiring,
                    payload = new
                    {
                        conversation_id = conv.Id,
                        expires_at = expiresAt,
                        minutes_remaining = minutesRemaining,
                    },
                }, JsonOpts);

                await PublishToCrmAsync(sub, slug, conv.AttendantId, conv.DepartmentId, payload);
                emitted++;
            }
        }

        return emitted;
    }

    private static async Task PublishToCrmAsync(
        ISubscriber sub,
        string slug,
        Guid? attendantId,
        Guid? departmentId,
        string payload)
    {
        if (attendantId is { } att)
        {
            await sub.PublishAsync(
                RedisChannel.Literal(RedisChannelNames.CrmUser(slug, att)),
                payload);
        }

        if (departmentId is { } dept)
        {
            await sub.PublishAsync(
                RedisChannel.Literal(RedisChannelNames.CrmDepartment(slug, dept)),
                payload);
        }
    }
}
