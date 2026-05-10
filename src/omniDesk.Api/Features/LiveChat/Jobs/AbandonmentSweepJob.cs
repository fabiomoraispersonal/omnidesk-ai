using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Hubs.Events;
using omniDesk.Api.Infrastructure.LiveChat;
using omniDesk.Api.Infrastructure.Persistence;
using StackExchange.Redis;

namespace omniDesk.Api.Features.LiveChat.Jobs;

/// <summary>
/// Spec 007 FR-022 / R9 — closes AI-owned conversations that have been idle past the
/// per-tenant <c>widget_config.abandonment_timeout_hours</c> (default 8h). Runs hourly.
/// Conversations owned by an attendant are handled by <see cref="InactivitySweepJob"/>.
/// </summary>
public class AbandonmentSweepJob
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<AbandonmentSweepJob> _logger;

    public AbandonmentSweepJob(
        AppDbContext db,
        IConnectionMultiplexer redis,
        ILogger<AbandonmentSweepJob> logger)
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
                _logger.LogWarning(ex, "AbandonmentSweepJob failed for tenant {Slug}", tenant.Slug);
            }
        }
    }

    private async Task SweepTenantAsync(Tenant tenant, CancellationToken ct)
    {
        var schema = tenant.SchemaName;
        var sql = $$"""
            WITH cfg AS (
                SELECT abandonment_timeout_hours FROM "{{schema}}".widget_config LIMIT 1
            )
            UPDATE "{{schema}}".conversations c
               SET status = 'abandoned',
                   ended_at = now(),
                   updated_at = now()
              FROM cfg
             WHERE c.status = 'open'
               AND c.attendant_id IS NULL
               AND c.last_message_at < now() - make_interval(hours => cfg.abandonment_timeout_hours)
            RETURNING c.id;
            """;

        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

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
                    status = "abandoned",
                    at = DateTimeOffset.UtcNow,
                },
            }, JsonOpts);
            await sub.PublishAsync(
                RedisChannel.Literal(RedisChannelNames.Conversation(tenant.Slug, convId)),
                envelope);
        }

        _logger.LogInformation(
            "AbandonmentSweepJob marked {Count} conversation(s) abandoned for tenant {Slug}",
            ids.Count, tenant.Slug);
    }
}
