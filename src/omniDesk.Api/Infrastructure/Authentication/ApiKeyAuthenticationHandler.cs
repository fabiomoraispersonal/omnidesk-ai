using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Infrastructure.Audit;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Authentication;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions { }

/// <summary>
/// Spec 012 US3 — authenticates requests via the <c>X-Api-Key</c> header.
/// Validates the SHA-256 hash against <c>api_keys</c>, checks revoked/expires_at,
/// then populates a <c>ClaimsPrincipal</c> with tenant and role claims.
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceProvider serviceProvider)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    private const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var rawValues))
            return AuthenticateResult.NoResult();

        var rawKey = rawValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(rawKey))
            return AuthenticateResult.NoResult();

        var keyHash = ApiKeyRepository.HashKey(rawKey);

        using var scope = serviceProvider.CreateScope();
        var db    = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo  = scope.ServiceProvider.GetRequiredService<ApiKeyRepository>();

        var apiKey = await repo.FindByHashAsync(keyHash, Context.RequestAborted);

        if (apiKey is null)
            return AuthenticateResult.Fail("Invalid or revoked API key.");

        if (apiKey.ExpiresAt.HasValue && apiKey.ExpiresAt.Value < DateTime.UtcNow)
            return AuthenticateResult.Fail("API key has expired.");

        var tenantSlug = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == apiKey.TenantId)
            .Select(t => t.Slug)
            .FirstOrDefaultAsync(Context.RequestAborted);

        if (tenantSlug is null)
            return AuthenticateResult.Fail("Tenant not found for API key.");

        var claims = new List<Claim>
        {
            new("tenant_slug", tenantSlug),
            new("tenant_id",   apiKey.TenantId.ToString()),
            new("role",        Roles.TenantAdmin),
            new("api_key_id",  apiKey.Id.ToString()),
        };

        var identity  = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, SchemeName);

        // T028 — best-effort last_used_at update; never block auth on this
        _ = Task.Run(async () =>
        {
            try
            {
                using var bgScope = serviceProvider.CreateScope();
                var bgRepo = bgScope.ServiceProvider.GetRequiredService<ApiKeyRepository>();
                await bgRepo.UpdateLastUsedAtAsync(keyHash, CancellationToken.None);
            }
            catch { /* intentionally swallowed */ }
        });

        return AuthenticateResult.Success(ticket);
    }
}
