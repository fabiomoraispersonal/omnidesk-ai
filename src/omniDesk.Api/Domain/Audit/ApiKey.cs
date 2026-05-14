namespace omniDesk.Api.Domain.Audit;

/// <summary>
/// Spec 012 — API Key for external integrations (e.g. Metabase).
/// Lives in <c>tenant_{slug}.api_keys</c>. Raw key is never stored — only SHA-256 hex hash.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = [];
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool Revoked { get; set; } = false;
    public DateTime CreatedAt { get; set; }
}
