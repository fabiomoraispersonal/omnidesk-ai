using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Features.Authorization.UserLifecycle;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Authorization;

[Trait("Category", "Integration")]
[Collection("Spec004-Authorization")]
public class LastTenantAdminGuardTests
{
    private readonly AuthorizationFixture _fx;

    public LastTenantAdminGuardTests(AuthorizationFixture fx) => _fx = fx;

    private AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_fx.PostgresConnectionString).Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task LoneTenantAdmin_CannotBeDeactivated()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb();
        var admin = new User
        {
            Id = Guid.NewGuid(),
            Email = $"only-{Guid.NewGuid():N}@t.com",
            PasswordHash = "x",
            Role = UserRole.TenantAdmin,
            TenantId = tenantId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var guard = new LastTenantAdminGuard(db);
        await Assert.ThrowsAsync<LastTenantAdminException>(() => guard.EnsureNotLastAsync(admin));
    }

    [Fact]
    public async Task SecondTenantAdminExists_DeactivationProceeds()
    {
        var tenantId = Guid.NewGuid();
        await using var db = NewDb();
        var a1 = new User
        {
            Id = Guid.NewGuid(), Email = $"a1-{Guid.NewGuid():N}@t.com", PasswordHash = "x",
            Role = UserRole.TenantAdmin, TenantId = tenantId, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var a2 = new User
        {
            Id = Guid.NewGuid(), Email = $"a2-{Guid.NewGuid():N}@t.com", PasswordHash = "x",
            Role = UserRole.TenantAdmin, TenantId = tenantId, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.AddRange(a1, a2);
        await db.SaveChangesAsync();

        var guard = new LastTenantAdminGuard(db);
        await guard.EnsureNotLastAsync(a1);
    }

    [Fact]
    public async Task NonAdminUser_GuardSkips()
    {
        await using var db = NewDb();
        var attendant = new User
        {
            Id = Guid.NewGuid(), Email = $"att-{Guid.NewGuid():N}@t.com", PasswordHash = "x",
            Role = UserRole.Attendant, TenantId = Guid.NewGuid(), IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var guard = new LastTenantAdminGuard(db);
        await guard.EnsureNotLastAsync(attendant);
    }
}
