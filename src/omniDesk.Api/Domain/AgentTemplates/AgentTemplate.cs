namespace omniDesk.Api.Domain.AgentTemplates;

public class AgentTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AgentType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Prompt { get; set; }
    public bool IsActive { get; set; } = true;
    public int UsedInProvisioningCount { get; set; } = 0;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
