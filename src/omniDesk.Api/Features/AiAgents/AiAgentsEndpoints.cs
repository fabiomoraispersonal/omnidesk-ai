using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Features.AiAgents.Validators;
using omniDesk.Api.Features.Authorization.Policies;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.AiAgents;

public static class AiAgentsEndpoints
{
    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync).RequireAuthorization(Policies.CanViewAgents);
        group.MapGet("/{id:guid}", GetByIdAsync).RequireAuthorization(Policies.CanViewAgents);
        group.MapPost("/", CreateAsync).RequireAuthorization(Policies.CanManageSubAgents);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(Policies.CanManageSubAgents);
        group.MapPatch("/{id:guid}/toggle", ToggleAsync).RequireAuthorization(Policies.CanManageSubAgents);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(Policies.CanManageSubAgents);
        return group;
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        bool? include_inactive,
        string? type,
        CancellationToken ct)
    {
        var q = db.AiAgents.AsNoTracking().AsQueryable();
        if (include_inactive != true) q = q.Where(a => a.IsActive);
        if (!string.IsNullOrEmpty(type))
        {
            var parsed = AgentTypes.Parse(type);
            q = q.Where(a => a.Type == parsed);
        }

        var agents = await q.OrderBy(a => a.Type).ThenBy(a => a.Name).ToListAsync(ct);
        var deptIds = agents.Where(a => a.DepartmentId.HasValue).Select(a => a.DepartmentId!.Value).Distinct().ToArray();
        var deptNames = await db.Departments.AsNoTracking()
            .Where(d => deptIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        var data = agents.Select(a => new
        {
            id = a.Id,
            type = AgentTypes.ToWire(a.Type),
            name = a.Name,
            short_description = a.ShortDescription,
            model = a.Model,
            department_id = a.DepartmentId,
            department_name = a.DepartmentId is { } id ? deptNames.GetValueOrDefault(id) : null,
            is_active = a.IsActive,
            openai_assistant_id_present = !string.IsNullOrEmpty(a.OpenAiAssistantId),
            created_at = a.CreatedAt,
            updated_at = a.UpdatedAt,
        });
        return Results.Ok(new { success = true, data, meta = new { total = agents.Count } });
    }

    private static async Task<IResult> GetByIdAsync(Guid id, AppDbContext db, IConfiguration config, CancellationToken ct)
    {
        var a = await db.AiAgents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound("AGENT_NOT_FOUND", "Agente não encontrado.");

        string? deptName = null;
        if (a.DepartmentId is { } d)
            deptName = await db.Departments.AsNoTracking().Where(x => x.Id == d).Select(x => x.Name).FirstOrDefaultAsync(ct);

        var aiSettings = await db.AiSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId != Guid.Empty, ct);
        var globalAllow = config.GetSection("Ai:GlobalAllowedModels").Get<string[]>() ?? Array.Empty<string>();
        var allowed = (aiSettings?.AvailableModels.Length > 0 ? aiSettings.AvailableModels : globalAllow);

        return Results.Ok(new { success = true, data = new
        {
            id = a.Id,
            type = AgentTypes.ToWire(a.Type),
            name = a.Name,
            short_description = a.ShortDescription,
            prompt = a.Prompt,
            model = a.Model,
            available_models_for_tenant = allowed,
            department_id = a.DepartmentId,
            department_name = deptName,
            is_active = a.IsActive,
            created_at = a.CreatedAt,
            updated_at = a.UpdatedAt,
            deleted_at = a.DeletedAt,
        } });
    }

    private static async Task<IResult> CreateAsync(
        CreateAiAgentRequest req,
        AppDbContext db,
        IValidator<CreateAiAgentRequest> validator,
        ICurrentUser currentUser,
        IConfiguration config,
        CancellationToken ct)
    {
        var v = await validator.ValidateAsync(req, ct);
        if (!v.IsValid) return ValidationFailed(v);

        var globalAllow = config.GetSection("Ai:GlobalAllowedModels").Get<string[]>() ?? Array.Empty<string>();
        var settings = await db.AiSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var tenantAllow = settings?.AvailableModels ?? Array.Empty<string>();
        var allowed = (tenantAllow.Length > 0 ? tenantAllow : globalAllow);
        if (!allowed.Contains(req.Model))
            return Conflict("MODEL_NOT_ALLOWED", $"Modelo '{req.Model}' não está habilitado.");

        var deptOk = await db.Departments.AsNoTracking().AnyAsync(d => d.Id == req.DepartmentId && d.IsActive, ct);
        if (!deptOk) return Conflict("DEPARTMENT_NOT_ACTIVE", "Departamento inativo ou inexistente.");

        var entity = new AiAgent
        {
            Id = Guid.NewGuid(),
            Type = AgentType.SubAgent,
            Name = req.Name,
            ShortDescription = req.ShortDescription,
            Prompt = req.Prompt,
            Model = req.Model,
            DepartmentId = req.DepartmentId,
            IsActive = true,
            CreatedBy = currentUser.UserId ?? Guid.Empty,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.AiAgents.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/agents/{entity.Id}", new { success = true, data = new { id = entity.Id } });
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateAiAgentRequest req,
        AppDbContext db,
        IValidator<UpdateAiAgentRequest> validator,
        CancellationToken ct)
    {
        var v = await validator.ValidateAsync(req, ct);
        if (!v.IsValid) return ValidationFailed(v);

        var a = await db.AiAgents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound("AGENT_NOT_FOUND", "Agente não encontrado.");

        if (a.Type == AgentType.Orchestrator)
        {
            // Orchestrator accepts only: Name, Prompt, Model.
            if (req.DepartmentId.HasValue || req.ShortDescription is not null)
                return Conflict("INVALID_FIELDS_FOR_ORCHESTRATOR", "Orchestrator aceita apenas name, prompt, model.");
            if (req.IsActive == false)
                return Conflict("CANNOT_DEACTIVATE_ORCHESTRATOR", "Orchestrator não pode ser desativado.");
        }
        else
        {
            if (req.DepartmentId is { } dId)
            {
                var deptOk = await db.Departments.AsNoTracking().AnyAsync(d => d.Id == dId && d.IsActive, ct);
                if (!deptOk) return Conflict("DEPARTMENT_NOT_ACTIVE", "Departamento inativo ou inexistente.");
                a.DepartmentId = dId;
            }
            if (req.ShortDescription is not null) a.ShortDescription = req.ShortDescription;
            if (req.IsActive.HasValue) a.IsActive = req.IsActive.Value;
        }

        if (req.Name is not null) a.Name = req.Name;
        if (req.Prompt is not null) a.Prompt = req.Prompt;
        if (req.Model is not null) a.Model = req.Model;
        a.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { success = true, data = new { id = a.Id } });
    }

    private static async Task<IResult> ToggleAsync(Guid id, ToggleRequest req, AppDbContext db, CancellationToken ct)
    {
        var a = await db.AiAgents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound("AGENT_NOT_FOUND", "Agente não encontrado.");
        if (a.Type == AgentType.Orchestrator && !req.IsActive)
            return Conflict("CANNOT_DEACTIVATE_ORCHESTRATOR", "Orchestrator não pode ser desativado.");
        a.IsActive = req.IsActive;
        a.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true, data = new { id = a.Id, is_active = a.IsActive } });
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var a = await db.AiAgents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound("AGENT_NOT_FOUND", "Agente não encontrado.");
        if (a.Type == AgentType.Orchestrator)
            return Conflict("CANNOT_DELETE_ORCHESTRATOR", "Orchestrator não pode ser deletado.");

        var hasHistory = await db.AiThreads.AnyAsync(t => t.CurrentAgentId == id, ct);
        if (hasHistory)
        {
            a.IsActive = false;
            a.DeletedAt = DateTimeOffset.UtcNow;
            a.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { success = true, data = new { id, soft_deleted = true } });
        }

        db.AiAgents.Remove(a);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true, data = new { id, soft_deleted = false } });
    }

    public record ToggleRequest(bool IsActive);

    private static IResult ValidationFailed(FluentValidation.Results.ValidationResult v)
        => Results.BadRequest(new
        {
            success = false,
            error = new { code = "VALIDATION_FAILED", message = "Payload inválido.", details = v.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }) },
        });

    private static IResult NotFound(string code, string message)
        => Results.NotFound(new { success = false, error = new { code, message } });

    private static IResult Conflict(string code, string message)
        => Results.Conflict(new { success = false, error = new { code, message } });
}
