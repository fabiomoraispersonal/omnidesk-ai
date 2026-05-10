using FluentValidation;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.WhatsApp.Config.Commands;
using omniDesk.Api.Features.WhatsApp.Config.Queries;

namespace omniDesk.Api.Features.WhatsApp.Config;

/// <summary>
/// Spec 008 — CRM admin surface mounted at <c>/api/whatsapp/config</c>.
/// JWT-authenticated. RBAC via Policies.CanViewChannelStatus / CanEditChannelConfig
/// / CanToggleChannel (research R10 / contracts/whatsapp-config-api.md §RBAC).
///
/// FR-003 / SC-004: access_token + app_secret NUNCA retornados em texto plano —
/// apenas flags <c>access_token_configured</c> / <c>app_secret_configured</c>.
/// </summary>
public static class WhatsAppConfigEndpoints
{
    public static RouteGroupBuilder MapWhatsAppConfigEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetAsync).RequireAuthorization(Policies.CanViewChannelStatus);
        group.MapPut("/", PutAsync).RequireAuthorization(Policies.CanEditChannelConfig);
        group.MapPatch("/toggle", ToggleAsync).RequireAuthorization(Policies.CanToggleChannel);
        return group;
    }

    private static async Task<IResult> GetAsync(
        HttpContext http,
        GetWhatsAppConfigQuery query,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId(http);
        var slug = ResolveTenantSlug(http);

        var dto = await query.ExecuteAsync(tenantId, slug, ct);
        if (dto is null)
            return Results.NotFound(Error("WHATSAPP_CONFIG_NOT_FOUND",
                "Configuração WhatsApp não provisionada para este tenant."));

        return Results.Ok(new { success = true, data = dto });
    }

    private static async Task<IResult> PutAsync(
        UpdateWhatsAppConfigRequest request,
        HttpContext http,
        UpdateWhatsAppConfigCommand command,
        IValidator<UpdateWhatsAppConfigRequest> validator,
        GetWhatsAppConfigQuery query,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new
            {
                success = false,
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Validação falhou.",
                    details = validation.Errors.Select(e => new
                    {
                        field = ToSnakeCase(e.PropertyName),
                        code = "INVALID",
                        message = e.ErrorMessage,
                    }),
                },
            });
        }

        var tenantId = ResolveTenantId(http);
        var slug = ResolveTenantSlug(http);

        await command.ExecuteAsync(tenantId, slug, request, ct);
        var dto = await query.ExecuteAsync(tenantId, slug, ct);
        return Results.Ok(new { success = true, data = dto });
    }

    private static async Task<IResult> ToggleAsync(
        ToggleWhatsAppChannelRequest request,
        HttpContext http,
        ToggleWhatsAppChannelCommand command,
        GetWhatsAppConfigQuery query,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId(http);
        var slug = ResolveTenantSlug(http);

        var result = await command.ExecuteAsync(tenantId, slug, request.IsEnabled, ct);

        return result.Status switch
        {
            ToggleResultStatus.Ok =>
                Results.Ok(new
                {
                    success = true,
                    data = await query.ExecuteAsync(tenantId, slug, ct),
                }),

            ToggleResultStatus.NotConfigured =>
                Results.UnprocessableEntity(new
                {
                    success = false,
                    error = new
                    {
                        code = "WHATSAPP_NOT_CONFIGURED",
                        message = "Preencha phone_number_id, waba_id, access_token e app_secret antes de ativar.",
                        details = (result.MissingFields ?? Array.Empty<string>())
                            .Select(f => new { field = f, code = "MISSING" }),
                    },
                }),

            ToggleResultStatus.InvalidToken =>
                Results.UnprocessableEntity(new
                {
                    success = false,
                    error = new
                    {
                        code = "INVALID_TOKEN",
                        message = "Access Token rejeitado pela Meta. Atualize as credenciais e tente novamente.",
                    },
                }),

            _ => Results.StatusCode(500),
        };
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

    private static object Error(string code, string message)
        => new { success = false, error = new { code, message } };

    private static string ToSnakeCase(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName)) return propertyName;
        var sb = new System.Text.StringBuilder(propertyName.Length + 4);
        for (var i = 0; i < propertyName.Length; i++)
        {
            var c = propertyName[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
