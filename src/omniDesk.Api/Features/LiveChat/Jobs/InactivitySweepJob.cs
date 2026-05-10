using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Infrastructure.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;
using StackExchange.Redis;

namespace omniDesk.Api.Features.LiveChat.Jobs;

/// <summary>
/// Spec 007 FR-023 — closes attendant-owned conversations that have been idle past the
/// per-tenant <c>widget_config.inactivity_close_hours</c> (default 24h). Marks as
/// <c>resolved</c> with <c>ended_by=system_inactivity</c>, inserts a system message,
/// and publishes <c>conversation.resolved</c> on both widget and CRM channels.
/// </summary>
public class InactivitySweepJob
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<InactivitySweepJob> _logger;

    public InactivitySweepJob(
        AppDbContext db,
        IConnectionMultiplexer redis,
        ILogger<InactivitySweepJob> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    [Queue("ai-incoming")]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var tenants = await _db.Tenants.AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active)
            .ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            try
            {
                await SweepTenantAsync(tenant, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InactivitySweepJob failed for tenant {Slug}", tenant.Slug);
            }
        }
    }

    private async Task SweepTenantAsync(Tenant tenant, CancellationToken ct)
    {
        var schema = tenant.SchemaName;
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

        var systemEvent = MessageContentType.SystemEvent.ToWire();
        var senderSystem = MessageSenderType.System.ToWire();
        var endedBy = EndedBy.SystemInactivity.ToWire();

        var sql = $$"""
            WITH cfg AS (
                SELECT inactivity_close_hours FROM "{{schema}}".widget_config LIMIT 1
            ),
            updated AS (
                UPDATE "{{schema}}".conversations c
                   SET status = 'resolved',
                       ended_by = '{{endedBy}}',
                       ended_at = now(),
                       updated_at = now()
                  FROM cfg
                 WHERE c.status = 'open'
                   AND c.attendant_id IS NOT NULL
                   AND c.last_message_at < now() - make_interval(hours => cfg.inactivity_close_hours)
                RETURNING c.id, c.attendant_id
            ),
            log AS (
                INSERT INTO "{{schema}}".messages (id, conversation_id, sender_type, content_type, content, created_at)
                SELECT gen_random_uuid(), id, '{{senderSystem}}', '{{systemEvent}}', 'inactivity_timeout', now()
                  FROM updated
            )
            SELECT id, attendant_id FROM updated;
            """;

        var rows = new List<(Guid Id, Guid AttendantId)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add((reader.GetGuid(0), reader.GetGuid(1)));
        }

        if (rows.Count == 0) return;

        var sub = _redis.GetSubscriber();
        foreach (var row in rows)
        {
            var widgetEnvelope = JsonSerializer.Serialize(new
            {
                type = WidgetEvents.ConversationResolved,
                payload = new
                {
                    conversation_id = row.Id,
                    ended_by = endedBy,
                    status = "resolved",
                    at = DateTimeOffset.UtcNow,
                },
            }, JsonOpts);
            await sub.PublishAsync(
                RedisChannel.Literal(RedisChannelNames.Conversation(tenant.Slug, row.Id)),
                widgetEnvelope);

            var crmEnvelope = JsonSerializer.Serialize(new
            {
                type = CrmEvents.ChatConversationResolved,
                payload = new
                {
                    conversation_id = row.Id,
                    ended_by = endedBy,
                    at = DateTimeOffset.UtcNow,
                },
            }, JsonOpts);
            await sub.PublishAsync(
                RedisChannel.Literal(RedisChannelNames.CrmUser(tenant.Slug, row.AttendantId)),
                crmEnvelope);
        }

        _logger.LogInformation(
            "InactivitySweepJob marked {Count} conversation(s) inactive for tenant {Slug}",
            rows.Count, tenant.Slug);
    }
}
