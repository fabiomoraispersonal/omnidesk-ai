namespace omniDesk.Api.Domain.Tenants;

public class TenantContact
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public ContactType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    public Tenant Tenant { get; set; } = null!;
}
