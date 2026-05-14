using MongoDB.Driver;
using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace omniDesk.Api.Infrastructure.Jobs;

/// <summary>
/// Spec 012 — monthly job (1st of each month, 2 AM UTC) that deletes audit log documents
/// older than 12 months. Runs per-tenant. Idempotent.
/// </summary>
public class AuditRetentionJob(
    IMongoClient mongo,
    AppDbContext db,
    ILogger<AuditRetentionJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-12);

        var tenants = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active)
            .Select(t => new { t.Slug })
            .ToListAsync(ct);

        long totalDeleted = 0;
        foreach (var tenant in tenants)
        {
            try
            {
                var deleted = await DeleteForTenantAsync(tenant.Slug, cutoff, ct);
                if (deleted > 0)
                {
                    logger.LogInformation(
                        "AuditRetentionJob: deleted {Count} logs older than 12 months for tenant {Slug}.",
                        deleted, tenant.Slug);
                }
                totalDeleted += deleted;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "AuditRetentionJob: failed for tenant {Slug}.", tenant.Slug);
            }
        }

        if (totalDeleted > 0)
            logger.LogInformation("AuditRetentionJob: total deleted this run = {Total}.", totalDeleted);
    }

    private async Task<long> DeleteForTenantAsync(string slug, DateTime cutoff, CancellationToken ct)
    {
        var dbName = $"tenant_{slug.Replace('-', '_')}";
        var collection = mongo.GetDatabase(dbName).GetCollection<AuditLog>("audit_logs");
        var filter = Builders<AuditLog>.Filter.Lt(l => l.Timestamp, cutoff);
        var result = await collection.DeleteManyAsync(filter, ct);
        return result.DeletedCount;
    }
}
