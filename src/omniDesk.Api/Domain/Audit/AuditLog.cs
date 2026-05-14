using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace omniDesk.Api.Domain.Audit;

/// <summary>
/// Spec 012 — Mongo document for one auditable event per tenant.
/// Collection: <c>{tenant_slug}_audit_logs</c>. Append-only — never updated or deleted.
/// </summary>
public class AuditLog
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("tenant_slug")]
    public string TenantSlug { get; set; } = string.Empty;

    [BsonElement("tenant_id")]
    public Guid TenantId { get; set; }

    [BsonElement("event")]
    public string Event { get; set; } = string.Empty;

    [BsonElement("actor")]
    public AuditActor Actor { get; set; } = null!;

    [BsonElement("target")]
    [BsonIgnoreIfNull]
    public AuditTarget? Target { get; set; }

    [BsonElement("metadata")]
    [BsonIgnoreIfNull]
    public BsonDocument? Metadata { get; set; }

    [BsonElement("ip_address")]
    [BsonIgnoreIfNull]
    public string? IpAddress { get; set; }

    [BsonElement("user_agent")]
    [BsonIgnoreIfNull]
    public string? UserAgent { get; set; }

    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; }
}

public class AuditActor
{
    [BsonElement("user_id")]
    [BsonIgnoreIfNull]
    public Guid? UserId { get; set; }

    [BsonElement("name")]
    [BsonIgnoreIfNull]
    public string? Name { get; set; }

    [BsonElement("role")]
    public string Role { get; set; } = string.Empty;

    [BsonElement("impersonated_by")]
    [BsonIgnoreIfNull]
    public string? ImpersonatedBy { get; set; }
}

public class AuditTarget
{
    [BsonElement("entity_type")]
    public string EntityType { get; set; } = string.Empty;

    [BsonElement("entity_id")]
    public Guid EntityId { get; set; }

    [BsonElement("label")]
    [BsonIgnoreIfNull]
    public string? Label { get; set; }
}
