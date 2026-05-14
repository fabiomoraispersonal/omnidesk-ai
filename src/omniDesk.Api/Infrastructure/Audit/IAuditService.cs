using MongoDB.Bson;
using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Infrastructure.Authentication;

namespace omniDesk.Api.Infrastructure.Audit;

public interface IAuditService
{
    /// <summary>Enqueues an audit log entry. Never throws — degrades gracefully on failure.</summary>
    void Log(
        string tenantSlug,
        Guid tenantId,
        string eventName,
        AuditActor actor,
        AuditTarget? target = null,
        object? metadata = null,
        string? ipAddress = null,
        string? userAgent = null);
}

public static class AuditActorFactory
{
    public static AuditActor FromCurrentUser(ICurrentUser user, string? name = null) => new()
    {
        UserId = user.UserId,
        Name = name,
        Role = user.Role,
        ImpersonatedBy = user.IsImpersonating ? "saas_admin" : null,
    };

    public static AuditActor ForLogin(Guid userId, string name, string role) => new()
    {
        UserId = userId,
        Name = name,
        Role = role,
    };

    public static AuditActor ForFailedLogin() => new()
    {
        UserId = null,
        Role = "anonymous",
    };

    public static AuditActor System() => new()
    {
        UserId = null,
        Role = "system",
    };
}

public static class AuditTargetFactory
{
    public static AuditTarget Ticket(Guid id, string label) => new()
    {
        EntityType = "ticket",
        EntityId = id,
        Label = label,
    };

    public static AuditTarget Appointment(Guid id, string? label = null) => new()
    {
        EntityType = "appointment",
        EntityId = id,
        Label = label,
    };

    public static AuditTarget User(Guid id, string? name = null) => new()
    {
        EntityType = "user",
        EntityId = id,
        Label = name,
    };

    public static AuditTarget AiAgent(Guid id, string? name = null) => new()
    {
        EntityType = "ai_agent",
        EntityId = id,
        Label = name,
    };

    public static AuditTarget Tenant(Guid id, string? slug = null) => new()
    {
        EntityType = "tenant",
        EntityId = id,
        Label = slug,
    };
}
