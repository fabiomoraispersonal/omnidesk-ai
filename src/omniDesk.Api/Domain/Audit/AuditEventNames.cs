namespace omniDesk.Api.Domain.Audit;

/// <summary>Spec 012 — all 29 auditable event names. No magic strings elsewhere.</summary>
public static class AuditEventNames
{
    // Auth (9)
    public const string AuthLoginSuccess         = "auth.login_success";
    public const string AuthLoginFailed          = "auth.login_failed";
    public const string AuthLogout               = "auth.logout";
    public const string AuthPasswordChanged      = "auth.password_changed";
    public const string AuthPasswordReset        = "auth.password_reset";
    public const string AuthTotpEnabled          = "auth.totp_enabled";
    public const string AuthTotpDisabled         = "auth.totp_disabled";
    public const string AuthImpersonationStarted = "auth.impersonation_started";
    public const string AuthImpersonationEnded   = "auth.impersonation_ended";

    // Users (5)
    public const string UserInvited          = "user.invited";
    public const string UserInviteAccepted   = "user.invite_accepted";
    public const string UserDeactivated      = "user.deactivated";
    public const string UserReactivated      = "user.reactivated";
    public const string UserRoleChanged      = "user.role_changed";

    // Tickets (5)
    public const string TicketCreated       = "ticket.created";
    public const string TicketAssigned      = "ticket.assigned";
    public const string TicketTransferred   = "ticket.transferred";
    public const string TicketStatusChanged = "ticket.status_changed";
    public const string TicketCancelled     = "ticket.cancelled";

    // Appointments (4)
    public const string AppointmentCreated   = "appointment.created";
    public const string AppointmentConfirmed = "appointment.confirmed";
    public const string AppointmentCancelled = "appointment.cancelled";
    public const string AppointmentNoShow    = "appointment.no_show";

    // Tenant Config (6)
    public const string TenantWhatsappConfigured = "tenant.whatsapp_configured";
    public const string TenantOpenAiKeyChanged   = "tenant.openai_key_changed";
    public const string TenantPlanChanged        = "tenant.plan_changed";
    public const string AiAgentCreated           = "ai_agent.created";
    public const string AiAgentUpdated           = "ai_agent.updated";
    public const string AiAgentDeleted           = "ai_agent.deleted";
}
