using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Pipelines.Queries;

/// <summary>
/// Spec 009 US9 — T171.
/// Returns a pipeline with its 3 columns, ordered by column order.
/// </summary>
public class GetPipelineWithColumnsQuery(AppDbContext db)
{
    public async Task<object?> ExecuteAsync(Guid pipelineId, CancellationToken ct)
    {
        var pipeline = await db.Pipelines
            .AsNoTracking()
            .Include(p => p.Columns)
            .Where(p => p.Id == pipelineId && p.DeletedAt == null)
            .Select(p => new
            {
                id            = p.Id,
                department_id = p.DepartmentId,
                name          = p.Name,
                columns       = p.Columns
                    .OrderBy(c => c.Order)
                    .Select(c => new
                    {
                        id             = c.Id,
                        name           = c.Name,
                        status_mapping = c.StatusMapping,
                        order          = c.Order,
                        color          = c.Color,
                    })
                    .ToList(),
                created_at = p.CreatedAt,
                updated_at = p.UpdatedAt,
            })
            .FirstOrDefaultAsync(ct);

        return pipeline;
    }

    public async Task<object?> ExecuteByDepartmentAsync(Guid departmentId, CancellationToken ct)
    {
        var pipeline = await db.Pipelines
            .AsNoTracking()
            .Include(p => p.Columns)
            .Where(p => p.DepartmentId == departmentId && p.DeletedAt == null)
            .Select(p => new
            {
                id            = p.Id,
                department_id = p.DepartmentId,
                name          = p.Name,
                columns       = p.Columns
                    .OrderBy(c => c.Order)
                    .Select(c => new
                    {
                        id             = c.Id,
                        name           = c.Name,
                        status_mapping = c.StatusMapping,
                        order          = c.Order,
                        color          = c.Color,
                    })
                    .ToList(),
                created_at = p.CreatedAt,
                updated_at = p.UpdatedAt,
            })
            .FirstOrDefaultAsync(ct);

        return pipeline;
    }
}

/// <summary>
/// Spec 009 US9 — T171.
/// Lists all active pipelines.
/// </summary>
public class ListPipelinesQuery(AppDbContext db)
{
    public async Task<IReadOnlyList<object>> ExecuteAsync(CancellationToken ct)
    {
        var pipelines = await db.Pipelines
            .AsNoTracking()
            .Include(p => p.Columns)
            .Where(p => p.DeletedAt == null)
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                id            = p.Id,
                department_id = p.DepartmentId,
                name          = p.Name,
                columns_count = p.Columns.Count,
                updated_at    = p.UpdatedAt,
            })
            .ToListAsync(ct);

        return pipelines;
    }
}
