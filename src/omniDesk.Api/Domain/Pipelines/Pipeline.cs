namespace omniDesk.Api.Domain.Pipelines;

public class Pipeline
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DepartmentId { get; set; }
    public string Name { get; set; } = "Pipeline";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }

    public List<PipelineColumn> Columns { get; set; } = [];
}
