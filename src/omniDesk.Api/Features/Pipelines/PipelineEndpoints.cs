using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Pipelines.Commands;
using omniDesk.Api.Features.Pipelines.Queries;
using omniDesk.Api.Infrastructure.Authentication;

namespace omniDesk.Api.Features.Pipelines;

/// <summary>
/// Spec 009 US9 — T173.
/// GET  /api/pipelines                 — list all pipelines
/// GET  /api/pipelines/{id}            — detail with columns
/// PUT  /api/pipelines/{id}/columns    — update columns (tenant_admin only)
/// GET  /api/pipelines/by-dept/{deptId} — by department
/// </summary>
public static class PipelineEndpoints
{
    public static RouteGroupBuilder MapPipelineEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", DetailAsync);
        group.MapGet("/by-dept/{departmentId:guid}", ByDeptAsync);
        group.MapPut("/{id:guid}/columns", UpdateColumnsAsync);

        return group;
    }

    // GET /api/pipelines
    private static async Task<IResult> ListAsync(
        ICurrentUser caller,
        ListPipelinesQuery query,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated) return Results.Unauthorized();

        var items = await query.ExecuteAsync(ct);
        return Results.Ok(new { success = true, data = items });
    }

    // GET /api/pipelines/{id}
    private static async Task<IResult> DetailAsync(
        Guid id,
        ICurrentUser caller,
        GetPipelineWithColumnsQuery query,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated) return Results.Unauthorized();

        var data = await query.ExecuteAsync(id, ct);
        if (data is null)
            return Results.Json(Error("PIPELINE_NOT_FOUND", "Pipeline not found."), statusCode: 404);

        return Results.Ok(new { success = true, data });
    }

    // GET /api/pipelines/by-dept/{departmentId}
    private static async Task<IResult> ByDeptAsync(
        Guid departmentId,
        ICurrentUser caller,
        GetPipelineWithColumnsQuery query,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated) return Results.Unauthorized();

        var data = await query.ExecuteByDepartmentAsync(departmentId, ct);
        if (data is null)
            return Results.Json(Error("PIPELINE_NOT_FOUND", "Pipeline not found."), statusCode: 404);

        return Results.Ok(new { success = true, data });
    }

    // PUT /api/pipelines/{id}/columns (tenant_admin only)
    private static async Task<IResult> UpdateColumnsAsync(
        Guid id,
        UpdatePipelineColumnsRequest req,
        ICurrentUser caller,
        UpdatePipelineColumnsCommand command,
        GetPipelineWithColumnsQuery detailQuery,
        CancellationToken ct)
    {
        if (!caller.IsAuthenticated) return Results.Unauthorized();

        if (caller.Role is not (Roles.TenantAdmin or Roles.Supervisor))
            return Results.Json(Error("FORBIDDEN", "Only tenant_admin or supervisor can update pipeline columns."), statusCode: 403);

        var (found, error) = await command.ExecuteAsync(id, req, ct);

        if (!found)
            return Results.Json(Error("PIPELINE_NOT_FOUND", "Pipeline not found."), statusCode: 404);
        if (error is not null)
            return Results.Json(Error("VALIDATION_ERROR", error), statusCode: 400);

        var data = await detailQuery.ExecuteAsync(id, ct);
        return Results.Ok(new { success = true, data });
    }

    private static object Error(string code, string message) =>
        new { success = false, error = new { code, message } };
}
