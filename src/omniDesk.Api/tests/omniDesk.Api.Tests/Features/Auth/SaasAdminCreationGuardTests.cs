using System.Net;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Tests.Helpers;
using Xunit;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace omniDesk.Api.Tests.Features.Auth;

[Trait("Category", "Integration")]
public class SaasAdminCreationGuardTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SaasAdminCreationGuardTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Invite_WithSaasAdminRole_ViaCrm_Returns422()
    {
        using var scope = _factory.Services.CreateScope();
        var tenantAdmin = await AuthTestHelpers.SeedUserAsync(
            scope, "ta@guard.test", "Pass!12345", UserRole.TenantAdmin, tenantId: Guid.NewGuid());
        var jwt = scope.ServiceProvider
            .GetRequiredService<omniDesk.Api.Infrastructure.Security.JwtService>();
        var token = jwt.GenerateAccessToken(tenantAdmin);

        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var response = await client.PostAsJsonAsync("/api/auth/invite", new
        {
            email = "newuser@guard.test",
            role = "SaasAdmin",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_ROLE_SAAS_ADMIN", body);
    }
}
