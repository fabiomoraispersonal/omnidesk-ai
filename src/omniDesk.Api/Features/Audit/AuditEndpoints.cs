using Microsoft.AspNetCore.Authentication.JwtBearer;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Infrastructure.Authentication;

namespace omniDesk.Api.Features.Audit;

public static class AuditEndpoints
{
    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetLogsAsync)
            .RequireAuthorization(policy => policy
                .RequireRole(Roles.TenantAdmin)
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyAuthenticationHandler.SchemeName));
        return group;
    }

    private static async Task<IResult> GetLogsAsync(
        ICurrentUser currentUser,
        GetAuditLogsHandler handler,
        string? @event,
        Guid? actor_id,
        DateTime? from,
        DateTime? to,
        int? page,
        int? per_page,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(currentUser.TenantSlug))
            return Results.Unauthorized();

        var filters = new AuditLogFilters(
            Event:   @event,
            ActorId: actor_id,
            From:    from,
            To:      to,
            Page:    page ?? 1,
            PerPage: Math.Clamp(per_page ?? 20, 1, 100));

        var (items, total) = await handler.ExecuteAsync(currentUser.TenantSlug, filters, ct);

        return Results.Ok(new
        {
            data = items,
            meta = new { page = filters.Page, per_page = filters.PerPage, total },
        });
    }
}
