using MongoDB.Bson;
using MongoDB.Driver;
using omniDesk.Api.Domain.Audit;

namespace omniDesk.Api.Infrastructure.Audit;

/// <summary>
/// Spec 012 — append-only repository for <see cref="AuditLog"/> documents.
/// Collection: <c>{tenant_slug}_audit_logs</c>.
/// </summary>
public class AuditMongoRepository(IMongoClient mongo)
{
    private IMongoCollection<AuditLog> Collection(string tenantSlug)
    {
        var dbName = $"tenant_{tenantSlug.Replace('-', '_')}";
        return mongo.GetDatabase(dbName).GetCollection<AuditLog>("audit_logs");
    }

    public Task InsertAsync(AuditLog log, CancellationToken ct) =>
        Collection(log.TenantSlug).InsertOneAsync(log, cancellationToken: ct);

    public async Task<(IReadOnlyList<AuditLog> Items, long Total)> QueryAsync(
        string tenantSlug,
        string? eventFilter,
        Guid? actorId,
        DateTime? from,
        DateTime? to,
        int page,
        int perPage,
        CancellationToken ct)
    {
        var collection = Collection(tenantSlug);
        var builder = Builders<AuditLog>.Filter;
        var filter = builder.Eq(l => l.TenantSlug, tenantSlug);

        if (!string.IsNullOrWhiteSpace(eventFilter))
            filter &= builder.Eq(l => l.Event, eventFilter);

        if (actorId.HasValue)
            filter &= builder.Eq(l => l.Actor.UserId, actorId.Value);

        if (from.HasValue)
            filter &= builder.Gte(l => l.Timestamp, from.Value.ToUniversalTime());

        if (to.HasValue)
        {
            var toEnd = to.Value.Date.AddDays(1).ToUniversalTime();
            filter &= builder.Lt(l => l.Timestamp, toEnd);
        }

        var total = await collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var items = await collection
            .Find(filter)
            .Sort(Builders<AuditLog>.Sort.Descending(l => l.Timestamp))
            .Skip((page - 1) * perPage)
            .Limit(perPage)
            .ToListAsync(ct);

        return (items, total);
    }
}
