using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Authorization.Policies;
using Xunit;

namespace omniDesk.Api.Tests.Features.Authorization;

/// <summary>
/// Spec 004 / US1 — paramétrico cobrindo TODAS as células da matriz (4.1–4.12).
/// Cada caso autentica um principal com role X e exercita IAuthorizationService.AuthorizeAsync
/// contra a policy Y, verificando o veredito esperado.
/// </summary>
public class PolicyMatrixTests
{
    private static readonly IAuthorizationService _authz = BuildService();

    private static IAuthorizationService BuildService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTransient<IAuthorizationHandler, RoleRequirementHandler>();
        services.AddTransient<IAuthorizationHandler, ForbidsDuringImpersonationHandler>();
        services.AddTransient<IAuthorizationHandler, DepartmentScopeHandler>();
        AuthorizationPoliciesRegistration.Register(services);
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Principal(string role, bool impersonating = false)
    {
        var id = new ClaimsIdentity(authenticationType: "Test");
        id.AddClaim(new Claim("role", role));
        if (impersonating) id.AddClaim(new Claim("impersonating", "true"));
        return new ClaimsPrincipal(id);
    }

    public static IEnumerable<object[]> Matrix()
    {
        // 4.1 Painel Admin
        yield return new object[] { Policies.PainelAdminAccess, Roles.SaasAdmin, true };
        yield return new object[] { Policies.PainelAdminAccess, Roles.TenantAdmin, false };
        yield return new object[] { Policies.PainelAdminAccess, Roles.Supervisor, false };
        yield return new object[] { Policies.PainelAdminAccess, Roles.Attendant, false };

        // 4.2 Departamentos / Atendentes — exact tenant_admin
        foreach (var p in new[] { Policies.CanCreateDepartment, Policies.CanEditDepartment })
        {
            yield return new object[] { p, Roles.TenantAdmin, true };
            yield return new object[] { p, Roles.Supervisor, false };
            yield return new object[] { p, Roles.Attendant, false };
        }
        // List = qualquer crm role
        yield return new object[] { Policies.CanListDepartments, Roles.TenantAdmin, true };
        yield return new object[] { Policies.CanListDepartments, Roles.Supervisor, true };
        yield return new object[] { Policies.CanListDepartments, Roles.Attendant, true };

        // Attendants management = supervisor+
        foreach (var p in new[] { Policies.CanCreateAttendant, Policies.CanEditAttendant,
                                  Policies.CanDeactivateAttendant, Policies.CanViewAnyAttendantTickets })
        {
            yield return new object[] { p, Roles.TenantAdmin, true };
            yield return new object[] { p, Roles.Supervisor, true };
            yield return new object[] { p, Roles.Attendant, false };
        }

        // 4.3 Widget
        yield return new object[] { Policies.CanViewWidgetConfig, Roles.Attendant, true };
        yield return new object[] { Policies.CanEditWidgetAppearance, Roles.Supervisor, true };
        yield return new object[] { Policies.CanEditWidgetAppearance, Roles.Attendant, false };
        yield return new object[] { Policies.CanEditAuthorizedDomains, Roles.TenantAdmin, true };
        yield return new object[] { Policies.CanEditAuthorizedDomains, Roles.Supervisor, false };
        yield return new object[] { Policies.CanToggleWidget, Roles.TenantAdmin, true };
        yield return new object[] { Policies.CanToggleWidget, Roles.Supervisor, false };

        // 4.4 Conversas
        yield return new object[] { Policies.CanViewAllConversations, Roles.Supervisor, true };
        yield return new object[] { Policies.CanViewAllConversations, Roles.Attendant, false };

        // 4.5 Agentes
        yield return new object[] { Policies.CanViewAgents, Roles.Attendant, true };
        yield return new object[] { Policies.CanEditOrchestrator, Roles.Supervisor, true };
        yield return new object[] { Policies.CanEditOrchestrator, Roles.Attendant, false };
        yield return new object[] { Policies.CanManageSubAgents, Roles.Supervisor, true };
        yield return new object[] { Policies.CanUseAgentPlayground, Roles.Supervisor, true };
        yield return new object[] { Policies.CanEditAgentAdvancedConfig, Roles.TenantAdmin, true };
        yield return new object[] { Policies.CanEditAgentAdvancedConfig, Roles.Supervisor, false };

        // 4.6 Tickets
        yield return new object[] { Policies.CanViewAllTickets, Roles.Supervisor, true };
        yield return new object[] { Policies.CanViewAllTickets, Roles.Attendant, false };
        yield return new object[] { Policies.CanConfigurePipelineColumns, Roles.TenantAdmin, true };
        yield return new object[] { Policies.CanConfigurePipelineColumns, Roles.Supervisor, false };

        // 4.7 Contatos
        yield return new object[] { Policies.CanManageContacts, Roles.TenantAdmin, true };
        yield return new object[] { Policies.CanManageContacts, Roles.Supervisor, true };
        yield return new object[] { Policies.CanManageContacts, Roles.Attendant, true };

        // 4.8 WhatsApp
        yield return new object[] { Policies.CanViewChannelStatus, Roles.Attendant, true };
        yield return new object[] { Policies.CanEditChannelConfig, Roles.TenantAdmin, true };
        yield return new object[] { Policies.CanEditChannelConfig, Roles.Supervisor, false };
        yield return new object[] { Policies.CanViewAccessToken, Roles.TenantAdmin, true };
        yield return new object[] { Policies.CanViewAccessToken, Roles.Supervisor, false };
        yield return new object[] { Policies.CanToggleChannel, Roles.TenantAdmin, true };
        yield return new object[] { Policies.CanToggleChannel, Roles.Supervisor, false };
        yield return new object[] { Policies.CanManageTemplates, Roles.Supervisor, true };
        yield return new object[] { Policies.CanManageTemplates, Roles.Attendant, false };

        // 4.9 Notificações
        yield return new object[] { Policies.CanConfigureClientNotifications, Roles.Supervisor, true };
        yield return new object[] { Policies.CanConfigureClientNotifications, Roles.Attendant, false };
        yield return new object[] { Policies.CanViewOwnNotifications, Roles.Attendant, true };
        yield return new object[] { Policies.CanMarkNotificationAsRead, Roles.Attendant, true };
        yield return new object[] { Policies.CanConfigurePushPreferences, Roles.Attendant, true };

        // 4.10 Agenda
        yield return new object[] { Policies.CanManageProfessionals, Roles.Supervisor, true };
        yield return new object[] { Policies.CanManageProfessionals, Roles.Attendant, false };
        yield return new object[] { Policies.CanManageServiceCatalog, Roles.Supervisor, true };
        yield return new object[] { Policies.CanConfigureAvailability, Roles.Supervisor, true };
        yield return new object[] { Policies.CanConfigureCancellationPolicy, Roles.TenantAdmin, true };
        yield return new object[] { Policies.CanConfigureCancellationPolicy, Roles.Supervisor, false };

        // 4.11 Auditoria
        yield return new object[] { Policies.CanViewAuditActivity, Roles.TenantAdmin, true };
        yield return new object[] { Policies.CanViewAuditActivity, Roles.Supervisor, false };
        yield return new object[] { Policies.CanManageAuditApiKeys, Roles.TenantAdmin, true };
        yield return new object[] { Policies.CanManageAuditApiKeys, Roles.Supervisor, false };

        // 4.12 Auth
        yield return new object[] { Policies.CanInviteUser, Roles.Supervisor, true };
        yield return new object[] { Policies.CanInviteUser, Roles.Attendant, false };
        yield return new object[] { Policies.CanInviteSupervisor, Roles.TenantAdmin, true };
        yield return new object[] { Policies.CanInviteSupervisor, Roles.Supervisor, false };
        yield return new object[] { Policies.CanDeactivateUser, Roles.TenantAdmin, true };
        yield return new object[] { Policies.CanDeactivateUser, Roles.Supervisor, false };
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task PolicyMatrix_EnforcesContract(string policy, string role, bool expected)
    {
        var result = await _authz.AuthorizeAsync(Principal(role), null, policy);
        Assert.True(result.Succeeded == expected,
            $"Policy={policy} Role={role} expected={expected} actual={result.Succeeded}");
    }

    [Fact]
    public async Task ImpersonatingSaasAdmin_PassesCrmTenantAdminPolicies()
    {
        // CanCreateDepartment requires exact tenant_admin; impersonation must satisfy it.
        var result = await _authz.AuthorizeAsync(
            Principal(Roles.SaasAdmin, impersonating: true), null,
            Policies.CanCreateDepartment);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ImpersonatingSaasAdmin_FailsForbidsDuringImpersonationPolicies()
    {
        var blocked = new[]
        {
            Policies.CanInviteUser,
            Policies.CanInviteSupervisor,
            Policies.CanDeactivateUser,
            Policies.CanViewAccessToken,
            Policies.CanEditChannelConfig,
        };
        foreach (var p in blocked)
        {
            var result = await _authz.AuthorizeAsync(
                Principal(Roles.SaasAdmin, impersonating: true), null, p);
            Assert.False(result.Succeeded, $"Policy {p} should be blocked during impersonation.");
        }
    }
}
