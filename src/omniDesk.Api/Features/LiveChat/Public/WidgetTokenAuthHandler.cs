using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.LiveChat.Public;

/// <summary>
/// Spec 007 — auth scheme `WidgetToken`. Authenticates public widget requests using the
/// per-tenant `widget_token` UUID.
///
/// Source of token (in order):
///   1. <c>X-Widget-Token</c> header
///   2. <c>?token=&lt;uuid&gt;</c> query string (used by WebSocket handshake when headers are awkward)
///
/// On success, exposes a <see cref="ClaimsPrincipal"/> with two claims that downstream
/// endpoints can rely on:
///   - <c>tenant_slug</c>
///   - <c>tenant_id</c>
///
/// Failure shape mirrors `INVALID_WIDGET_TOKEN` from contracts/widget-public-api.md.
/// </summary>
public class WidgetTokenAuthenticationOptions : AuthenticationSchemeOptions { }

public class WidgetTokenAuthHandler : AuthenticationHandler<WidgetTokenAuthenticationOptions>
{
    public const string SchemeName = "WidgetToken";
    public const string TenantSlugClaim = "tenant_slug";
    public const string TenantIdClaim = "tenant_id";

    private readonly AppDbContext _db;

    public WidgetTokenAuthHandler(
        IOptionsMonitor<WidgetTokenAuthenticationOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        AppDbContext db)
        : base(options, loggerFactory, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var raw = ReadToken(Context.Request);
        if (string.IsNullOrWhiteSpace(raw))
            return AuthenticateResult.NoResult();

        if (!Guid.TryParse(raw, out var token))
            return AuthenticateResult.Fail("INVALID_WIDGET_TOKEN");

        var tenant = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.WidgetToken == token)
            .Select(t => new { t.Id, t.Slug, t.Status })
            .FirstOrDefaultAsync();

        if (tenant is null) return AuthenticateResult.Fail("INVALID_WIDGET_TOKEN");
        if (tenant.Status != TenantStatus.Active) return AuthenticateResult.Fail("INVALID_WIDGET_TOKEN");

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(TenantSlugClaim, tenant.Slug),
                new Claim(TenantIdClaim, tenant.Id.ToString()),
            },
            authenticationType: SchemeName,
            nameType: TenantSlugClaim,
            roleType: ClaimTypes.Role);

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.ContentType = "application/json";
        return Response.WriteAsJsonAsync(new
        {
            success = false,
            error = new { code = "INVALID_WIDGET_TOKEN", message = "Widget token missing or invalid." },
        });
    }

    private static string? ReadToken(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Widget-Token", out var header) && !string.IsNullOrWhiteSpace(header))
            return header.ToString().Trim();

        if (request.Query.TryGetValue("token", out var query) && !string.IsNullOrWhiteSpace(query))
            return query.ToString().Trim();

        return null;
    }
}
