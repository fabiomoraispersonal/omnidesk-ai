using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Features.Notifications;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Authorization;
using omniDesk.Api.Infrastructure.Persistence;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.Jobs;

/// <summary>
/// Spec 010 US3 T069 — recurring cron job (every minute).
/// Scans tickets stuck in <c>status='new' AND attendant_id IS NULL</c> for ≥ 5 minutes
/// (FR-009 — fixed, non-configurable threshold). For each such ticket, attempts a Redis
/// <c>SET NX</c> on <c>{slug}:queue_alert:{ticket_id}</c> with TTL 1h; if it wins the race,
/// fans out a <c>ticket.queued</c> notification to all supervisors of the department.
///
/// Idempotency: Redis NX flag prevents re-notification within the TTL window.
/// </summary>
public class TicketQueueMonitorJob(
    AppDbContext db,
    IConnectionMultiplexer redis,
    INotificationService notifications,
    TenantContextHolder tenantContext,
    IConfiguration config,
    ILogger<TicketQueueMonitorJob> logger)
{
    private const int IdempotencyTtlSeconds = 3_600; // 1h — research §R11.

    public async Task RunAsync(CancellationToken ct = default)
    {
        var thresholdMin = config.GetValue<int>("Notifications:QueueAlertThresholdMinutes", 5);

        var tenants = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active)
            .ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            try
            {
                await ProcessTenantAsync(tenant, thresholdMin, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "QueueMonitor: failed for tenant {Slug}.", tenant.Slug);
            }
        }
    }

    private async Task ProcessTenantAsync(Tenant tenant, int thresholdMin, CancellationToken ct)
    {
        tenantContext.Set(tenant.Slug, tenant.Id);

        var rows = await LoadStaleQueueRowsAsync(tenant.SchemaName, thresholdMin, ct);
        if (rows.Count == 0) return;

        var redisDb = redis.GetDatabase();

        foreach (var row in rows)
        {
            var key = RedisKeys.NotificationQueueAlert(tenant.Slug, row.Id);
            var isNew = await redisDb.StringSetAsync(
                key, "1", TimeSpan.FromSeconds(IdempotencyTtlSeconds), When.NotExists);

            if (!isNew) continue; // Already notified this hour.

            if (row.Protocol is null) continue; // Defensive; tickets without protocol are pre-Spec009.

            try
            {
                await notifications.NotifyTicketQueuedAsync(
                    row.Id, row.Protocol, row.DepartmentId, ct);
                logger.LogInformation(
                    "QueueMonitor: alerted supervisors for ticket {Protocol} (tenant {Slug}).",
                    row.Protocol, tenant.Slug);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "QueueMonitor: notify failed for ticket {Id} (tenant {Slug}).",
                    row.Id, tenant.Slug);
            }
        }
    }

    private async Task<List<QueueRow>> LoadStaleQueueRowsAsync(
        string schema, int thresholdMin, CancellationToken ct)
    {
        var sql = $"""
            SELECT id, department_id, protocol, created_at
            FROM "{schema}".tickets
            WHERE status = 'new'
              AND attendant_id IS NULL
              AND deleted_at IS NULL
              AND created_at <= NOW() - INTERVAL '{thresholdMin} minutes'
            """;

        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync(ct);

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            var rows = new List<QueueRow>();
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new QueueRow
                {
                    Id           = reader.GetGuid(0),
                    DepartmentId = reader.GetGuid(1),
                    Protocol     = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CreatedAt    = reader.GetFieldValue<DateTimeOffset>(3),
                });
            }
            return rows;
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }

    private sealed class QueueRow
    {
        public Guid Id { get; set; }
        public Guid DepartmentId { get; set; }
        public string? Protocol { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
