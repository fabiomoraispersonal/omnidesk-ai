namespace omniDesk.Api.Domain.Authorization;

public static class Roles
{
    public const string SaasAdmin = "saas_admin";
    public const string TenantAdmin = "tenant_admin";
    public const string Supervisor = "supervisor";
    public const string Attendant = "attendant";

    public static readonly IReadOnlyList<string> AllCrmRoles =
        [TenantAdmin, Supervisor, Attendant];

    public static readonly IReadOnlyList<string> All =
        [SaasAdmin, TenantAdmin, Supervisor, Attendant];

    public static string FromUserRole(omniDesk.Api.Domain.Users.UserRole r) => r switch
    {
        omniDesk.Api.Domain.Users.UserRole.SaasAdmin => SaasAdmin,
        omniDesk.Api.Domain.Users.UserRole.TenantAdmin => TenantAdmin,
        omniDesk.Api.Domain.Users.UserRole.Supervisor => Supervisor,
        omniDesk.Api.Domain.Users.UserRole.Attendant => Attendant,
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, "Unknown UserRole")
    };

    public static string Normalize(string? roleClaim)
    {
        if (string.IsNullOrWhiteSpace(roleClaim)) return string.Empty;
        return roleClaim switch
        {
            "SaasAdmin" => SaasAdmin,
            "TenantAdmin" => TenantAdmin,
            "Supervisor" => Supervisor,
            "Attendant" => Attendant,
            _ => roleClaim.ToLowerInvariant()
        };
    }
}
