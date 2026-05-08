namespace omniDesk.Api.Infrastructure.Authorization;

/// <summary>
/// Typed helpers for every Redis key used by Specs 002–005.
/// Constitutional principle I (Multi-Tenant Isolation): every key MUST be prefixed by `{tenant_slug}:`.
/// Adding a new key without going through this helper requires an ADR.
/// </summary>
public static class RedisKeys
{
    public static string ClaimsCache(string tenantSlug, Guid userId) =>
        Require(tenantSlug) + $":user:{userId}:claims";

    public static string RefreshToken(string tenantSlug, Guid userId, string tokenId) =>
        Require(tenantSlug) + $":refresh:{userId}:{tokenId}";

    // Spec 005 — presence
    public static string AttendantStatus(string tenantSlug, Guid attendantId) =>
        Require(tenantSlug) + $":attendant_status:{attendantId}";

    // Spec 005 — round-robin cursor per department
    public static string RoundRobin(string tenantSlug, Guid departmentId) =>
        Require(tenantSlug) + $":rr:{departmentId}";

    // Spec 005 — ticket assignment lock
    public static string TicketLock(string tenantSlug, Guid ticketId) =>
        Require(tenantSlug) + $":ticket_lock:{ticketId}";

    // Spec 005 — WebSocket pub/sub channels (research §R4)
    public static string WsTenant(string tenantSlug) =>
        Require(tenantSlug) + ":ws:tenant";

    public static string WsDepartment(string tenantSlug, Guid departmentId) =>
        Require(tenantSlug) + $":ws:dept:{departmentId}";

    public static string WsAttendant(string tenantSlug, Guid attendantId) =>
        Require(tenantSlug) + $":ws:attendant:{attendantId}";

    private static string Require(string tenantSlug)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug))
            throw new ArgumentException(
                "Redis key requires a non-empty tenant_slug (Constitution §I).",
                nameof(tenantSlug));
        return tenantSlug;
    }
}
