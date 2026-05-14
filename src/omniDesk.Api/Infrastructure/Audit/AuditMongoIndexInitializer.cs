using MongoDB.Driver;
using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace omniDesk.Api.Infrastructure.Audit;

/// <summary>
/// Spec 012 — creates the 3 compound indexes on <c>{tenant_slug}_audit_logs</c> for
/// all active tenants. Called once at app startup. Idempotent.
/// </summary>
public class AuditMongoIndexInitializer(
    IMongoClient mongo,
    AppDbContext db,
    ILogger<AuditMongoIndexInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var tenants = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active)
            .Select(t => t.Slug)
            .ToListAsync(ct);

        foreach (var slug in tenants)
        {
            try
            {
                await EnsureIndexesAsync(slug, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "AuditMongoIndexInitializer: failed to create indexes for tenant {Slug}.", slug);
            }
        }
    }

    private async Task EnsureIndexesAsync(string slug, CancellationToken ct)
    {
        var dbName = $"tenant_{slug.Replace('-', '_')}";
        var collection = mongo.GetDatabase(dbName)
            .GetCollection<AuditLog>("audit_logs");

        var keys = Builders<AuditLog>.IndexKeys;
        var models = new[]
        {
            new CreateIndexModel<AuditLog>(
                keys.Ascending(l => l.TenantSlug).Descending(l => l.Timestamp),
                new CreateIndexOptions { Name = "idx_tenant_timestamp", Background = true }),
            new CreateIndexModel<AuditLog>(
                keys.Ascending(l => l.TenantSlug).Ascending(l => l.Event).Descending(l => l.Timestamp),
                new CreateIndexOptions { Name = "idx_tenant_event_timestamp", Background = true }),
            new CreateIndexModel<AuditLog>(
                keys.Ascending(l => l.TenantSlug).Ascending("actor.user_id").Descending(l => l.Timestamp),
                new CreateIndexOptions { Name = "idx_tenant_actor_timestamp", Background = true }),
        };

        await collection.Indexes.CreateManyAsync(models, ct);
    }
}
