using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Presence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Attendants;

[Trait("Category", "Integration")]
public class UpdateStatusTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public UpdateStatusTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SelfStatusChange_PropagatesToAllSinks()
    {
        using var scope = _factory.Services.CreateScope();
        var (token, attendantId, slug) = await SeedAttendantAsync(scope, asTenantAdmin: false);

        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);

        var resp = await client.PatchAsJsonAsync($"/api/attendants/{attendantId}/status", new { status = "online" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.AttendantStatuses.AsNoTracking().FirstAsync(s => s.AttendantId == attendantId);
        Assert.Equal(AttendanceStatus.Online, entry.Status);

        var presence = scope.ServiceProvider.GetRequiredService<PresenceCache>();
        var snap = await presence.GetAsync(slug, attendantId);
        Assert.NotNull(snap);
        Assert.Equal(AttendanceStatus.Online, snap!.Status);
    }

    [Fact]
    public async Task AnotherAttendant_CannotChangeMyStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var (otherToken, attendantId, _) = await SeedAttendantAsync(scope, asTenantAdmin: false);
        var (myToken, _, _) = await SeedAttendantAsync(scope, asTenantAdmin: false);

        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, myToken);
        var resp = await client.PatchAsJsonAsync($"/api/attendants/{attendantId}/status", new { status = "away" });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CanChangeOthersStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var (_, attendantId, _) = await SeedAttendantAsync(scope, asTenantAdmin: false);
        var (adminToken, _, _) = await SeedAttendantAsync(scope, asTenantAdmin: true);

        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, adminToken);
        var resp = await client.PatchAsJsonAsync($"/api/attendants/{attendantId}/status", new { status = "away" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task InvalidStatus_Returns422()
    {
        using var scope = _factory.Services.CreateScope();
        var (token, attendantId, _) = await SeedAttendantAsync(scope, asTenantAdmin: false);
        var client = _factory.CreateClient();
        AuthTestHelpers.SetBearerToken(client, token);
        var resp = await client.PatchAsJsonAsync($"/api/attendants/{attendantId}/status", new { status = "ghost" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    private static async Task<(string token, Guid attendantId, string slug)> SeedAttendantAsync(
        IServiceScope scope, bool asTenantAdmin)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantId = Guid.NewGuid();
        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 12);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Slug = slug, Status = TenantStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });

        var user = await AuthTestHelpers.SeedUserAsync(scope,
            $"a-{Guid.NewGuid():N}@s.test", "Pass!12345",
            asTenantAdmin ? UserRole.TenantAdmin : UserRole.Attendant,
            tenantId: tenantId);

        var attendant = new Attendant
        {
            Id = Guid.NewGuid(), UserId = user.Id, Name = "X", MaxSimultaneousChats = 5,
            IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            Status = new AttendantStatusEntry
            {
                Status = AttendanceStatus.Offline,
                ChangedAt = DateTimeOffset.UtcNow,
                ChangedBy = AttendanceStatusChangedBy.Manual,
            },
        };
        db.Attendants.Add(attendant);
        await db.SaveChangesAsync();

        var jwt = scope.ServiceProvider.GetRequiredService<omniDesk.Api.Infrastructure.Security.JwtService>();
        return (jwt.GenerateAccessToken(user), attendant.Id, slug);
    }
}
