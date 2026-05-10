using Microsoft.AspNetCore.Authorization;
using omniDesk.Api.Domain.Authorization;

namespace omniDesk.Api.Features.Authorization.Authz;

public static class AuthorizationPoliciesRegistration
{
    public static IServiceCollection Register(IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            // 4.1 Painel Admin — exclusive to saas_admin (impersonation does NOT grant access).
            .AddPolicy(Policies.PainelAdminAccess, p => p.AddRequirements(
                new RoleRequirement(Roles.SaasAdmin, exact: true)))

            // 4.2 Departamentos / Atendentes
            .AddPolicy(Policies.CanCreateDepartment, p => p.AddRequirements(
                new RoleRequirement(Roles.TenantAdmin, exact: true)))
            .AddPolicy(Policies.CanEditDepartment, p => p.AddRequirements(
                new RoleRequirement(Roles.TenantAdmin, exact: true)))
            .AddPolicy(Policies.CanListDepartments, p => p.AddRequirements(
                new RoleRequirement(Roles.Attendant)))
            .AddPolicy(Policies.CanCreateAttendant, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))
            .AddPolicy(Policies.CanEditAttendant, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))
            .AddPolicy(Policies.CanDeactivateAttendant, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))
            .AddPolicy(Policies.CanViewAnyAttendantTickets, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))

            // 4.3 Live Chat — Widget
            .AddPolicy(Policies.CanViewWidgetConfig, p => p.AddRequirements(
                new RoleRequirement(Roles.Attendant)))
            .AddPolicy(Policies.CanEditWidgetAppearance, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))
            .AddPolicy(Policies.CanEditAuthorizedDomains, p => p.AddRequirements(
                new RoleRequirement(Roles.TenantAdmin, exact: true)))
            .AddPolicy(Policies.CanToggleWidget, p => p.AddRequirements(
                new RoleRequirement(Roles.TenantAdmin, exact: true)))

            // 4.4 Conversas
            .AddPolicy(Policies.CanViewAllConversations, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))

            // 4.5 Agentes de IA
            .AddPolicy(Policies.CanViewAgents, p => p.AddRequirements(
                new RoleRequirement(Roles.Attendant)))
            .AddPolicy(Policies.CanEditOrchestrator, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))
            .AddPolicy(Policies.CanManageSubAgents, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))
            .AddPolicy(Policies.CanUseAgentPlayground, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))
            .AddPolicy(Policies.CanEditAgentAdvancedConfig, p => p.AddRequirements(
                new RoleRequirement(Roles.TenantAdmin, exact: true)))

            // 4.6 Tickets
            .AddPolicy(Policies.CanViewAllTickets, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))
            .AddPolicy(Policies.CanConfigurePipelineColumns, p => p.AddRequirements(
                new RoleRequirement(Roles.TenantAdmin, exact: true)))

            // 4.7 Contatos
            .AddPolicy(Policies.CanManageContacts, p => p.AddRequirements(
                new RoleRequirement(Roles.Attendant)))

            // 4.8 WhatsApp
            .AddPolicy(Policies.CanViewChannelStatus, p => p.AddRequirements(
                new RoleRequirement(Roles.Attendant)))
            .AddPolicy(Policies.CanEditChannelConfig, p => p.AddRequirements(
                new RoleRequirement(Roles.TenantAdmin, exact: true),
                new ForbidsDuringImpersonationRequirement()))
            .AddPolicy(Policies.CanViewAccessToken, p => p.AddRequirements(
                new RoleRequirement(Roles.TenantAdmin, exact: true),
                new ForbidsDuringImpersonationRequirement()))
            .AddPolicy(Policies.CanToggleChannel, p => p.AddRequirements(
                new RoleRequirement(Roles.TenantAdmin, exact: true)))
            .AddPolicy(Policies.CanManageTemplates, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))

            // 4.9 Notificações
            .AddPolicy(Policies.CanConfigureClientNotifications, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))
            .AddPolicy(Policies.CanViewOwnNotifications, p => p.AddRequirements(
                new RoleRequirement(Roles.Attendant)))
            .AddPolicy(Policies.CanMarkNotificationAsRead, p => p.AddRequirements(
                new RoleRequirement(Roles.Attendant)))
            .AddPolicy(Policies.CanConfigurePushPreferences, p => p.AddRequirements(
                new RoleRequirement(Roles.Attendant)))

            // 4.10 Agenda
            .AddPolicy(Policies.CanManageProfessionals, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))
            .AddPolicy(Policies.CanManageServiceCatalog, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))
            .AddPolicy(Policies.CanConfigureAvailability, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor)))
            .AddPolicy(Policies.CanConfigureCancellationPolicy, p => p.AddRequirements(
                new RoleRequirement(Roles.TenantAdmin, exact: true)))

            // 4.11 Auditoria
            .AddPolicy(Policies.CanViewAuditActivity, p => p.AddRequirements(
                new RoleRequirement(Roles.TenantAdmin, exact: true)))
            .AddPolicy(Policies.CanManageAuditApiKeys, p => p.AddRequirements(
                new RoleRequirement(Roles.TenantAdmin, exact: true)))

            // 4.12 Auth / Usuários
            .AddPolicy(Policies.CanInviteUser, p => p.AddRequirements(
                new RoleRequirement(Roles.Supervisor),
                new ForbidsDuringImpersonationRequirement()))
            .AddPolicy(Policies.CanInviteSupervisor, p => p.AddRequirements(
                new RoleRequirement(Roles.TenantAdmin, exact: true),
                new ForbidsDuringImpersonationRequirement()))
            .AddPolicy(Policies.CanDeactivateUser, p => p.AddRequirements(
                new RoleRequirement(Roles.TenantAdmin, exact: true),
                new ForbidsDuringImpersonationRequirement()));

        return services;
    }
}
