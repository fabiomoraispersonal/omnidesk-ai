using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Features.Attendants;
using omniDesk.Api.Features.Distribution;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Presence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Distribution;

[Trait("Category", "Integration")]
public class PresenceTimeoutJobTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public PresenceTimeoutJobTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task OnlineWithoutHeartbeat_For15Min_TransitionsToAway()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (slug, attendantId, userId) = await SeedAttendantAsync(scope, AttendanceStatus.Online,
            heartbeatAt: DateTimeOffset.UtcNow.AddMinutes(-20),
            changedAt: DateTimeOffset.UtcNow.AddMinutes(-20));

        var statusService = scope.ServiceProvider.GetRequiredService<UpdateAttendantStatusService>();
        var job = new PresenceTimeoutJob(db, statusService, NullLogger<PresenceTimeoutJob>.Instance);
        await job.RunAsync();

        var entry = await db.AttendantStatuses.AsNoTracking().FirstAsync(s => s.AttendantId == attendantId);
        Assert.Equal(AttendanceStatus.Away, entry.Status);
        Assert.Equal(AttendanceStatusChangedBy.System, entry.ChangedBy);
    }

    [Fact]
    public async Task AwayFor30Min_TransitionsToOffline()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (slug, attendantId, userId) = await SeedAttendantAsync(scope, AttendanceStatus.Away,
            heartbeatAt: DateTimeOffset.UtcNow.AddMinutes(-50),
            changedAt: DateTimeOffset.UtcNow.AddMinutes(-50));

        var statusService = scope.ServiceProvider.GetRequiredService<UpdateAttendantStatusService>();
        var job = new PresenceTimeoutJob(db, statusService, NullLogger<PresenceTimeoutJob>.Instance);
        await job.RunAsync();

        var entry = await db.AttendantStatuses.AsNoTracking().FirstAsync(s => s.AttendantId == attendantId);
        Assert.Equal(AttendanceStatus.Offline, entry.Status);
        Assert.Equal(AttendanceStatusChangedBy.System, entry.ChangedBy);
    }

    [Fact]
    public async Task RecentHeartbeat_DoesNotTransition()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (slug, attendantId, userId) = await SeedAttendantAsync(scope, AttendanceStatus.Online,
            heartbeatAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            changedAt: DateTimeOffset.UtcNow.AddMinutes(-2));

        var statusService = scope.ServiceProvider.GetRequiredService<UpdateAttendantStatusService>();
        var job = new PresenceTimeoutJob(db, statusService, NullLogger<PresenceTimeoutJob>.Instance);
        await job.RunAsync();

        var entry = await db.AttendantStatuses.AsNoTracking().FirstAsync(s => s.AttendantId == attendantId);
        Assert.Equal(AttendanceStatus.Online, entry.Status);
    }

    private static async Task<(string slug, Guid attendantId, Guid userId)> SeedAttendantAsync(
        IServiceScope scope, AttendanceStatus initial, DateTimeOffset heartbeatAt, DateTimeOffset changedAt)
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
            $"a-{Guid.NewGuid():N}@p.test", "Pass!12345", UserRole.Attendant, tenantId: tenantId);
        var attendant = new Attendant
        {
            Id = Guid.NewGuid(), UserId = user.Id, Name = "Maria",
            MaxSimultaneousChats = 5, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            Status = new AttendantStatusEntry
            {
                Status = initial, ChangedAt = changedAt, ChangedBy = AttendanceStatusChangedBy.Manual,
                LastHeartbeatAt = heartbeatAt,
            },
        };
        db.Attendants.Add(attendant);
        await db.SaveChangesAsync();
        return (slug, attendant.Id, user.Id);
    }
}
