using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AgentTemplates;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Admin.AgentTemplates;

public static class AgentTemplatesEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var templates = group.MapGroup("/agent-templates");

        templates.MapGet("/", ListAsync).WithName("ListAgentTemplates");
        templates.MapPost("/", CreateAsync).WithName("CreateAgentTemplate");
        templates.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateAgentTemplate");
        templates.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteAgentTemplate");
    }

    private static async Task<IResult> ListAsync(
        bool? activeOnly,
        AppDbContext db,
        CancellationToken ct)
    {
        var query = db.AgentTemplates.AsQueryable();
        if (activeOnly == true) query = query.Where(t => t.IsActive);

        var templates = await query.OrderBy(t => t.CreatedAt).ToListAsync(ct);
        return Results.Ok(templates.Select(MapResponse));
    }

    private static async Task<IResult> CreateAsync(
        CreateAgentTemplateRequest req,
        AppDbContext db,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(req.Name)) errors["name"] = ["Name is required."];
        if (string.IsNullOrWhiteSpace(req.Description)) errors["description"] = ["Description is required."];
        if (!Enum.TryParse<AgentType>(req.Type, ignoreCase: true, out var agentType))
            errors["type"] = ["Type must be 'Orchestrator' or 'SubAgent'."];

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var now = DateTimeOffset.UtcNow;
        var template = new AgentTemplate
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Type = agentType,
            Description = req.Description,
            Prompt = req.Prompt,
            IsActive = true,
            UsedInProvisioningCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.AgentTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/admin/agent-templates/{template.Id}", MapResponse(template));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateAgentTemplateRequest req,
        AppDbContext db,
        CancellationToken ct)
    {
        var template = await db.AgentTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return Results.NotFound();

        if (!string.IsNullOrWhiteSpace(req.Name)) template.Name = req.Name;
        if (!string.IsNullOrWhiteSpace(req.Description)) template.Description = req.Description;
        if (req.Prompt is not null) template.Prompt = req.Prompt;
        if (req.IsActive.HasValue) template.IsActive = req.IsActive.Value;

        template.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(MapResponse(template));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        AppDbContext db,
        CancellationToken ct)
    {
        var template = await db.AgentTemplates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (template is null) return Results.NotFound();
        if (template.DeletedAt.HasValue)
            return Results.Conflict(new { code = "already_deleted", message = "Template already deleted." });

        if (template.UsedInProvisioningCount == 0)
        {
            db.AgentTemplates.Remove(template);
        }
        else
        {
            template.DeletedAt = DateTimeOffset.UtcNow;
            template.IsActive = false;
            template.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static object MapResponse(AgentTemplate t) => new
    {
        id = t.Id,
        name = t.Name,
        type = t.Type.ToString().ToLowerInvariant() == "subagent" ? "sub_agent" : t.Type.ToString().ToLowerInvariant(),
        description = t.Description,
        prompt = t.Prompt,
        is_active = t.IsActive,
        used_in_provisioning_count = t.UsedInProvisioningCount,
        created_at = t.CreatedAt,
        updated_at = t.UpdatedAt
    };
}

public record CreateAgentTemplateRequest(string Name, string Type, string Description, string? Prompt);
public record UpdateAgentTemplateRequest(string? Name, string? Description, string? Prompt, bool? IsActive);
