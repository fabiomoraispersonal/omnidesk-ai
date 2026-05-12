using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Pipelines;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Pipelines;

public class PipelineRepository(AppDbContext db) : IPipelineRepository
{
    public async Task<Pipeline?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.Pipelines
            .Include(p => p.Columns)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null, ct);

    public async Task<Pipeline?> GetByDepartmentIdAsync(Guid departmentId, CancellationToken ct) =>
        await db.Pipelines
            .Include(p => p.Columns)
            .FirstOrDefaultAsync(p => p.DepartmentId == departmentId && p.DeletedAt == null, ct);

    public async Task<IReadOnlyList<Pipeline>> ListAsync(CancellationToken ct) =>
        await db.Pipelines
            .Include(p => p.Columns)
            .Where(p => p.DeletedAt == null)
            .ToListAsync(ct);

    public async Task<Pipeline> AddAsync(Pipeline pipeline, CancellationToken ct)
    {
        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync(ct);
        return pipeline;
    }

    public async Task UpdateAsync(Pipeline pipeline, CancellationToken ct)
    {
        pipeline.UpdatedAt = DateTimeOffset.UtcNow;
        db.Pipelines.Update(pipeline);
        await db.SaveChangesAsync(ct);
    }
}
