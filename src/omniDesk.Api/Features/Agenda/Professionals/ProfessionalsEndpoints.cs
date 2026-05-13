using omniDesk.Api.Domain.Agenda;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Agenda.Professionals.Commands;
using omniDesk.Api.Features.Agenda.Professionals.Queries;
using omniDesk.Api.Features.Agenda.Validators;
using omniDesk.Api.Infrastructure.Authentication;

namespace omniDesk.Api.Features.Agenda.Professionals;

/// <summary>
/// Spec 011 T061 — REST surface para profissionais, serviços vinculados, disponibilidade e bloqueios.
/// Montado em <c>app.MapGroup("/api/professionals").MapProfessionalsEndpoints()</c>.
/// Escrita restrita a tenant_admin. Leitura para qualquer autenticado.
/// </summary>
public static class ProfessionalsEndpoints
{
    public record CreateProfRequest(string Name, string? Specialty, Guid? DepartmentId, Guid? AttendantId);
    public record UpdateProfRequest(string Name, string? Specialty, Guid? DepartmentId, Guid? AttendantId);
    public record ToggleProfRequest(bool IsActive);
    public record UpdateServicesRequest(IReadOnlyList<Guid> ServiceIds);
    public record ScheduleSlotRequest(int DayOfWeek, string StartTime, string EndTime);
    public record UpdateScheduleRequest(IReadOnlyList<ScheduleSlotRequest> Slots);
    public record CreateBlockRequest(DateTimeOffset StartAt, DateTimeOffset EndAt, string? Reason);

    public static RouteGroupBuilder MapProfessionalsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/",    ListAsync).WithName("Professionals_List");
        group.MapPost("/",   CreateAsync).WithName("Professionals_Create");
        group.MapPut("/{id:guid}",   UpdateAsync).WithName("Professionals_Update");
        group.MapPatch("/{id:guid}/toggle", ToggleAsync).WithName("Professionals_Toggle");

        group.MapGet("/{id:guid}/services", GetServicesAsync).WithName("Professionals_GetServices");
        group.MapPut("/{id:guid}/services", UpdateServicesAsync).WithName("Professionals_UpdateServices");

        group.MapGet("/{id:guid}/schedule", GetScheduleAsync).WithName("Professionals_GetSchedule");
        group.MapPut("/{id:guid}/schedule", UpdateScheduleAsync).WithName("Professionals_UpdateSchedule");

        group.MapGet("/{id:guid}/blocks",   ListBlocksAsync).WithName("Professionals_ListBlocks");
        group.MapPost("/{id:guid}/blocks",  CreateBlockAsync).WithName("Professionals_CreateBlock");
        group.MapDelete("/{id:guid}/blocks/{blockId:guid}", DeleteBlockAsync).WithName("Professionals_DeleteBlock");
        return group;
    }

    // ── LIST ─────────────────────────────────────────────────────────

    private static async Task<IResult> ListAsync(
        ListProfessionalsQuery query,
        Guid? department_id, Guid? service_id,
        bool? include_inactive, int? page, int? per_page,
        CancellationToken ct)
    {
        var (items, total) = await query.ExecuteAsync(
            page ?? 1, per_page ?? 50, department_id, service_id, include_inactive ?? false, ct);
        return Results.Ok(new
        {
            success = true,
            data = items.Select(ProfDto),
            meta = new { page = page ?? 1, per_page = per_page ?? 50, total },
        });
    }

    // ── CREATE ────────────────────────────────────────────────────────

    private static async Task<IResult> CreateAsync(
        CreateProfRequest req, ICurrentUser current,
        CreateProfessionalCommand cmd, CreateProfessionalValidator v, CancellationToken ct)
    {
        if (!IsTenantAdmin(current)) return Forbidden();
        var vr = v.Validate(new CreateProfessionalValidator.Request(req.Name, req.Specialty, req.DepartmentId, req.AttendantId));
        if (!vr.IsValid) return ValidationError(vr);
        var p = await cmd.ExecuteAsync(req.Name, req.Specialty, req.DepartmentId, req.AttendantId, ct);
        return Results.Created($"/api/professionals/{p.Id}", new { success = true, data = ProfDto(p) });
    }

    // ── UPDATE ────────────────────────────────────────────────────────

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateProfRequest req, ICurrentUser current,
        UpdateProfessionalCommand cmd, UpdateProfessionalValidator v, CancellationToken ct)
    {
        if (!IsTenantAdmin(current)) return Forbidden();
        var vr = v.Validate(new UpdateProfessionalValidator.Request(req.Name, req.Specialty, req.DepartmentId, req.AttendantId));
        if (!vr.IsValid) return ValidationError(vr);
        var p = await cmd.ExecuteAsync(id, req.Name, req.Specialty, req.DepartmentId, req.AttendantId, ct);
        return p is null
            ? Results.NotFound(Error(AgendaErrorCodes.ProfessionalNotFound, "Professional not found."))
            : Results.Ok(new { success = true, data = ProfDto(p) });
    }

    // ── TOGGLE ────────────────────────────────────────────────────────

    private static async Task<IResult> ToggleAsync(
        Guid id, ToggleProfRequest req, ICurrentUser current,
        ToggleProfessionalCommand cmd, CancellationToken ct)
    {
        if (!IsTenantAdmin(current)) return Forbidden();
        var p = await cmd.ExecuteAsync(id, req.IsActive, ct);
        return p is null
            ? Results.NotFound(Error(AgendaErrorCodes.ProfessionalNotFound, "Professional not found."))
            : Results.Ok(new { success = true, data = new { id = p.Id, is_active = p.IsActive } });
    }

    // ── SERVICES ──────────────────────────────────────────────────────

    private static async Task<IResult> GetServicesAsync(
        Guid id, GetProfessionalServicesQuery query, CancellationToken ct)
    {
        var links = await query.ExecuteAsync(id, ct);
        return Results.Ok(new { success = true, data = links.Select(l => new { l.Id, service_id = l.ServiceId }) });
    }

    private static async Task<IResult> UpdateServicesAsync(
        Guid id, UpdateServicesRequest req, ICurrentUser current,
        UpdateProfessionalServicesCommand cmd, CancellationToken ct)
    {
        if (!IsTenantAdmin(current)) return Forbidden();
        await cmd.ExecuteAsync(id, req.ServiceIds, ct);
        return Results.Ok(new { success = true });
    }

    // ── SCHEDULE ──────────────────────────────────────────────────────

    private static async Task<IResult> GetScheduleAsync(
        Guid id, GetWeeklyScheduleQuery query, CancellationToken ct)
    {
        var schedule = await query.ExecuteAsync(id, ct);
        return Results.Ok(new { success = true, data = schedule.Select(ScheduleDto) });
    }

    private static async Task<IResult> UpdateScheduleAsync(
        Guid id, UpdateScheduleRequest req, ICurrentUser current,
        UpdateWeeklyScheduleCommand cmd, WeeklyScheduleValidator v, CancellationToken ct)
    {
        if (!IsTenantAdmin(current)) return Forbidden();

        foreach (var slot in req.Slots)
        {
            var vr = v.Validate(new WeeklyScheduleValidator.SlotRequest(slot.DayOfWeek, slot.StartTime, slot.EndTime));
            if (!vr.IsValid) return ValidationError(vr);
        }

        var slots = req.Slots.Select(s => new WeeklyScheduleSlot(
            s.DayOfWeek,
            TimeOnly.ParseExact(s.StartTime, "HH:mm"),
            TimeOnly.ParseExact(s.EndTime,   "HH:mm")));

        var result = await cmd.ExecuteAsync(id, slots, ct);

        return result.Success
            ? Results.Ok(new { success = true, data = result.Schedule!.Select(ScheduleDto) })
            : result.ErrorCode == "PROFESSIONAL_NOT_FOUND"
                ? Results.NotFound(Error(AgendaErrorCodes.ProfessionalNotFound, "Professional not found."))
                : Results.UnprocessableEntity(Error(AgendaErrorCodes.WeeklyScheduleOverlap, "Overlapping time slots on the same day."));
    }

    // ── BLOCKS ────────────────────────────────────────────────────────

    private static async Task<IResult> ListBlocksAsync(
        Guid id, ListBlocksQuery query,
        DateTimeOffset? from,
        CancellationToken ct)
    {
        var blocks = await query.ExecuteAsync(id, from ?? DateTimeOffset.UtcNow, ct);
        return Results.Ok(new { success = true, data = blocks.Select(BlockDto) });
    }

    private static async Task<IResult> CreateBlockAsync(
        Guid id, CreateBlockRequest req, ICurrentUser current,
        CreateBlockCommand cmd, ScheduleBlockValidator v, CancellationToken ct)
    {
        if (!IsTenantAdmin(current)) return Forbidden();
        var vr = v.Validate(new ScheduleBlockValidator.Request(req.StartAt, req.EndAt, req.Reason));
        if (!vr.IsValid) return ValidationError(vr);

        var result = await cmd.TryExecuteAsync(id, req.StartAt, req.EndAt, req.Reason, ct);

        if (!result.Success)
        {
            return result.ErrorCode == AgendaErrorCodes.BlockOverlapsAppointments
                ? Results.Conflict(new
                {
                    success = false,
                    error = new { code = result.ErrorCode, message = "Block overlaps existing confirmed/pending appointments.", details = result.ConflictingIds },
                })
                : Results.BadRequest(Error(result.ErrorCode!, "Invalid block range."));
        }

        return Results.Created($"/api/professionals/{id}/blocks/{result.Block!.Id}",
            new { success = true, data = BlockDto(result.Block) });
    }

    private static async Task<IResult> DeleteBlockAsync(
        Guid id, Guid blockId, ICurrentUser current,
        DeleteBlockCommand cmd, CancellationToken ct)
    {
        if (!IsTenantAdmin(current)) return Forbidden();
        var deleted = await cmd.ExecuteAsync(blockId, id, ct);
        return deleted
            ? Results.Ok(new { success = true })
            : Results.NotFound(Error(AgendaErrorCodes.BlockNotFound, "Block not found."));
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static object ProfDto(Professional p) => new
    {
        id = p.Id, name = p.Name, specialty = p.Specialty,
        department_id = p.DepartmentId, attendant_id = p.AttendantId,
        is_active = p.IsActive, created_at = p.CreatedAt, updated_at = p.UpdatedAt,
    };

    private static object ScheduleDto(WeeklySchedule ws) => new
    {
        id = ws.Id, professional_id = ws.ProfessionalId,
        day_of_week = ws.DayOfWeek,
        start_time = ws.StartTime.ToString("HH:mm"),
        end_time   = ws.EndTime.ToString("HH:mm"),
    };

    private static object BlockDto(ScheduleBlock b) => new
    {
        id = b.Id, professional_id = b.ProfessionalId,
        start_at = b.StartAt, end_at = b.EndAt, reason = b.Reason, created_at = b.CreatedAt,
    };

    private static bool IsTenantAdmin(ICurrentUser u) => u.IsAuthenticated && u.Role == Roles.TenantAdmin;

    private static IResult Forbidden() =>
        Results.Json(new { success = false, error = new { code = "FORBIDDEN", message = "Only tenant_admin can manage professionals." } }, statusCode: 403);

    private static object Error(string code, string msg) => new { success = false, error = new { code, message = msg } };

    private static IResult ValidationError(FluentValidation.Results.ValidationResult vr) =>
        Results.UnprocessableEntity(new
        {
            success = false,
            error = new { code = AgendaErrorCodes.ValidationFailed, message = "Validation failed.", details = vr.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage }) },
        });

    private static class AgendaErrorCodes
    {
        public const string ProfessionalNotFound = "PROFESSIONAL_NOT_FOUND";
        public const string WeeklyScheduleOverlap = "WEEKLY_SCHEDULE_OVERLAP";
        public const string BlockNotFound = "BLOCK_NOT_FOUND";
        public const string BlockOverlapsAppointments = "BLOCK_OVERLAPS_APPOINTMENTS";
        public const string ValidationFailed = "VALIDATION_FAILED";
    }
}
