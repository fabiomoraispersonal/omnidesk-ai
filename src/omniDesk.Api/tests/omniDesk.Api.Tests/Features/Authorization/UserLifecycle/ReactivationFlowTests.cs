using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Features.Authorization.UserLifecycle;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Authorization;

[Trait("Category", "Integration")]
public class ReactivationFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ReactivationFlowTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Reactivate_RestoresIsActive_DoesNotIssueRefreshTokens()
    {
        using var scope = _factory.Services.CreateScope();
        var tenantId = Guid.NewGuid();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Slug = $"react-{Guid.NewGuid():N}".Substring(0, 12),
            Status = TenantStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var admin = await AuthTestHelpers.SeedUserAsync(scope,
            $"admin-{Guid.NewGuid():N}@r.test", "Pass!12345",
            UserRole.TenantAdmin, tenantId: tenantId);
        var victim = await AuthTestHelpers.SeedUserAsync(scope,
            $"victim-{Guid.NewGuid():N}@r.test", "Pass!12345",
            UserRole.Attendant, tenantId: tenantId, isActive: false);

        var jwt = scope.ServiceProvider.GetRequiredService<JwtService>();
        var token = jwt.GenerateAccessToken(admin);
        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.PostAsync($"/api/users/{victim.Id}/reactivate", null);
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());

        var refreshed = await db.Users.AsNoTracking().FirstAsync(u => u.Id == victim.Id);
        Assert.True(refreshed.IsActive);

        var liveRefresh = await db.RefreshTokens
            .AnyAsync(t => t.UserId == victim.Id && !t.Revoked);
        Assert.False(liveRefresh);
    }
}
