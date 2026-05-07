namespace omniDesk.Api.Domain.Authorization;

public static class RoleHierarchy
{
    private static readonly Dictionary<string, int> Rank = new()
    {
        [Roles.TenantAdmin] = 3,
        [Roles.Supervisor] = 2,
        [Roles.Attendant] = 1,
    };

    public static bool IsAtLeast(string? actual, string? minimum)
    {
        if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(minimum))
            return false;
        return Rank.TryGetValue(actual, out var a)
            && Rank.TryGetValue(minimum, out var m)
            && a >= m;
    }

    public static int? RankOf(string? role) =>
        role is not null && Rank.TryGetValue(role, out var r) ? r : null;
}
