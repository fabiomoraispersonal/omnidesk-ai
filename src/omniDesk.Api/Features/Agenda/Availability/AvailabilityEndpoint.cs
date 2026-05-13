using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Agenda.Availability;

/// <summary>
/// Spec 011 T082 — GET /api/availability?professional_id=&service_id=&date=YYYY-MM-DD.
/// Uses AvailabilityCalculator (shared with AI check_availability tool — FR-018 parity).
/// </summary>
public static class AvailabilityEndpoint
{
    public static IEndpointRouteBuilder MapAvailabilityEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/availability", GetSlotsAsync)
            .RequireAuthorization()
            .WithName("Availability_GetSlots");
        return app;
    }

    private static async Task<IResult> GetSlotsAsync(
        Guid professional_id,
        Guid service_id,
        string date,
        IAvailabilityCalculator calculator,
        ICurrentUser caller,
        AppDbContext db,
        CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsedDate))
            return Results.BadRequest(new
            {
                success = false,
                error = new { code = "INVALID_DATE_FORMAT", message = "date must be YYYY-MM-DD" },
            });

        // Get tenant timezone (defaults to America/Sao_Paulo per CLAUDE.md)
        var tenant   = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == caller.TenantSlug, ct);
        var timezone = tenant?.Timezone ?? "America/Sao_Paulo";

        var slots = await calculator.GetSlotsAsync(professional_id, service_id, parsedDate, timezone, ct);

        return Results.Ok(new
        {
            success = true,
            data    = slots.Select(s => new { start_at = s.StartAt, end_at = s.EndAt }),
            meta    = new { professional_id, service_id, date, timezone },
        });
    }
}
