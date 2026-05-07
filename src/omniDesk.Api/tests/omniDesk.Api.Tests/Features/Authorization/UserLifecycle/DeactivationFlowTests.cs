using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Features.Authorization.UserLifecycle;
using omniDesk.Api.Infrastructure.Authorization;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.Authorization;

[Trait("Category", "Integration")]
public class DeactivationFlowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public DeactivationFlowTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Deactivate_RevokesNextRequestWithinOneSecond()
    {
        using var scope = _factory.Services.CreateScope();
        var tenantId = Guid.NewGuid();
        var tenantSlug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Slug = tenantSlug,
            Status = TenantStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var admin = await AuthTestHelpers.SeedUserAsync(scope,
            $"admin-{Guid.NewGuid():N}@d.test", "Pass!12345",
            UserRole.TenantAdmin, tenantId: tenantId);
        var victim = await AuthTestHelpers.SeedUserAsync(scope,
            $"victim-{Guid.NewGuid():N}@d.test", "Pass!12345",
            UserRole.Attendant, tenantId: tenantId);

        var jwt = scope.ServiceProvider.GetRequiredService<JwtService>();
        var adminToken = jwt.GenerateAccessToken(admin);

        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, adminToken);

        var stopwatch = Stopwatch.StartNew();
        var resp = await client.PostAsync($"/api/users/{victim.Id}/deactivate", null);
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());

        // Confirm victim row is_active = false
        var refreshed = await db.Users.AsNoTracking().FirstAsync(u => u.Id == victim.Id);
        Assert.False(refreshed.IsActive);
        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"Deactivation took {stopwatch.ElapsedMilliseconds}ms (must be < 1000 for SC-005)");

        // Refresh tokens for victim must be revoked.
        var anyLive = await db.RefreshTokens
            .AnyAsync(t => t.UserId == victim.Id && !t.Revoked);
        Assert.False(anyLive);
    }

    [Fact]
    public async Task Deactivate_LastTenantAdmin_Returns422WithPtBrMessage()
    {
        using var scope = _factory.Services.CreateScope();
        var tenantId = Guid.NewGuid();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Slug = $"only-{Guid.NewGuid():N}".Substring(0, 12),
            Status = TenantStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var sole = await AuthTestHelpers.SeedUserAsync(scope,
            $"sole-{Guid.NewGuid():N}@d.test", "Pass!12345",
            UserRole.TenantAdmin, tenantId: tenantId);

        var jwt = scope.ServiceProvider.GetRequiredService<JwtService>();
        var token = jwt.GenerateAccessToken(sole);

        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.PostAsync($"/api/users/{sole.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("último Administrador", body);
        Assert.Contains("LAST_TENANT_ADMIN", body);
    }
}
