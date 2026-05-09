namespace omniDesk.Api.Domain.AiSettings;

public class AiSettings
{
    public const int DefaultContextWindowMessages = 20;
    public const int MinContextWindowMessages = 5;
    public const int MaxContextWindowMessages = 100;

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public int ContextWindowMessages { get; set; } = DefaultContextWindowMessages;
    public string[] AvailableModels { get; set; } = Array.Empty<string>();
    public DateTimeOffset UpdatedAt { get; set; }
}

public interface IAiSettingsRepository
{
    Task<AiSettings?> GetForTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<AiSettings> GetOrCreateAsync(Guid tenantId, CancellationToken ct = default);
    Task UpdateAsync(AiSettings settings, CancellationToken ct = default);
}
