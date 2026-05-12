using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Tickets;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WebSockets;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.Jobs;

/// <summary>
/// Spec 009 T115 — recurring cron job (every minute).
/// Scans active tickets across all tenants, emits SLA warning/breach events idempotently.
/// Idempotency: Redis SET NX flags with 24h TTL prevent duplicate events per cycle.
/// </summary>
public class TicketSlaMonitorJob(
    AppDbContext db,
    IConnectionMultiplexer redis,
    ITicketEventStore eventStore,
    TicketEventPublisher publisher,
    IConfiguration config,
    ILogger<TicketSlaMonitorJob> logger)
{
    private const int IdempotencyTtlSeconds = 86_400; // 24h

    public async Task RunAsync(CancellationToken ct = default)
    {
        var thresholdPct = config.GetValue<double>("Tickets:SlaWarningThresholdPercent", 80) / 100.0;
        var now = DateTimeOffset.UtcNow;

        var tenants = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active)
            .ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            try
            {
                await ProcessTenantAsync(tenant, thresholdPct, now, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SlaMonitor: failed for tenant {Slug}.", tenant.Slug);
            }
        }
    }

    private async Task ProcessTenantAsync(
        Tenant tenant,
        double thresholdPct,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var schema = tenant.SchemaName;
        var rows = await LoadActiveTicketsAsync(schema, ct);

        if (rows.Count == 0) return;

        var redisDb = redis.GetDatabase();

        foreach (var row in rows)
        {
            await CheckSlaTypeAsync(
                tenant, row, "first_response",
                row.SlaFirstResponseDeadline,
                row.FirstResponseAt.HasValue,   // already responded = skip first_response SLA
                thresholdPct, now, redisDb, ct);

            await CheckSlaTypeAsync(
                tenant, row, "resolution",
                row.SlaResolutionDeadline,
                resolved: false,                // resolution SLA always active until terminal
                thresholdPct, now, redisDb, ct);
        }
    }

    private async Task CheckSlaTypeAsync(
        Tenant tenant,
        TicketSlaRow row,
        string slaType,
        DateTimeOffset? baseDeadline,
        bool resolved,
        double thresholdPct,
        DateTimeOffset now,
        IDatabase redisDb,
        CancellationToken ct)
    {
        if (baseDeadline is null || resolved) return;

        var pct = SlaPauseCalculator.PercentConsumed(
            row.CreatedAt, baseDeadline.Value,
            row.SlaPausedDurationMinutes, row.WaitingClientSince, now);

        var warningKey = $"{tenant.Slug}:ticket:{row.Id}:sla_warned:{slaType}";
        var breachKey  = $"{tenant.Slug}:ticket:{row.Id}:sla_breached:{slaType}";

        if (pct >= 1.0)
        {
            var isNew = await redisDb.StringSetAsync(
                breachKey, "1", TimeSpan.FromSeconds(IdempotencyTtlSeconds), When.NotExists);

            if (isNew)
            {
                await EmitBreachAsync(tenant, row, slaType, now, ct);
                logger.LogInformation(
                    "SlaMonitor: breach {Type} ticket {Protocol} tenant {Slug}.",
                    slaType, row.Protocol, tenant.Slug);
            }
        }
        else if (pct >= thresholdPct)
        {
            var isNew = await redisDb.StringSetAsync(
                warningKey, "1", TimeSpan.FromSeconds(IdempotencyTtlSeconds), When.NotExists);

            if (isNew)
            {
                await EmitWarningAsync(tenant, row, slaType, pct, ct);
                logger.LogInformation(
                    "SlaMonitor: warning {Type} ({Pct:P0}) ticket {Protocol} tenant {Slug}.",
                    slaType, pct, row.Protocol, tenant.Slug);
            }
        }
    }

    private async Task EmitWarningAsync(
        Tenant tenant,
        TicketSlaRow row,
        string slaType,
        double pct,
        CancellationToken ct)
    {
        var payload = new
        {
            ticket_id  = row.Id,
            protocol   = row.Protocol,
            sla_type   = slaType,
            percent    = Math.Round(pct * 100, 1),
        };

        try
        {
            await publisher.PublishSlaWarningAsync(tenant.Slug, row.DepartmentId, payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SlaMonitor: WS publish warning failed for ticket {Id}.", row.Id);
        }
    }

    private async Task EmitBreachAsync(
        Tenant tenant,
        TicketSlaRow row,
        string slaType,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var payload = new
        {
            ticket_id = row.Id,
            protocol  = row.Protocol,
            sla_type  = slaType,
        };

        // Persist breach to Mongo (audit)
        try
        {
            var ev = new TicketEvent(
                TenantSlug: tenant.Slug,
                TicketId:   row.Id,
                Protocol:   row.Protocol,
                EventType:  TicketEventType.SlaBreached,
                ActorType:  "system",
                Timestamp:  now)
            {
                SlaType = slaType,
            };
            await eventStore.AppendAsync(ev, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SlaMonitor: Mongo breach event failed for ticket {Id}.", row.Id);
        }

        // Publish WS breach event
        try
        {
            await publisher.PublishSlaBreachedAsync(tenant.Slug, row.DepartmentId, payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SlaMonitor: WS publish breach failed for ticket {Id}.", row.Id);
        }
    }

    private async Task<List<TicketSlaRow>> LoadActiveTicketsAsync(string schema, CancellationToken ct)
    {
        var sql = $"""
            SELECT id,
                   department_id,
                   protocol,
                   created_at,
                   sla_first_response_deadline,
                   sla_resolution_deadline,
                   sla_paused_duration_minutes,
                   waiting_client_since,
                   first_response_at
            FROM "{schema}".tickets
            WHERE status NOT IN ('resolved', 'cancelled')
              AND deleted_at IS NULL
              AND (sla_first_response_deadline IS NOT NULL
                OR sla_resolution_deadline IS NOT NULL)
            """;

        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync(ct);

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            var rows = new List<TicketSlaRow>();
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new TicketSlaRow
                {
                    Id                        = reader.GetGuid(0),
                    DepartmentId              = reader.GetGuid(1),
                    Protocol                  = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CreatedAt                 = reader.GetFieldValue<DateTimeOffset>(3),
                    SlaFirstResponseDeadline  = reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                    SlaResolutionDeadline     = reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                    SlaPausedDurationMinutes  = reader.GetInt32(6),
                    WaitingClientSince        = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                    FirstResponseAt           = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                });
            }
            return rows;
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }

    private sealed class TicketSlaRow
    {
        public Guid Id                               { get; set; }
        public Guid DepartmentId                     { get; set; }
        public string? Protocol                      { get; set; }
        public DateTimeOffset CreatedAt              { get; set; }
        public DateTimeOffset? SlaFirstResponseDeadline  { get; set; }
        public DateTimeOffset? SlaResolutionDeadline     { get; set; }
        public int SlaPausedDurationMinutes          { get; set; }
        public DateTimeOffset? WaitingClientSince    { get; set; }
        public DateTimeOffset? FirstResponseAt       { get; set; }
    }
}
