using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Agenda.Services.Commands;
using omniDesk.Api.Features.Agenda.Services.Queries;
using omniDesk.Api.Features.Agenda.Validators;
using omniDesk.Api.Infrastructure.Authentication;

namespace omniDesk.Api.Features.Agenda.Services;

/// <summary>
/// Spec 011 T034 — REST surface do catálogo de serviços.
/// Montado em <c>app.MapGroup("/api/services").MapServicesEndpoints().RequireAuthorization()</c>.
/// Escrita restrita a <c>tenant_admin</c> (CanManageServiceCatalog). Leitura para qualquer
/// autenticado (attendant+).
/// </summary>
public static class ServicesEndpoints
{
    public record CreateRequest(
        string Name,
        string? Description,
        string? Category,
        int DurationMinutes,
        decimal? Price,
        bool RequiresConfirmation);

    public record UpdateRequest(
        string Name,
        string? Description,
        string? Category,
        int DurationMinutes,
        decimal? Price,
        bool RequiresConfirmation);

    public record ToggleRequest(bool IsActive);

    public static RouteGroupBuilder MapServicesEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/",             ListAsync).WithName("Services_List");
        group.MapPost("/",            CreateAsync).WithName("Services_Create");
        group.MapPut("/{id:guid}",    UpdateAsync).WithName("Services_Update");
        group.MapPatch("/{id:guid}/toggle", ToggleAsync).WithName("Services_Toggle");
        return group;
    }

    // ── GET /api/services ─────────────────────────────────────────────

    private static async Task<IResult> ListAsync(
        ListServicesQuery query,
        bool? include_inactive,
        int? page,
        int? per_page,
        string? sort,
        string? order,
        CancellationToken ct)
    {
        var (items, total) = await query.ExecuteAsync(
            page ?? 1,
            per_page ?? 50,
            include_inactive ?? false,
            sort ?? "name",
            order ?? "asc",
            ct);

        return Results.Ok(new
        {
            success = true,
            data = items.Select(ServiceDto),
            meta = new { page = page ?? 1, per_page = per_page ?? 50, total },
        });
    }

    // ── POST /api/services ────────────────────────────────────────────

    private static async Task<IResult> CreateAsync(
        CreateRequest request,
        ICurrentUser current,
        CreateServiceCommand command,
        CreateServiceValidator validator,
        CancellationToken ct)
    {
        if (!IsTenantAdmin(current)) return Forbidden();

        var validation = validator.Validate(new CreateServiceValidator.Request(
            request.Name, request.Description, request.Category,
            request.DurationMinutes, request.Price, request.RequiresConfirmation));
        if (!validation.IsValid)
            return ValidationErrors(validation.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage }));

        if (request.DurationMinutes <= 0)
            return Results.BadRequest(Error(AgendaErrorCodes.ServiceDurationInvalid, "duration_minutes must be greater than 0."));

        var service = await command.ExecuteAsync(
            request.Name, request.Description, request.Category,
            request.DurationMinutes, request.Price, request.RequiresConfirmation, ct);

        return Results.Created($"/api/services/{service.Id}", new { success = true, data = ServiceDto(service) });
    }

    // ── PUT /api/services/{id} ────────────────────────────────────────

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateRequest request,
        ICurrentUser current,
        UpdateServiceCommand command,
        UpdateServiceValidator validator,
        CancellationToken ct)
    {
        if (!IsTenantAdmin(current)) return Forbidden();

        var validation = validator.Validate(new UpdateServiceValidator.Request(
            request.Name, request.Description, request.Category,
            request.DurationMinutes, request.Price, request.RequiresConfirmation));
        if (!validation.IsValid)
            return ValidationErrors(validation.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage }));

        if (request.DurationMinutes <= 0)
            return Results.BadRequest(Error(AgendaErrorCodes.ServiceDurationInvalid, "duration_minutes must be greater than 0."));

        var service = await command.ExecuteAsync(
            id, request.Name, request.Description, request.Category,
            request.DurationMinutes, request.Price, request.RequiresConfirmation, ct);

        if (service is null)
            return Results.NotFound(Error(AgendaErrorCodes.ServiceNotFound, "Service not found."));

        return Results.Ok(new { success = true, data = ServiceDto(service) });
    }

    // ── PATCH /api/services/{id}/toggle ──────────────────────────────

    private static async Task<IResult> ToggleAsync(
        Guid id,
        ToggleRequest request,
        ICurrentUser current,
        ToggleServiceCommand command,
        CancellationToken ct)
    {
        if (!IsTenantAdmin(current)) return Forbidden();

        var service = await command.ExecuteAsync(id, request.IsActive, ct);

        if (service is null)
            return Results.NotFound(Error(AgendaErrorCodes.ServiceNotFound, "Service not found."));

        return Results.Ok(new { success = true, data = new { id = service.Id, is_active = service.IsActive } });
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static object ServiceDto(Service s) => new
    {
        id = s.Id,
        name = s.Name,
        description = s.Description,
        category = s.Category,
        duration_minutes = s.DurationMinutes,
        price = s.Price,
        requires_confirmation = s.RequiresConfirmation,
        is_active = s.IsActive,
        created_at = s.CreatedAt,
        updated_at = s.UpdatedAt,
    };

    private static bool IsTenantAdmin(ICurrentUser current) =>
        current.IsAuthenticated && current.Role == Roles.TenantAdmin;

    private static IResult Forbidden() =>
        Results.Json(new
        {
            success = false,
            error = new { code = "FORBIDDEN", message = "Only tenant_admin can manage the service catalog." },
        }, statusCode: 403);

    private static object Error(string code, string message) => new
    {
        success = false,
        error = new { code, message },
    };

    private static IResult ValidationErrors(IEnumerable<object> errors) =>
        Results.UnprocessableEntity(new
        {
            success = false,
            error = new { code = AgendaErrorCodes.ValidationFailed, message = "Validation failed.", details = errors },
        });
}
