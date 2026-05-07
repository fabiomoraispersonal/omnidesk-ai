namespace omniDesk.Api.Domain.Users;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public Guid? TenantId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool EmailVerified { get; set; } = false;
    public string? TotpSecret { get; set; }
    public bool TotpEnabled { get; set; } = false;
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
    public string Name { get; set; } = string.Empty;
}
