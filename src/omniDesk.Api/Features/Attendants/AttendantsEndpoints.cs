using FluentValidation;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Features.Attendants.Validators;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Attendants;

public static class AttendantsEndpoints
{
    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync).RequireAuthorization(Policies.CanListDepartments);
        group.MapGet("/{id:guid}", GetByIdAsync).RequireAuthorization(Policies.CanListDepartments);
        group.MapPost("/", CreateAsync).RequireAuthorization(Policies.CanCreateAttendant);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(Policies.CanEditAttendant);
        group.MapDelete("/{id:guid}", DeactivateAsync).RequireAuthorization(Policies.CanDeactivateAttendant);
        group.MapPut("/{id:guid}/departments", UpdateDepartmentsAsync).RequireAuthorization(Policies.CanEditAttendant);
        AvatarUploadEndpoint.Map(group);
        UpdateStatusEndpoint.Map(group);
        GetAttendantTicketsEndpoint.Map(group);
        return group;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var attendants = await db.Attendants.AsNoTracking()
            .Include(a => a.Departments)
            .Include(a => a.Status)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);
        var data = attendants.Select(ToResponse);
        return Results.Ok(new { success = true, data });
    }

    private static async Task<IResult> GetByIdAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var a = await db.Attendants.AsNoTracking()
            .Include(x => x.Departments)
            .Include(x => x.Status)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        return Results.Ok(new { success = true, data = ToResponse(a) });
    }

    private static async Task<IResult> CreateAsync(
        CreateAttendantRequest request,
        AppDbContext db,
        IValidator<CreateAttendantRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(validation.Errors);

        var userExists = await db.Users.AsNoTracking().AnyAsync(u => u.Id == request.UserId, ct);
        if (!userExists)
            return Results.NotFound(new
            {
                success = false,
                error = new { code = "USER_NOT_FOUND", message = "Usuário não encontrado em public.users." }
            });

        var alreadyAttendant = await db.Attendants.AsNoTracking().AnyAsync(a => a.UserId == request.UserId, ct);
        if (alreadyAttendant)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "USER_ALREADY_ATTENDANT", message = "Este usuário já é um atendente." }
            });

        var deptIds = request.DepartmentIds ?? Array.Empty<Guid>();
        if (deptIds.Length > 0)
        {
            var validDepts = await db.Departments.AsNoTracking()
                .Where(d => deptIds.Contains(d.Id) && d.IsActive)
                .Select(d => d.Id)
                .ToArrayAsync(ct);
            if (validDepts.Length != deptIds.Length)
                return Results.UnprocessableEntity(new
                {
                    success = false,
                    error = new { code = "DEPARTMENT_NOT_FOUND_OR_INACTIVE", message = "Um ou mais departamentos não existem ou estão inativos." }
                });
        }

        var attendant = new Attendant
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Name = request.Name.Trim(),
            MaxSimultaneousChats = request.MaxSimultaneousChats ?? 5,
            ActiveTicketCount = 0,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var primary = request.PrimaryDepartmentId ?? deptIds.FirstOrDefault();
        foreach (var deptId in deptIds)
        {
            attendant.Departments.Add(new AttendantDepartment
            {
                AttendantId = attendant.Id,
                DepartmentId = deptId,
                IsPrimary = deptId == primary,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        attendant.Status = new AttendantStatusEntry
        {
            AttendantId = attendant.Id,
            Status = AttendanceStatus.Offline,
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = AttendanceStatusChangedBy.Manual,
        };

        db.Attendants.Add(attendant);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/attendants/{attendant.Id}",
            new { success = true, data = ToResponse(attendant) });
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateAttendantRequest request,
        AppDbContext db,
        CancellationToken ct)
    {
        var a = await db.Attendants.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 255)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "VALIDATION_FAILED", message = "Nome inválido." }
            });
        if (request.MaxSimultaneousChats is { } m && (m < 1 || m > 100))
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "VALIDATION_FAILED", message = "max_simultaneous_chats deve estar entre 1 e 100." }
            });

        a.Name = request.Name.Trim();
        if (request.MaxSimultaneousChats.HasValue)
            a.MaxSimultaneousChats = request.MaxSimultaneousChats.Value;
        a.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { success = true, data = ToResponse(a) });
    }

    private static async Task<IResult> DeactivateAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var a = await db.Attendants.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        a.IsActive = false;
        a.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> UpdateDepartmentsAsync(
        Guid id,
        UpdateAttendantDepartmentsRequest request,
        AppDbContext db,
        IValidator<UpdateAttendantDepartmentsRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(validation.Errors);

        var a = await db.Attendants
            .Include(x => x.Departments)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();

        var newIds = request.DepartmentIds ?? Array.Empty<Guid>();
        var primary = request.PrimaryDepartmentId ?? newIds.FirstOrDefault();

        // Validate departments exist and active
        if (newIds.Length > 0)
        {
            var validCount = await db.Departments.AsNoTracking()
                .CountAsync(d => newIds.Contains(d.Id) && d.IsActive, ct);
            if (validCount != newIds.Length)
                return Results.UnprocessableEntity(new
                {
                    success = false,
                    error = new { code = "DEPARTMENT_NOT_FOUND_OR_INACTIVE", message = "Um ou mais departamentos inválidos." }
                });
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Remove links not present
        var toRemove = a.Departments.Where(ad => !newIds.Contains(ad.DepartmentId)).ToList();
        foreach (var rm in toRemove) db.AttendantDepartments.Remove(rm);

        // Add missing
        var existingIds = a.Departments.Select(ad => ad.DepartmentId).ToHashSet();
        foreach (var deptId in newIds.Where(d => !existingIds.Contains(d)))
        {
            db.AttendantDepartments.Add(new AttendantDepartment
            {
                AttendantId = a.Id,
                DepartmentId = deptId,
                IsPrimary = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        await db.SaveChangesAsync(ct);

        // Reset is_primary atomically
        await db.AttendantDepartments
            .Where(ad => ad.AttendantId == a.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(ad => ad.IsPrimary, false), ct);
        if (primary != Guid.Empty)
        {
            await db.AttendantDepartments
                .Where(ad => ad.AttendantId == a.Id && ad.DepartmentId == primary)
                .ExecuteUpdateAsync(s => s.SetProperty(ad => ad.IsPrimary, true), ct);
        }

        a.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var refreshed = await db.Attendants.AsNoTracking()
            .Include(x => x.Departments)
            .Include(x => x.Status)
            .FirstAsync(x => x.Id == id, ct);
        return Results.Ok(new { success = true, data = ToResponse(refreshed) });
    }

    private static AttendantResponse ToResponse(Attendant a)
    {
        var deptIds = a.Departments?.Select(d => d.DepartmentId).ToArray() ?? Array.Empty<Guid>();
        var primary = a.Departments?.FirstOrDefault(d => d.IsPrimary)?.DepartmentId;
        return new AttendantResponse(
            a.Id, a.UserId, a.Name, a.AvatarUrl,
            a.MaxSimultaneousChats, a.ActiveTicketCount, a.IsActive,
            deptIds, primary,
            a.Status?.Status.ToWireValue(),
            a.CreatedAt);
    }

    private static IResult NotFound() => Results.NotFound(new
    {
        success = false,
        error = new { code = "ATTENDANT_NOT_FOUND", message = "Atendente não encontrado." }
    });

    private static IResult ValidationProblem(IEnumerable<FluentValidation.Results.ValidationFailure> errors) =>
        Results.UnprocessableEntity(new
        {
            success = false,
            error = new
            {
                code = "VALIDATION_FAILED",
                message = "Dados inválidos.",
                details = errors.Select(e => new { e.PropertyName, e.ErrorMessage, e.ErrorCode })
            }
        });
}
