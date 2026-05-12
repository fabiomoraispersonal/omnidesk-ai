using FluentValidation;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Features.Departments.Validators;
using omniDesk.Api.Features.Pipelines;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Departments;

public static class DepartmentsEndpoints
{
    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync).RequireAuthorization(Policies.CanListDepartments);
        group.MapGet("/{id:guid}", GetByIdAsync).RequireAuthorization(Policies.CanListDepartments);
        group.MapPost("/", CreateAsync).RequireAuthorization(Policies.CanCreateDepartment);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(Policies.CanEditDepartment);
        group.MapDelete("/{id:guid}", DeactivateAsync).RequireAuthorization(Policies.CanEditDepartment);
        group.MapGet("/{id:guid}/attendants", ListDepartmentAttendantsAsync).RequireAuthorization(Policies.CanListDepartments);
        return group;
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        bool? include_inactive,
        CancellationToken ct)
    {
        var query = db.Departments.AsNoTracking();
        if (include_inactive != true)
            query = query.Where(d => d.IsActive);

        var depts = await query.OrderBy(d => d.Name).ToListAsync(ct);
        var ids = depts.Select(d => d.Id).ToArray();

        var counts = await db.AttendantDepartments.AsNoTracking()
            .Where(ad => ids.Contains(ad.DepartmentId))
            .GroupBy(ad => ad.DepartmentId)
            .Select(g => new { DeptId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DeptId, x => x.Count, ct);

        var data = depts.Select(d => ToResponse(d, counts.GetValueOrDefault(d.Id), activeTicketCount: 0));
        return Results.Ok(new { success = true, data });
    }

    private static async Task<IResult> GetByIdAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var dept = await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dept is null) return NotFound();
        var attendantCount = await db.AttendantDepartments.AsNoTracking()
            .CountAsync(ad => ad.DepartmentId == id, ct);
        return Results.Ok(new { success = true, data = ToResponse(dept, attendantCount, 0) });
    }

    private static async Task<IResult> CreateAsync(
        CreateDepartmentRequest request,
        AppDbContext db,
        IValidator<CreateDepartmentRequest> validator,
        PipelineProvisioningService pipelineProvisioner,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(validation.Errors);

        var nameLower = request.Name.Trim().ToLowerInvariant();
        var duplicate = await db.Departments.AsNoTracking()
            .AnyAsync(d => d.IsActive && d.Name.ToLower() == nameLower, ct);
        if (duplicate) return Results.UnprocessableEntity(new
        {
            success = false,
            error = new { code = "DEPARTMENT_NAME_DUPLICATE", message = "Já existe um departamento ativo com esse nome." }
        });

        var hours = request.BusinessHours is null
            ? null
            : DepartmentBusinessHours.Create(
                TimeOnly.Parse(request.BusinessHours.Start),
                TimeOnly.Parse(request.BusinessHours.End),
                request.BusinessHours.Days);

        var department = new Department
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description,
            SlaFirstResponseMinutes = request.Sla?.FirstResponseMinutes,
            SlaResolutionMinutes = request.Sla?.ResolutionMinutes,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        department.SetBusinessHours(hours);
        db.Departments.Add(department);
        await db.SaveChangesAsync(ct);

        await pipelineProvisioner.EnsurePipelineForDepartmentAsync(department.Id, ct);

        return Results.Created($"/api/departments/{department.Id}",
            new { success = true, data = ToResponse(department, 0, 0) });
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateDepartmentRequest request,
        AppDbContext db,
        IValidator<UpdateDepartmentRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(validation.Errors);

        var dept = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dept is null) return NotFound();

        var nameLower = request.Name.Trim().ToLowerInvariant();
        var duplicate = await db.Departments.AsNoTracking()
            .AnyAsync(d => d.IsActive && d.Id != id && d.Name.ToLower() == nameLower, ct);
        if (duplicate) return Results.UnprocessableEntity(new
        {
            success = false,
            error = new { code = "DEPARTMENT_NAME_DUPLICATE", message = "Já existe um departamento ativo com esse nome." }
        });

        var hours = request.BusinessHours is null
            ? null
            : DepartmentBusinessHours.Create(
                TimeOnly.Parse(request.BusinessHours.Start),
                TimeOnly.Parse(request.BusinessHours.End),
                request.BusinessHours.Days);

        dept.Name = request.Name.Trim();
        dept.Description = request.Description;
        dept.SlaFirstResponseMinutes = request.Sla?.FirstResponseMinutes;
        dept.SlaResolutionMinutes = request.Sla?.ResolutionMinutes;
        dept.SetBusinessHours(hours);
        dept.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var attendantCount = await db.AttendantDepartments.AsNoTracking()
            .CountAsync(ad => ad.DepartmentId == id, ct);
        return Results.Ok(new { success = true, data = ToResponse(dept, attendantCount, 0) });
    }

    private static async Task<IResult> DeactivateAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var dept = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dept is null) return NotFound();

        var linked = await db.AttendantDepartments.AsNoTracking()
            .CountAsync(ad => ad.DepartmentId == id, ct);
        if (linked > 0)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new
                {
                    code = "DEPARTMENT_HAS_LINKED_ATTENDANTS",
                    message = "Departamento possui atendentes vinculados. Desvincule antes de desativar.",
                    details = new { linked_attendants = linked }
                }
            });

        dept.IsActive = false;
        dept.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> ListDepartmentAttendantsAsync(
        Guid id,
        string? status,
        bool? include_at_capacity,
        AppDbContext db,
        CancellationToken ct)
    {
        var query = from ad in db.AttendantDepartments.AsNoTracking()
                    join a in db.Attendants.AsNoTracking() on ad.AttendantId equals a.Id
                    where ad.DepartmentId == id && a.IsActive
                    select new { a, IsPrimary = ad.IsPrimary };

        var rows = await query.ToListAsync(ct);
        var data = rows.Select(r => new
        {
            attendant_id = r.a.Id,
            name = r.a.Name,
            avatar_url = r.a.AvatarUrl,
            active_ticket_count = r.a.ActiveTicketCount,
            max_simultaneous_chats = r.a.MaxSimultaneousChats,
            is_primary_department = r.IsPrimary,
        });
        return Results.Ok(new { success = true, data });
    }

    private static DepartmentResponse ToResponse(Department d, int attendantCount, int activeTicketCount)
    {
        BusinessHoursDto? hours = null;
        if (d.BusinessHoursStart.HasValue && d.BusinessHoursEnd.HasValue && d.BusinessDays?.Length > 0)
        {
            hours = new BusinessHoursDto(
                d.BusinessHoursStart.Value.ToString("HH:mm"),
                d.BusinessHoursEnd.Value.ToString("HH:mm"),
                d.BusinessDays);
        }

        return new DepartmentResponse(
            d.Id,
            d.Name,
            d.Description,
            hours,
            new SlaDto(d.SlaFirstResponseMinutes, d.SlaResolutionMinutes),
            d.IsActive,
            attendantCount,
            activeTicketCount,
            d.CreatedAt,
            d.UpdatedAt);
    }

    private static IResult NotFound() => Results.NotFound(new
    {
        success = false,
        error = new { code = "DEPARTMENT_NOT_FOUND", message = "Departamento não encontrado." }
    });

    private static IResult ValidationProblem(IEnumerable<FluentValidation.Results.ValidationFailure> errors) =>
        Results.UnprocessableEntity(new
        {
            success = false,
            error = new
            {
                code = "VALIDATION_FAILED",
                message = "Dados inválidos.",
                details = errors.Select(e => new { e.PropertyName, e.ErrorMessage })
            }
        });
}
