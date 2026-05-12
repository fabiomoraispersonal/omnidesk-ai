namespace omniDesk.Api.Domain.Pipelines;

public interface IPipelineRepository
{
    Task<Pipeline?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Pipeline?> GetByDepartmentIdAsync(Guid departmentId, CancellationToken ct);
    Task<IReadOnlyList<Pipeline>> ListAsync(CancellationToken ct);
    Task<Pipeline> AddAsync(Pipeline pipeline, CancellationToken ct);
    Task UpdateAsync(Pipeline pipeline, CancellationToken ct);
}
