using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.AiSettings.Validators;
using omniDesk.Api.Features.Authorization.Authz;
using omniDesk.Api.Infrastructure.Audit;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.OpenAi;
using omniDesk.Api.Infrastructure.Persistence;
using AiSettingsEntity = omniDesk.Api.Domain.AiSettings.AiSettings;

namespace omniDesk.Api.Features.AiSettings;

public static class AiSettingsEndpoints
{
    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        group.MapGet("/", GetAsync).RequireAuthorization(Policies.CanEditAgentAdvancedConfig);
        group.MapPut("/", UpdateAsync).RequireAuthorization(Policies.CanEditAgentAdvancedConfig);
        group.MapPut("/openai-credentials", UpdateCredentialsAsync).RequireAuthorization(Policies.CanEditAgentAdvancedConfig);
        group.MapDelete("/openai-credentials", DeleteCredentialsAsync).RequireAuthorization(Policies.CanEditAgentAdvancedConfig);
        return group;
    }

    private static async Task<IResult> GetAsync(
        AppDbContext db,
        ICurrentUser currentUser,
        IConfiguration config,
        CancellationToken ct)
    {
        if (currentUser.TenantId is null) return Results.Unauthorized();
        var settings = await db.AiSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == currentUser.TenantId, ct);
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == currentUser.TenantId, ct);
        var globalAllow = config.GetSection("Ai:GlobalAllowedModels").Get<string[]>() ?? Array.Empty<string>();

        var keyPreview = string.IsNullOrEmpty(tenant.OpenAiApiKeyEnc) ? null : "sk-•••";
        // Note: actual last-4 preview requires decryption; kept simple here.

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                context_window_messages = settings?.ContextWindowMessages ?? AiSettingsEntity.DefaultContextWindowMessages,
                available_models = settings?.AvailableModels ?? Array.Empty<string>(),
                global_allowlist = globalAllow,
                openai_credentials = new
                {
                    key_set = !string.IsNullOrEmpty(tenant.OpenAiApiKeyEnc),
                    key_preview = keyPreview,
                    organization = tenant.OpenAiOrganization,
                    project = tenant.OpenAiProject,
                },
            },
        });
    }

    private static async Task<IResult> UpdateAsync(
        UpdateAiSettingsRequest req,
        AppDbContext db,
        IValidator<UpdateAiSettingsRequest> validator,
        ICurrentUser currentUser,
        IConfiguration config,
        CancellationToken ct)
    {
        if (currentUser.TenantId is null) return Results.Unauthorized();
        var v = await validator.ValidateAsync(req, ct);
        if (!v.IsValid)
            return Results.BadRequest(new { success = false, error = new { code = "VALIDATION_FAILED", details = v.Errors.Select(e => e.ErrorMessage) } });

        if (req.AvailableModels is { Length: > 0 })
        {
            var globalAllow = config.GetSection("Ai:GlobalAllowedModels").Get<string[]>() ?? Array.Empty<string>();
            var invalid = req.AvailableModels.Where(m => !globalAllow.Contains(m)).ToArray();
            if (invalid.Length > 0)
                return Results.BadRequest(new { success = false, error = new { code = "MODEL_NOT_IN_GLOBAL_ALLOWLIST", invalid } });
        }

        var settings = await db.AiSettings.FirstOrDefaultAsync(s => s.TenantId == currentUser.TenantId, ct);
        if (settings is null)
        {
            settings = new AiSettingsEntity { TenantId = currentUser.TenantId.Value };
            db.AiSettings.Add(settings);
        }
        if (req.ContextWindowMessages.HasValue) settings.ContextWindowMessages = req.ContextWindowMessages.Value;
        if (req.AvailableModels is not null) settings.AvailableModels = req.AvailableModels;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { success = true, data = new { context_window_messages = settings.ContextWindowMessages, available_models = settings.AvailableModels } });
    }

    private static async Task<IResult> UpdateCredentialsAsync(
        UpdateOpenAiCredentialsRequest req,
        AppDbContext db,
        IValidator<UpdateOpenAiCredentialsRequest> validator,
        IDataProtectionProvider dp,
        OpenAiKeyResolver resolver,
        ICurrentUser currentUser,
        IAuditService audit,
        CancellationToken ct)
    {
        if (currentUser.TenantId is null) return Results.Unauthorized();
        var v = await validator.ValidateAsync(req, ct);
        if (!v.IsValid)
            return Results.BadRequest(new { success = false, error = new { code = "VALIDATION_FAILED", details = v.Errors.Select(e => e.ErrorMessage) } });

        // Validate key against OpenAI before persisting.
        var ok = await resolver.ValidateKeyAsync(req.ApiKey, req.Organization, req.Project, ct);
        if (!ok)
            return Results.BadRequest(new { success = false, error = new { code = "OPENAI_KEY_INVALID", message = "OpenAI rejeitou as credenciais." } });

        var protector = dp.CreateProtector(OpenAiKeyResolver.DataProtectionPurpose);
        var encrypted = protector.Protect(req.ApiKey);

        var tenant = await db.Tenants.FirstAsync(t => t.Id == currentUser.TenantId, ct);
        tenant.OpenAiApiKeyEnc = encrypted;
        tenant.OpenAiOrganization = req.Organization;
        tenant.OpenAiProject = req.Project;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        audit.Log(currentUser.TenantSlug, currentUser.TenantId!.Value, AuditEventNames.TenantOpenAiKeyChanged,
            AuditActorFactory.FromCurrentUser(currentUser),
            AuditTargetFactory.Tenant(currentUser.TenantId!.Value, currentUser.TenantSlug));

        var preview = "sk-..." + req.ApiKey[^Math.Min(4, req.ApiKey.Length)..];
        return Results.Ok(new { success = true, data = new { key_set = true, key_preview = preview } });
    }

    private static async Task<IResult> DeleteCredentialsAsync(
        AppDbContext db,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        if (currentUser.TenantId is null) return Results.Unauthorized();
        var tenant = await db.Tenants.FirstAsync(t => t.Id == currentUser.TenantId, ct);
        tenant.OpenAiApiKeyEnc = null;
        tenant.OpenAiOrganization = null;
        tenant.OpenAiProject = null;
        tenant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true, data = new { key_set = false } });
    }
}
