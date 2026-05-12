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

    // Spec 009 — CRM WebSocket pub/sub channels
    public static string CrmDepartment(string tenantSlug, Guid departmentId) =>
        Require(tenantSlug) + $":crm:dept:{departmentId}";

    public static string CrmSupervisor(string tenantSlug) =>
        Require(tenantSlug) + ":crm:supervisor";

    // Spec 009 — SLA warning idempotency flags (TTL 24h set by TicketSlaMonitorJob)
    public static string SlaWarnedFlag(string tenantSlug, Guid ticketId, string slaType) =>
        Require(tenantSlug) + $":ticket:{ticketId}:sla_warned:{slaType}";

    // Spec 009 — Contact dedup lock
    public static string ContactDedupLock(string tenantSlug, string key) =>
        Require(tenantSlug) + $":contact:dedup:lock:{key}";

    // Spec 010 — TicketQueueMonitorJob idempotency flag (FR-009, TTL 1h via SETNX).
    public static string NotificationQueueAlert(string tenantSlug, Guid ticketId) =>
        Require(tenantSlug) + $":queue_alert:{ticketId}";

    // Spec 010 — AppointmentReminderJob idempotency flag (FR-018, TTL 48h via SETNX).
    public static string ReminderSent(string tenantSlug, Guid appointmentId, string dateYyyyMmDd) =>
        Require(tenantSlug) + $":reminder_sent:{appointmentId}:{dateYyyyMmDd}";

    // Spec 010 — silence-rule (FR-010): which ticket the attendant has open. TTL 60s, heartbeat 30s.
    public static string AttendantActiveTicket(string tenantSlug, Guid attendantId) =>
        Require(tenantSlug) + $":attendant_active_ticket:{attendantId}";

    private static string Require(string tenantSlug)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug))
            throw new ArgumentException(
                "Redis key requires a non-empty tenant_slug (Constitution §I).",
                nameof(tenantSlug));
        return tenantSlug;
    }
}
