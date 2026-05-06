using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using omniDesk.Api.Domain.Users;

namespace omniDesk.Api.Infrastructure.Security;

public class JwtService
{
    private readonly RsaSecurityKey _privateKey;
    private readonly RsaSecurityKey _publicKey;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtService()
    {
        var privateRsa = RSA.Create();
        privateRsa.ImportFromPem(Environment.GetEnvironmentVariable("JWT_PRIVATE_KEY_PEM")
            ?? throw new InvalidOperationException("JWT_PRIVATE_KEY_PEM not set"));

        var publicRsa = RSA.Create();
        publicRsa.ImportFromPem(Environment.GetEnvironmentVariable("JWT_PUBLIC_KEY_PEM")
            ?? throw new InvalidOperationException("JWT_PUBLIC_KEY_PEM not set"));

        _privateKey = new RsaSecurityKey(privateRsa);
        _publicKey = new RsaSecurityKey(publicRsa);
        _signingCredentials = new SigningCredentials(_privateKey, SecurityAlgorithms.RsaSha256);

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _publicKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
    }

    public string GenerateAccessToken(User user, TimeSpan? duration = null)
    {
        var expiry = duration ?? TimeSpan.FromMinutes(15);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("role", user.Role.ToString()),
            new("tenant_id", user.TenantId?.ToString() ?? string.Empty),
            new("email", user.Email),
        };

        return BuildToken(claims, expiry);
    }

    public string GenerateAccessTokenWithSlug(User user, string tenantSlug, TimeSpan? duration = null)
    {
        var expiry = duration ?? TimeSpan.FromMinutes(15);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("role", user.Role.ToString()),
            new("tenant_id", user.TenantId?.ToString() ?? string.Empty),
            new("tenant_slug", tenantSlug),
            new("email", user.Email),
        };

        return BuildToken(claims, expiry);
    }

    public string GenerateTotpSessionToken(Guid userId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("type", "totp_session")
        };
        return BuildToken(claims, TimeSpan.FromMinutes(5));
    }

    public Guid? ValidateTotpSessionToken(string token)
    {
        try
        {
            var principal = _handler.ValidateToken(token, _validationParameters, out _);
            var typeClaim = principal.FindFirst("type")?.Value;
            if (typeClaim != "totp_session") return null;
            var sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return Guid.TryParse(sub, out var id) ? id : null;
        }
        catch
        {
            return null;
        }
    }

    public string GenerateImpersonationToken(Guid tenantId, string tenantSlug, Guid impersonatedBy)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, impersonatedBy.ToString()),
            new("role", UserRole.TenantAdmin.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("tenant_slug", tenantSlug),
            new("impersonation", "true"),
            new("impersonated_by", impersonatedBy.ToString())
        };
        return BuildToken(claims, TimeSpan.FromMinutes(5));
    }

    public TokenValidationParameters GetValidationParameters() => _validationParameters;

    private string BuildToken(IEnumerable<Claim> claims, TimeSpan duration)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: now,
            expires: now.Add(duration),
            signingCredentials: _signingCredentials);

        return _handler.WriteToken(token);
    }
}
