using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Pipelines;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.Pipelines;

/// <summary>
/// Spec 009 T056 — ensures every department has exactly one pipeline with the 3 default columns.
/// Idempotent: safe to call multiple times for the same department.
/// </summary>
public class PipelineProvisioningService(AppDbContext db)
{
    public async Task EnsurePipelineForDepartmentAsync(Guid departmentId, CancellationToken ct = default)
    {
        var exists = await db.Pipelines
            .AsNoTracking()
            .AnyAsync(p => p.DepartmentId == departmentId && p.DeletedAt == null, ct);

        if (exists) return;

        var now = DateTimeOffset.UtcNow;
        var pipeline = new Pipeline
        {
            Id = Guid.NewGuid(),
            DepartmentId = departmentId,
            Name = "Pipeline",
            CreatedAt = now,
            UpdatedAt = now,
        };

        var columns = PipelineDefaults.DefaultColumns
            .Select(col => new PipelineColumn
            {
                Id = Guid.NewGuid(),
                PipelineId = pipeline.Id,
                Name = col.Name,
                StatusMapping = col.StatusMapping,
                Order = col.Order,
                CreatedAt = now,
                UpdatedAt = now,
            })
            .ToList();

        pipeline.Columns = columns;
        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync(ct);
    }
}
