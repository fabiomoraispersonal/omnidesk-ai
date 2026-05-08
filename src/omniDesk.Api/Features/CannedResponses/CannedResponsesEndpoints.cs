using FluentValidation;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Domain.CannedResponses;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.CannedResponses;

public static class CannedResponsesEndpoints
{
    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync).RequireAuthorization();
        group.MapGet("/{id:guid}", GetByIdAsync).RequireAuthorization();
        group.MapPost("/", CreateAsync).RequireAuthorization();
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization();
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization();
        group.MapPost("/render", RenderAsync).RequireAuthorization();
        return group;
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        ICurrentUser currentUser,
        Guid? department_id,
        string? q,
        CancellationToken ct)
    {
        var attendant = await ResolveAttendantAsync(db, currentUser, ct);

        var query = db.CannedResponses.AsNoTracking().AsQueryable();

        if (department_id is { } dept)
        {
            query = query.Where(c => c.DepartmentId == null || c.DepartmentId == dept);
        }
        else if (attendant is not null)
        {
            // Default scope = global + departments the attendant belongs to.
            var deptIds = await db.AttendantDepartments.AsNoTracking()
                .Where(ad => ad.AttendantId == attendant.Id)
                .Select(ad => ad.DepartmentId)
                .ToArrayAsync(ct);
            query = query.Where(c => c.DepartmentId == null || deptIds.Contains(c.DepartmentId.Value));
        }
        else
        {
            // Non-attendants (admin/supervisor) see all.
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.ToLowerInvariant()}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Title, like) || EF.Functions.ILike(c.Content, like));
        }

        var rows = await query.OrderBy(c => c.Title).ToListAsync(ct);
        var authorIds = rows.Select(r => r.CreatedBy).Distinct().ToArray();
        var authors = await db.Attendants.AsNoTracking()
            .Where(a => authorIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var data = rows.Select(c => ToResponse(c, authors));
        return Results.Ok(new { success = true, data });
    }

    private static async Task<IResult> GetByIdAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var c = await db.CannedResponses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        var authorName = await db.Attendants.AsNoTracking()
            .Where(a => a.Id == c.CreatedBy).Select(a => a.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        return Results.Ok(new
        {
            success = true,
            data = ToResponse(c, new Dictionary<Guid, string> { [c.CreatedBy] = authorName })
        });
    }

    private static async Task<IResult> CreateAsync(
        CreateCannedResponseRequest request,
        IValidator<CreateCannedResponseRequest> validator,
        AppDbContext db,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(validation.Errors);

        var attendant = await ResolveAttendantAsync(db, currentUser, ct);
        if (attendant is null)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "NOT_AN_ATTENDANT", message = "Apenas atendentes podem criar respostas pré-formadas." }
            });

        if (request.DepartmentId is { } dept)
        {
            var deptOk = await db.Departments.AsNoTracking().AnyAsync(d => d.Id == dept && d.IsActive, ct);
            if (!deptOk)
                return Results.UnprocessableEntity(new
                {
                    success = false,
                    error = new { code = "DEPARTMENT_NOT_FOUND_OR_INACTIVE", message = "Departamento alvo não encontrado ou inativo." }
                });
        }

        var titleLower = request.Title.Trim().ToLowerInvariant();
        var duplicate = await db.CannedResponses.AsNoTracking().AnyAsync(c =>
            c.DepartmentId == request.DepartmentId
            && c.Title.ToLower() == titleLower, ct);
        if (duplicate)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "TITLE_DUPLICATE_IN_SCOPE", message = "Já existe uma resposta com esse título no mesmo escopo." }
            });

        var response = new CannedResponse
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Content = request.Content,
            DepartmentId = request.DepartmentId,
            CreatedBy = attendant.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.CannedResponses.Add(response);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/canned-responses/{response.Id}",
            new { success = true, data = ToResponse(response, new Dictionary<Guid, string> { [attendant.Id] = attendant.Name }) });
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateCannedResponseRequest request,
        IValidator<UpdateCannedResponseRequest> validator,
        AppDbContext db,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return ValidationProblem(validation.Errors);

        var existing = await db.CannedResponses.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (existing is null) return NotFound();

        if (!await IsOwnerOrTenantAdminAsync(db, currentUser, existing, ct))
            return Forbidden();

        existing.Title = request.Title.Trim();
        existing.Content = request.Content;
        existing.DepartmentId = request.DepartmentId;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var authorName = await db.Attendants.AsNoTracking()
            .Where(a => a.Id == existing.CreatedBy).Select(a => a.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        return Results.Ok(new
        {
            success = true,
            data = ToResponse(existing, new Dictionary<Guid, string> { [existing.CreatedBy] = authorName })
        });
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        AppDbContext db,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        var existing = await db.CannedResponses.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (existing is null) return NotFound();
        if (!await IsOwnerOrTenantAdminAsync(db, currentUser, existing, ct))
            return Forbidden();

        db.CannedResponses.Remove(existing);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RenderAsync(
        RenderCannedResponseRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var template = await db.CannedResponses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.TemplateId, ct);
        if (template is null) return NotFound();

        // Resolve context values from the optional ticket — minimal scaffold; Spec 008 will extend.
        long? ticketNumber = null;
        Guid? deptId = template.DepartmentId;
        if (request.Context.TicketId is { } tid)
        {
            var ticket = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid, ct);
            if (ticket is not null)
            {
                ticketNumber = ticket.Number;
                deptId = ticket.DepartmentId;
            }
        }

        var attendantName = (await ResolveAttendantAsync(db, currentUser, ct))?.Name;
        string? departmentName = null;
        if (deptId is { } d)
            departmentName = await db.Departments.AsNoTracking()
                .Where(x => x.Id == d).Select(x => x.Name).FirstOrDefaultAsync(ct);

        var ctxValues = new SubstitutionContext(
            ClientName: null, // Spec 008 owns conversation/contact resolution
            AttendantName: attendantName,
            TicketNumber: ticketNumber,
            DepartmentName: departmentName);

        var result = VariableSubstitution.Apply(template.Content, ctxValues);

        if (result.UnknownVariables.Count > 0)
        {
            var logger = loggerFactory.CreateLogger("CannedResponses");
            logger.LogWarning("Canned response {TemplateId} contém variáveis desconhecidas: {Vars}",
                template.Id, string.Join(",", result.UnknownVariables));
        }

        return Results.Ok(new
        {
            success = true,
            data = new RenderCannedResponseResponse(result.Rendered, result.UnknownVariables)
        });
    }

    private static async Task<bool> IsOwnerOrTenantAdminAsync(
        AppDbContext db, ICurrentUser currentUser, CannedResponse target, CancellationToken ct)
    {
        var role = Roles.Normalize(currentUser.Role);
        if (role == Roles.TenantAdmin) return true;

        if (currentUser.UserId is not Guid userId) return false;
        var att = await db.Attendants.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);
        return att?.Id == target.CreatedBy;
    }

    private static async Task<omniDesk.Api.Domain.Attendants.Attendant?> ResolveAttendantAsync(
        AppDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId) return null;
        return await db.Attendants.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);
    }

    private static CannedResponseResponse ToResponse(CannedResponse c, IDictionary<Guid, string> authors)
        => new(
            c.Id, c.Title, c.Content, c.DepartmentId,
            c.DepartmentId is null ? "global" : "department",
            new CannedResponseAuthor(c.CreatedBy, authors.TryGetValue(c.CreatedBy, out var n) ? n : string.Empty),
            c.CreatedAt, c.UpdatedAt);

    private static IResult NotFound() => Results.NotFound(new
    {
        success = false,
        error = new { code = "CANNED_RESPONSE_NOT_FOUND", message = "Resposta pré-formada não encontrada." }
    });

    private static IResult Forbidden() => Results.Json(new
    {
        success = false,
        error = new { code = "FORBIDDEN_NOT_OWNER", message = "Apenas o autor ou tenant_admin podem editar/excluir." }
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ValidationProblem(IEnumerable<FluentValidation.Results.ValidationFailure> errors)
        => Results.UnprocessableEntity(new
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
