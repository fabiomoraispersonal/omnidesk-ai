using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Distribution;

public record AssignTicketRequestDto(Guid DepartmentId, string Reason);

public record AssignTicketResponse(string Outcome, Guid? AssignedAttendantId, string? QueueReason);

public static class AssignTicketEndpoint
{
    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        // Internal endpoint — consumed by Spec 008 (Tickets) and the AI orchestrator (Spec 002).
        // Gated by tenant_admin to prevent direct usage from CRM clients.
        group.MapPost("/{ticketId:guid}/assign", HandleAsync)
             .RequireAuthorization(new AuthorizeAttribute { Roles = "TenantAdmin,SaasAdmin" });
        return group;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        AssignTicketRequestDto request,
        TicketAssignmentService service,
        ICurrentUser currentUser,
        AppDbContext db,
        CancellationToken ct)
    {
        if (!Enum.TryParse<AssignmentReason>(request.Reason, ignoreCase: true, out var reason))
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "INVALID_REASON", message = "reason inválida." }
            });

        var slug = await ResolveTenantSlugAsync(currentUser, db, ct);
        if (slug is null)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "TENANT_SLUG_NOT_RESOLVED", message = "Não foi possível resolver o tenant." }
            });

        try
        {
            var result = await service.AssignAsync(
                slug,
                new AssignTicketRequest(ticketId, request.DepartmentId, reason),
                ct);
            return Results.Ok(new
            {
                success = true,
                data = new AssignTicketResponse(
                    result.Outcome.ToString(),
                    result.AssignedAttendantId,
                    result.QueueReason?.ToString())
            });
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { success = false, error = new { code = "RESOURCE_NOT_FOUND", message = ex.Message } });
        }
    }

    internal static async Task<string?> ResolveTenantSlugAsync(ICurrentUser user, AppDbContext db, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(user.TenantSlug)) return user.TenantSlug;
        if (user.TenantId is { } tid)
        {
            return await db.Tenants.AsNoTracking()
                .Where(t => t.Id == tid)
                .Select(t => t.Slug)
                .FirstOrDefaultAsync(ct);
        }
        return null;
    }
}
