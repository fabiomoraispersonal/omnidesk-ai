namespace omniDesk.Api.Domain.Pipelines;

public class PipelineColumn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PipelineId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StatusMapping { get; set; } = string.Empty;  // "new" | "in_progress" | "waiting_client"
    public int Order { get; set; }
    public string? Color { get; set; }  // Hex #RRGGBB
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
