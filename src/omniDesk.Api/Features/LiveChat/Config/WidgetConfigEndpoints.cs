using FluentValidation;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.LiveChat.Config.Commands;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.LiveChat.Config;

/// <summary>
/// Spec 007 — CRM admin surface mounted at <c>/api/widget/config</c>.
/// JWT-authenticated; tenant_admin role enforced (V1 scope per contract R11).
/// </summary>
public static class WidgetConfigEndpoints
{
    public static RouteGroupBuilder MapWidgetConfigEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetAsync);
        group.MapPut("/", PutAsync);
        group.MapPatch("/toggle", ToggleAsync);
        return group;
    }

    private static async Task<IResult> GetAsync(
        HttpContext http,
        AppDbContext db,
        IWidgetConfigRepository configs,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId(http);
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return Results.NotFound(Error("TENANT_NOT_FOUND", "Tenant not found."));

        var config = await configs.GetByTenantAsync(tenantId, ct);
        if (config is null) return Results.NotFound(Error("WIDGET_CONFIG_NOT_FOUND", "Widget config not provisioned."));

        var cdnBase = configuration["Widget:CdnBaseUrl"] ?? "https://cdn.omnicare.ia.br/widget/v1";
        var snippet = BuildInstallationSnippet(tenant.WidgetToken, cdnBase);

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                widget_token = tenant.WidgetToken,
                installation_snippet = snippet,
                config = SerializeConfig(config),
            },
        });
    }

    private static async Task<IResult> PutAsync(
        UpdateWidgetConfigRequest request,
        HttpContext http,
        UpdateWidgetConfigCommand command,
        IValidator<UpdateWidgetConfigRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.Json(new
            {
                success = false,
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Validation failed.",
                    details = validation.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage }),
                },
            }, statusCode: 400);
        }

        var tenantId = ResolveTenantId(http);
        var updated = await command.ExecuteAsync(tenantId, request, ct);
        return Results.Ok(new { success = true, data = SerializeConfig(updated) });
    }

    private static async Task<IResult> ToggleAsync(
        ToggleWidgetRequest request,
        HttpContext http,
        ToggleWidgetCommand command,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId(http);
        var tenantSlug = ResolveTenantSlug(http);
        var result = await command.ExecuteAsync(tenantId, tenantSlug, request.IsEnabled, ct);
        return Results.Ok(new
        {
            success = true,
            data = new
            {
                is_enabled = result.IsEnabled,
                affected_conversations = result.AffectedConversations,
            },
        });
    }

    private static Guid ResolveTenantId(HttpContext http)
    {
        var raw = http.User.FindFirst("tenant_id")?.Value
            ?? throw new InvalidOperationException("tenant_id claim missing.");
        return Guid.Parse(raw);
    }

    private static string ResolveTenantSlug(HttpContext http)
        => http.User.FindFirst("tenant_slug")?.Value
        ?? throw new InvalidOperationException("tenant_slug claim missing.");

    private static string BuildInstallationSnippet(Guid widgetToken, string cdnBaseUrl)
    {
        return $$"""
            <script>
              window.OmniDeskConfig = { token: '{{widgetToken}}' };
            </script>
            <script src="{{cdnBaseUrl}}/loader.js" async></script>
            """;
    }

    private static object SerializeConfig(WidgetConfig c) => new
    {
        is_enabled = c.IsEnabled,
        primary_color = c.PrimaryColor,
        launcher_icon = c.LauncherIcon.ToString().ToLowerInvariant(),
        company_name = c.CompanyName,
        welcome_message = c.WelcomeMessage,
        input_placeholder = c.InputPlaceholder,
        position = c.Position == WidgetPosition.BottomRight ? "bottom_right" : "bottom_left",
        require_identification = c.RequireIdentification,
        identification_fields = c.IdentificationFields,
        allowed_domains = c.AllowedDomains,
        privacy_policy_text = c.PrivacyPolicyText,
        privacy_policy_url = c.PrivacyPolicyUrl,
        abandonment_timeout_hours = c.AbandonmentTimeoutHours,
        inactivity_close_hours = c.InactivityCloseHours,
        updated_at = c.UpdatedAt,
    };

    private static object Error(string code, string message)
        => new { success = false, error = new { code, message } };
}
