namespace omniDesk.Api.Domain.Authorization;

public static class Policies
{
    // 4.1 Painel Admin
    public const string PainelAdminAccess = "PainelAdmin.Access";

    // 4.2 Departamentos / Atendentes
    public const string CanCreateDepartment = "Departments.Create";
    public const string CanEditDepartment = "Departments.Edit";
    public const string CanListDepartments = "Departments.List";
    public const string CanCreateAttendant = "Attendants.Create";
    public const string CanEditAttendant = "Attendants.Edit";
    public const string CanDeactivateAttendant = "Attendants.Deactivate";
    public const string CanViewAnyAttendantTickets = "Attendants.ViewAnyTickets";

    // 4.3 Live Chat — Widget
    public const string CanViewWidgetConfig = "Widget.View";
    public const string CanEditWidgetAppearance = "Widget.EditAppearance";
    public const string CanEditAuthorizedDomains = "Widget.EditDomains";
    public const string CanToggleWidget = "Widget.Toggle";

    // 4.4 Live Chat — Conversas
    public const string CanViewAllConversations = "Conversations.ViewAll";

    // 4.5 Agentes de IA
    public const string CanViewAgents = "Agents.View";
    public const string CanEditOrchestrator = "Agents.EditOrchestrator";
    public const string CanManageSubAgents = "Agents.ManageSubAgents";
    public const string CanUseAgentPlayground = "Agents.UsePlayground";
    public const string CanEditAgentAdvancedConfig = "Agents.EditAdvancedConfig";

    // 4.6 Tickets
    public const string CanViewAllTickets = "Tickets.ViewAll";
    public const string CanConfigurePipelineColumns = "Tickets.ConfigurePipeline";

    // 4.7 Contatos
    public const string CanManageContacts = "Contacts.Manage";

    // 4.8 WhatsApp
    public const string CanViewChannelStatus = "Whatsapp.ViewStatus";
    public const string CanEditChannelConfig = "Whatsapp.EditConfig";
    public const string CanViewAccessToken = "Whatsapp.ViewAccessToken";
    public const string CanToggleChannel = "Whatsapp.Toggle";
    public const string CanManageTemplates = "Whatsapp.ManageTemplates";

    // 4.9 Notificações
    public const string CanConfigureClientNotifications = "Notifications.ConfigureForClients";
    public const string CanViewOwnNotifications = "Notifications.ViewOwn";
    public const string CanMarkNotificationAsRead = "Notifications.MarkAsRead";
    public const string CanConfigurePushPreferences = "Notifications.ConfigurePushPreferences";

    // 4.10 Agenda
    public const string CanManageProfessionals = "Agenda.ManageProfessionals";
    public const string CanManageServiceCatalog = "Agenda.ManageServices";
    public const string CanConfigureAvailability = "Agenda.ConfigureAvailability";
    public const string CanConfigureCancellationPolicy = "Agenda.ConfigureCancellationPolicy";

    // 4.10b Agendamentos (Spec 011) — visibilidade filtrada por IAppointmentVisibilityPolicy
    public const string CanViewAppointments = "Appointments.View";
    public const string CanManageAppointments = "Appointments.Manage";

    // 4.11 Auditoria
    public const string CanViewAuditActivity = "Audit.ViewActivity";
    public const string CanManageAuditApiKeys = "Audit.ManageApiKeys";

    // 4.12 Auth / Usuários
    public const string CanInviteUser = "Auth.InviteUser";
    public const string CanInviteSupervisor = "Auth.InviteSupervisor";
    public const string CanDeactivateUser = "Auth.DeactivateUser";

    public static readonly IReadOnlyList<string> All =
    [
        PainelAdminAccess,
        CanCreateDepartment, CanEditDepartment, CanListDepartments,
        CanCreateAttendant, CanEditAttendant, CanDeactivateAttendant, CanViewAnyAttendantTickets,
        CanViewWidgetConfig, CanEditWidgetAppearance, CanEditAuthorizedDomains, CanToggleWidget,
        CanViewAllConversations,
        CanViewAgents, CanEditOrchestrator, CanManageSubAgents, CanUseAgentPlayground, CanEditAgentAdvancedConfig,
        CanViewAllTickets, CanConfigurePipelineColumns,
        CanManageContacts,
        CanViewChannelStatus, CanEditChannelConfig, CanViewAccessToken, CanToggleChannel, CanManageTemplates,
        CanConfigureClientNotifications, CanViewOwnNotifications, CanMarkNotificationAsRead, CanConfigurePushPreferences,
        CanManageProfessionals, CanManageServiceCatalog, CanConfigureAvailability, CanConfigureCancellationPolicy,
        CanViewAppointments, CanManageAppointments,
        CanViewAuditActivity, CanManageAuditApiKeys,
        CanInviteUser, CanInviteSupervisor, CanDeactivateUser
    ];
}
