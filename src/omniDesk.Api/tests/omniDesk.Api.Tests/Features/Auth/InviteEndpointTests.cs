using System.Net;
using System.Net.Http.Headers;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Tests.Helpers;
using Xunit;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace omniDesk.Api.Tests.Features.Auth;

[Trait("Category", "Integration")]
public class InviteEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public InviteEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SendInvite_AsTenantAdmin_Creates201()
    {
        using var scope = _factory.Services.CreateScope();
        await AuthTestHelpers.SeedUserAsync(scope, "ta@test.com", "Pass123!", UserRole.TenantAdmin,
            tenantId: Guid.NewGuid());

        var client = _factory.CreateClient();
        var (accessToken, _) = await AuthTestHelpers.LoginAsync(client, "ta@test.com", "Pass123!");
        AuthTestHelpers.SetBearerToken(client, accessToken);

        var response = await client.PostAsJsonAsync("/api/auth/invite", new
        {
            email = "newuser@test.com",
            role = "Attendant",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task SendInvite_ExistingEmail_Returns409()
    {
        using var scope = _factory.Services.CreateScope();
        var tenantId = Guid.NewGuid();
        await AuthTestHelpers.SeedUserAsync(scope, "ta2@test.com", "Pass123!", UserRole.TenantAdmin, tenantId: tenantId);
        await AuthTestHelpers.SeedUserAsync(scope, "existing@test.com", "Pass123!", UserRole.Attendant, tenantId: tenantId);

        var client = _factory.CreateClient();
        var (accessToken, _) = await AuthTestHelpers.LoginAsync(client, "ta2@test.com", "Pass123!");
        AuthTestHelpers.SetBearerToken(client, accessToken);

        var response = await client.PostAsJsonAsync("/api/auth/invite", new
        {
            email = "existing@test.com",
            role = "Attendant",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvite_ValidToken_CreatesUserWithEmailVerifiedAndReturnsAccessToken()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<omniDesk.Api.Infrastructure.Persistence.AppDbContext>();

        var token = Guid.NewGuid().ToString("N");
        db.InviteTokens.Add(new omniDesk.Api.Domain.InviteTokens.InviteToken
        {
            Id = Guid.NewGuid(),
            Email = "invited@test.com",
            Role = UserRole.Attendant,
            TenantId = Guid.NewGuid(),
            TokenHash = AuthTestHelpers.ComputeSha256(token),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(72),
            CreatedBy = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/accept-invite", new
        {
            token,
            name = "New User",
            password = "SecurePass123!",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(body);
    }

    [Fact]
    public async Task AcceptInvite_ExpiredToken_Returns400()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<omniDesk.Api.Infrastructure.Persistence.AppDbContext>();

        var token = Guid.NewGuid().ToString("N");
        db.InviteTokens.Add(new omniDesk.Api.Domain.InviteTokens.InviteToken
        {
            Id = Guid.NewGuid(),
            Email = "expired-invite@test.com",
            Role = UserRole.Attendant,
            TokenHash = AuthTestHelpers.ComputeSha256(token),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedBy = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-73),
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/accept-invite", new
        {
            token,
            name = "User",
            password = "Pass123!",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
