using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using omniDesk.Api.Domain.Authorization;

namespace omniDesk.Api.Features.Authorization.Impersonation;

public record ImpersonationToken(string Token, DateTimeOffset ExpiresAt, string Jti);

public class ImpersonationTokenIssuer
{
    public const int MaxTtlSeconds = 600;
    public const int DefaultTtlSeconds = 300;
    private readonly TimeSpan _ttl;
    private readonly RsaSecurityKey _signingKey;
    private readonly SigningCredentials _signingCredentials;
    private readonly JwtSecurityTokenHandler _handler = new();

    public ImpersonationTokenIssuer(IConfiguration config)
    {
        _ttl = TimeSpan.FromSeconds(ResolveTtlSeconds(config));

        var pem = Environment.GetEnvironmentVariable("JWT_PRIVATE_KEY_PEM")
            ?? throw new InvalidOperationException("JWT_PRIVATE_KEY_PEM not set");
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        _signingKey = new RsaSecurityKey(rsa);
        _signingCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);
    }

    public ImpersonationToken Issue(string targetTenantSlug, Guid? targetTenantId, string operatorSubject = "saas_admin")
    {
        if (string.IsNullOrWhiteSpace(targetTenantSlug))
            throw new ArgumentException("Tenant slug is required.", nameof(targetTenantSlug));

        var jti = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var exp = now.Add(_ttl);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, operatorSubject),
            new("role", Roles.SaasAdmin),
            new("tenant_slug", targetTenantSlug),
            new("impersonating", "true"),
            new("impersonated_by", operatorSubject),
            new(JwtRegisteredClaimNames.Jti, jti),
        };
        if (targetTenantId is { } id)
            claims.Add(new Claim("tenant_id", id.ToString()));

        var token = new JwtSecurityToken(
            issuer: "omnidesk-saas",
            audience: "omnidesk-crm",
            claims: claims,
            notBefore: now,
            expires: exp,
            signingCredentials: _signingCredentials);

        var serialized = _handler.WriteToken(token);
        return new ImpersonationToken(serialized, new DateTimeOffset(exp, TimeSpan.Zero), jti);
    }

    public TimeSpan Ttl => _ttl;

    public static void ValidateStartupConfig(IConfiguration config)
    {
        var ttl = ResolveTtlSeconds(config);
        if (ttl <= 0)
            throw new InvalidOperationException(
                "IMPERSONATION_JWT_TTL_SECONDS deve ser maior que zero.");
        if (ttl > MaxTtlSeconds)
            throw new InvalidOperationException(
                $"IMPERSONATION_JWT_TTL_SECONDS={ttl} excede o máximo permitido ({MaxTtlSeconds}s = 10 min). Ajuste a configuração.");
    }

    private static int ResolveTtlSeconds(IConfiguration config)
    {
        var raw = Environment.GetEnvironmentVariable("IMPERSONATION_JWT_TTL_SECONDS")
            ?? config["Authorization:ImpersonationJwtTtlSeconds"];
        if (int.TryParse(raw, out var parsed) && parsed > 0)
            return parsed;
        return DefaultTtlSeconds;
    }
}
