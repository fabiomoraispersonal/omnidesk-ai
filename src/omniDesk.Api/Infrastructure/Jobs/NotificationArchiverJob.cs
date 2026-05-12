using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Jobs;

/// <summary>
/// Spec 010 Polish T098 — daily cron job (3 AM UTC by default) that soft-deletes
/// notifications older than <c>Notifications:ArchiveRetentionDays</c> (90 days default).
/// "Archive" = set <c>archived_at = NOW()</c>; rows are excluded from the feed but
/// preserved on disk per Constitution §IV (no physical deletes in V1).
///
/// Idempotent: running twice in the same day is a no-op (no rows match the filter
/// after the first run). FR-007.
/// </summary>
public class NotificationArchiverJob(
    AppDbContext db,
    IConfiguration config,
    ILogger<NotificationArchiverJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var retentionDays = config.GetValue<int>("Notifications:ArchiveRetentionDays", 90);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        var tenants = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active)
            .Select(t => new { t.Slug, t.SchemaName })
            .ToListAsync(ct);

        int totalArchived = 0;
        foreach (var tenant in tenants)
        {
            try
            {
                var rows = await ArchiveForSchemaAsync(tenant.SchemaName, cutoff, ct);
                if (rows > 0)
                {
                    logger.LogInformation(
                        "NotificationArchiver: archived {Rows} rows older than {Days}d for tenant {Slug}.",
                        rows, retentionDays, tenant.Slug);
                }
                totalArchived += rows;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "NotificationArchiver: failed for tenant {Slug}.", tenant.Slug);
            }
        }

        if (totalArchived > 0)
        {
            logger.LogInformation(
                "NotificationArchiver: total archived this run = {Total}.", totalArchived);
        }
    }

    private async Task<int> ArchiveForSchemaAsync(
        string schema, DateTimeOffset cutoff, CancellationToken ct)
    {
        // Use parameterized raw SQL because the schema name is dynamic per tenant.
        // The cutoff is parameterized (no injection risk); the schema is taken from
        // public.tenants which is operator-controlled.
        var sql = $"""
            UPDATE "{schema}".notifications
               SET archived_at = NOW()
             WHERE archived_at IS NULL
               AND created_at < @cutoff
            """;

        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync(ct);

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var p = cmd.CreateParameter();
            p.ParameterName = "@cutoff";
            p.Value = cutoff;
            cmd.Parameters.Add(p);
            return await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }
}
