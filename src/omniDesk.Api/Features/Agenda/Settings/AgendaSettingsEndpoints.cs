using FluentValidation;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Infrastructure.Agenda;

namespace omniDesk.Api.Features.Agenda.Settings;

/// <summary>
/// Spec 011 T134 — GET /api/agenda-settings + PUT /api/agenda-settings.
/// Restricted to tenant_admin role.
/// </summary>
public static class AgendaSettingsEndpoints
{
    public static RouteGroupBuilder MapAgendaSettingsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetAsync)
            .RequireAuthorization(policy => policy.RequireRole(Roles.TenantAdmin));

        group.MapPut("/", PutAsync)
            .RequireAuthorization(policy => policy.RequireRole(Roles.TenantAdmin));

        return group;
    }

    private static async Task<IResult> GetAsync(
        AgendaSettingsRepository repo,
        CancellationToken ct)
    {
        var settings = await repo.GetOrDefaultAsync(ct);
        return Results.Ok(new
        {
            success = true,
            data = MapDto(settings),
        });
    }

    private static async Task<IResult> PutAsync(
        UpdateAgendaSettingsRequest req,
        IValidator<UpdateAgendaSettingsRequest> validator,
        UpdateAgendaSettingsCommand command,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(req, ct);
        if (!validation.IsValid)
        {
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "VALIDATION_FAILED", details = validation.Errors.Select(e => e.ErrorMessage) },
            });
        }

        var settings = await command.ExecuteAsync(req, ct);
        return Results.Ok(new { success = true, data = MapDto(settings) });
    }

    private static object MapDto(omniDesk.Api.Domain.Agenda.AgendaSettings s) => new
    {
        late_cancel_window_hours = s.LateCancelWindowHours,
        late_cancel_text = s.LateCancelText,
        cancellation_policy_text = s.CancellationPolicyText,
        updated_at = s.UpdatedAt,
    };
}
