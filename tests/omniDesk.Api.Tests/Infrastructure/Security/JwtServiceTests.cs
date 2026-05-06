using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Security;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Security;

public class JwtServiceTests : IDisposable
{
    private readonly JwtService _jwtService;
    private const string PrivateKeyEnvVar = "JWT_PRIVATE_KEY_PEM";
    private const string PublicKeyEnvVar = "JWT_PUBLIC_KEY_PEM";

    public JwtServiceTests()
    {
        using var rsa = RSA.Create(2048);
        Environment.SetEnvironmentVariable(PrivateKeyEnvVar, rsa.ExportRSAPrivateKeyPem());
        Environment.SetEnvironmentVariable(PublicKeyEnvVar, rsa.ExportRSAPublicKeyPem());
        _jwtService = new JwtService();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PrivateKeyEnvVar, null);
        Environment.SetEnvironmentVariable(PublicKeyEnvVar, null);
    }

    [Fact]
    public void GenerateAccessToken_ContainsCorrectClaims()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            Role = UserRole.Attendant,
            TenantId = Guid.NewGuid(),
            Name = "Test User",
        };

        var token = _jwtService.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal("Attendant", jwt.Claims.First(c => c.Type == "role").Value);
        Assert.Equal(user.TenantId.ToString(), jwt.Claims.First(c => c.Type == "tenant_id").Value);
        Assert.Equal(user.Email, jwt.Claims.First(c => c.Type == "email").Value);
    }

    [Fact]
    public void GenerateAccessToken_Expires_In15Minutes()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "t@t.com", Role = UserRole.Attendant, Name = "T" };
        var token = _jwtService.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var expectedExpiry = DateTime.UtcNow.AddMinutes(15);
        Assert.InRange(jwt.ValidTo, expectedExpiry.AddSeconds(-10), expectedExpiry.AddSeconds(10));
    }

    [Fact]
    public void GenerateTotpSessionToken_HasCorrectTypeClaim()
    {
        var userId = Guid.NewGuid();
        var token = _jwtService.GenerateTotpSessionToken(userId);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal("totp_session", jwt.Claims.First(c => c.Type == "type").Value);
        Assert.Equal(userId.ToString(), jwt.Subject);
    }

    [Fact]
    public void GenerateImpersonationToken_HasImpersonationClaims()
    {
        var tenantId = Guid.NewGuid();
        var impersonatedBy = Guid.NewGuid();
        var token = _jwtService.GenerateImpersonationToken(tenantId, "test-slug", impersonatedBy);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal("true", jwt.Claims.First(c => c.Type == "impersonation").Value);
        Assert.Equal(impersonatedBy.ToString(), jwt.Claims.First(c => c.Type == "impersonated_by").Value);
        Assert.Equal("TenantAdmin", jwt.Claims.First(c => c.Type == "role").Value);
    }
}
