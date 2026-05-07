using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Authorization.Impersonation;
using Xunit;

namespace omniDesk.Api.Tests.Features.Authorization;

public class ImpersonationTokenTests : IDisposable
{
    public ImpersonationTokenTests()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JWT_PRIVATE_KEY_PEM")))
        {
            using var rsa = System.Security.Cryptography.RSA.Create(2048);
            Environment.SetEnvironmentVariable("JWT_PRIVATE_KEY_PEM", rsa.ExportRSAPrivateKeyPem());
            Environment.SetEnvironmentVariable("JWT_PUBLIC_KEY_PEM", rsa.ExportRSAPublicKeyPem());
        }
    }

    public void Dispose() { }

    private static IConfiguration Config(int? ttl = null)
    {
        var dict = new Dictionary<string, string?>();
        if (ttl is not null) dict["Authorization:ImpersonationJwtTtlSeconds"] = ttl.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void Issue_ProducesExpectedClaims()
    {
        Environment.SetEnvironmentVariable("IMPERSONATION_JWT_TTL_SECONDS", null);
        var issuer = new ImpersonationTokenIssuer(Config());
        var tenantId = Guid.NewGuid();
        var token = issuer.Issue("clinica-x", tenantId);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token.Token);

        Assert.Equal("omnidesk-saas", jwt.Issuer);
        Assert.Contains(jwt.Audiences, a => a == "omnidesk-crm");
        Assert.Equal("saas_admin", jwt.Claims.First(c => c.Type == "sub").Value);
        Assert.Equal(Roles.SaasAdmin, jwt.Claims.First(c => c.Type == "role").Value);
        Assert.Equal("clinica-x", jwt.Claims.First(c => c.Type == "tenant_slug").Value);
        Assert.Equal("true", jwt.Claims.First(c => c.Type == "impersonating").Value);
        Assert.Equal("saas_admin", jwt.Claims.First(c => c.Type == "impersonated_by").Value);
        Assert.NotEqual(Guid.Empty.ToString(), token.Jti);
    }

    [Fact]
    public void Issue_RespectsTtlConfig()
    {
        Environment.SetEnvironmentVariable("IMPERSONATION_JWT_TTL_SECONDS", "120");
        var issuer = new ImpersonationTokenIssuer(Config());
        Assert.Equal(TimeSpan.FromSeconds(120), issuer.Ttl);
        Environment.SetEnvironmentVariable("IMPERSONATION_JWT_TTL_SECONDS", null);
    }

    [Fact]
    public void Issue_GeneratesUniqueJti()
    {
        var issuer = new ImpersonationTokenIssuer(Config());
        var a = issuer.Issue("t", Guid.NewGuid());
        var b = issuer.Issue("t", Guid.NewGuid());
        Assert.NotEqual(a.Jti, b.Jti);
    }

    [Fact]
    public void ValidateStartupConfig_RejectsTtlAboveMax()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ImpersonationTokenIssuer.ValidateStartupConfig(Config(700)));
        Assert.Contains("excede o máximo", ex.Message);
    }

    [Fact]
    public void ValidateStartupConfig_AcceptsAtMax()
    {
        ImpersonationTokenIssuer.ValidateStartupConfig(Config(600));
    }

    [Fact]
    public void Issue_ThrowsOnEmptySlug()
    {
        var issuer = new ImpersonationTokenIssuer(Config());
        Assert.Throws<ArgumentException>(() => issuer.Issue("", Guid.NewGuid()));
    }
}
