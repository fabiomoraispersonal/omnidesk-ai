namespace omniDesk.Api.Domain.LiveChat;

/// <summary>
/// Anonymous web visitor identified by a UUID generated in-browser via crypto.randomUUID()
/// and persisted in localStorage. No fingerprinting (Spec 007 FR-003).
/// </summary>
public class Visitor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AnonymousId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public interface IVisitorRepository
{
    Task<Visitor?> GetByAnonymousIdAsync(Guid anonymousId, CancellationToken ct);
    Task<Visitor> CreateAsync(Visitor visitor, CancellationToken ct);
    Task UpdateIdentificationAsync(Guid visitorId, string? name, string? email, string? phone, CancellationToken ct);
}
