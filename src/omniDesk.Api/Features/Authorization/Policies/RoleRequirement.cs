using Microsoft.AspNetCore.Authorization;

namespace omniDesk.Api.Features.Authorization.Policies;

public class RoleRequirement : IAuthorizationRequirement
{
    public string MinimumRole { get; }
    public bool Exact { get; }

    public RoleRequirement(string minimumRole, bool exact = false)
    {
        if (string.IsNullOrWhiteSpace(minimumRole))
            throw new ArgumentException("MinimumRole is required.", nameof(minimumRole));
        MinimumRole = minimumRole;
        Exact = exact;
    }
}
