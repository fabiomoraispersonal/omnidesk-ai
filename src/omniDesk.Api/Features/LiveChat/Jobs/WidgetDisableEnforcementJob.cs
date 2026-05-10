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
/// Spec 007 FR-013 — when an admin disables the widget, every <c>open</c> conversation
/// closes with <c>ended_by=system_disable</c>. A system_event message is appended for
/// the audit trail and a <c>conversation.resolved</c> event publishes on the visitor's
/// channel so any live widget tabs receive an immediate close.
/// </summary>
public class WidgetDisableEnforcementJob
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<WidgetDisableEnforcementJob> _logger;

    public WidgetDisableEnforcementJob(
        AppDbContext db,
        IConnectionMultiplexer redis,
        ILogger<WidgetDisableEnforcementJob> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    [Queue("ai-incoming")]
    public async Task RunAsync(string tenantSlug, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == tenantSlug, ct);
        if (tenant is null)
        {
            _logger.LogWarning("WidgetDisableEnforcementJob: tenant {Slug} not found", tenantSlug);
            return;
        }

        await SweepAsync(tenant, ct);
    }

    private async Task SweepAsync(Tenant tenant, CancellationToken ct)
    {
        var schema = tenant.SchemaName;
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

        var endedBy = EndedBy.SystemDisable.ToWire();
        var senderSystem = MessageSenderType.System.ToWire();
        var systemEvent = MessageContentType.SystemEvent.ToWire();

        var sql = $$"""
            WITH updated AS (
                UPDATE "{{schema}}".conversations
                   SET status = 'resolved',
                       ended_by = '{{endedBy}}',
                       ended_at = now(),
                       updated_at = now()
                 WHERE status = 'open'
                RETURNING id
            ),
            log AS (
                INSERT INTO "{{schema}}".messages (id, conversation_id, sender_type, content_type, content, created_at)
                SELECT gen_random_uuid(), id, '{{senderSystem}}', '{{systemEvent}}', 'widget_disabled', now()
                  FROM updated
            )
            SELECT id FROM updated;
            """;

        var ids = new List<Guid>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        }

        if (ids.Count == 0) return;

        var sub = _redis.GetSubscriber();
        foreach (var convId in ids)
        {
            var envelope = JsonSerializer.Serialize(new
            {
                type = WidgetEvents.ConversationResolved,
                payload = new
                {
                    conversation_id = convId,
                    ended_by = endedBy,
                    status = "resolved",
                    at = DateTimeOffset.UtcNow,
                },
            }, JsonOpts);
            await sub.PublishAsync(
                RedisChannel.Literal(RedisChannelNames.Conversation(tenant.Slug, convId)),
                envelope);
        }

        _logger.LogInformation(
            "WidgetDisableEnforcementJob closed {Count} open conversation(s) for tenant {Slug}",
            ids.Count, tenant.Slug);
    }
}
