using MongoDB.Bson;
using omniDesk.Api.Domain.Audit;

namespace omniDesk.Api.Infrastructure.Audit;

/// <summary>
/// Spec 012 — fire-and-forget audit logging. Failures are logged but never propagated
/// to callers — audit must never break the primary operation.
/// </summary>
public class AuditService(
    AuditMongoRepository repository,
    ILogger<AuditService> logger) : IAuditService
{
    public void Log(
        string tenantSlug,
        Guid tenantId,
        string eventName,
        AuditActor actor,
        AuditTarget? target = null,
        object? metadata = null,
        string? ipAddress = null,
        string? userAgent = null)
    {
        var entry = new AuditLog
        {
            TenantSlug = tenantSlug,
            TenantId = tenantId,
            Event = eventName,
            Actor = actor,
            Target = target,
            Metadata = metadata is null ? null : BsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(metadata)),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Timestamp = DateTime.UtcNow,
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await repository.InsertAsync(entry, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to write audit log for tenant {Slug}, event {Event}.",
                    tenantSlug, eventName);
            }
        });
    }
}
