using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Infrastructure.Audit;

namespace omniDesk.Api.Features.Audit;

public record AuditLogFilters(
    string? Event,
    Guid? ActorId,
    DateTime? From,
    DateTime? To,
    int Page,
    int PerPage);

public record AuditActorDto(
    Guid? UserId,
    string? Name,
    string Role,
    string? ImpersonatedBy);

public record AuditTargetDto(
    string EntityType,
    Guid EntityId,
    string? Label);

public record AuditLogDto(
    string Id,
    string Event,
    AuditActorDto Actor,
    AuditTargetDto? Target,
    object? Metadata,
    string? IpAddress,
    DateTime Timestamp);

public class GetAuditLogsHandler(AuditMongoRepository repo)
{
    public async Task<(IReadOnlyList<AuditLogDto> Items, long Total)> ExecuteAsync(
        string tenantSlug,
        AuditLogFilters filters,
        CancellationToken ct)
    {
        var (items, total) = await repo.QueryAsync(
            tenantSlug,
            filters.Event,
            filters.ActorId,
            filters.From,
            filters.To,
            filters.Page,
            filters.PerPage,
            ct);

        var dtos = items.Select(ToDto).ToList();
        return (dtos, total);
    }

    private static AuditLogDto ToDto(AuditLog log) => new(
        Id:        log.Id.ToString(),
        Event:     log.Event,
        Actor:     new AuditActorDto(log.Actor.UserId, log.Actor.Name, log.Actor.Role, log.Actor.ImpersonatedBy),
        Target:    log.Target is null ? null : new AuditTargetDto(log.Target.EntityType, log.Target.EntityId, log.Target.Label),
        Metadata:  log.Metadata is null ? null : MongoDB.Bson.BsonTypeMapper.MapToDotNetValue(log.Metadata),
        IpAddress: log.IpAddress,
        Timestamp: log.Timestamp);
}
