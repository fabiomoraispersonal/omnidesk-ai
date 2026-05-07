namespace omniDesk.Api.Infrastructure.Authentication;

public interface ICurrentUser
{
    Guid? UserId { get; }
    string Role { get; }
    string TenantSlug { get; }
    Guid? TenantId { get; }
    IReadOnlyList<Guid> DepartmentIds { get; }
    bool IsImpersonating { get; }
    bool IsAuthenticated { get; }
}
