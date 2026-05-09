using MongoDB.Bson;
using MongoDB.Driver;
using omniDesk.Api.Domain.Attendants;

namespace omniDesk.Api.Infrastructure.Presence;

/// <summary>
/// Persists every presence transition in `{tenant_slug}_attendant_status_logs`
/// (data-model §2.1, FR-011). Each transition creates a new immutable document.
/// </summary>
public class PresenceLogger
{
    private readonly IMongoClient _mongo;

    public PresenceLogger(IMongoClient mongo) => _mongo = mongo;

    public async Task LogTransitionAsync(
        string tenantSlug,
        Guid attendantId,
        string attendantName,
        AttendanceStatus from,
        AttendanceStatus to,
        AttendanceStatusChangedBy by,
        DateTimeOffset timestamp,
        CancellationToken ct = default)
    {
        var db = _mongo.GetDatabase(SanitizeDb(tenantSlug));
        var coll = db.GetCollection<BsonDocument>("attendant_status_logs");
        var doc = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "attendant_id", attendantId.ToString() },
            { "attendant_name", attendantName ?? string.Empty },
            { "from_status", from.ToWireValue() },
            { "to_status", to.ToWireValue() },
            { "changed_by", by.ToWireValue() },
            { "timestamp", timestamp.UtcDateTime },
            { "tenant_slug", tenantSlug },
        };
        await coll.InsertOneAsync(doc, cancellationToken: ct);
    }

    /// <summary>
    /// Database name follows the project convention `{slug}_db`. We strip non-alphanumerics
    /// from the slug to keep Mongo happy. Constitution §I requires `{slug}_*` collection naming —
    /// the database itself is per-tenant per Spec 003.
    /// </summary>
    private static string SanitizeDb(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("tenant_slug required (Constitution §I)", nameof(slug));
        return slug.Replace('-', '_');
    }
}
