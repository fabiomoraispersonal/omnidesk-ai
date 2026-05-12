using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Infrastructure.Jobs;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Jobs;

/// <summary>
/// Spec 010 Polish T099 — NotificationArchiverJob archives rows older than retention,
/// preserves recent rows, and is idempotent across runs. Requires Testcontainers.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class NotificationArchiverJobTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public NotificationArchiverJobTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString).Options);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
    }

    [Fact]
    public async Task ArchivesOldRows_PreservesRecent_AndIsIdempotent()
    {
        var attendantId = await SeedAttendantAsync();
        var now = DateTimeOffset.UtcNow;

        // 3 ancient + 2 recent; archiver runs with default 90d retention.
        await InsertNotificationAsync(attendantId, now.AddDays(-120), archivedAt: null);
        await InsertNotificationAsync(attendantId, now.AddDays(-100), archivedAt: null);
        await InsertNotificationAsync(attendantId, now.AddDays(-91),  archivedAt: null);
        await InsertNotificationAsync(attendantId, now.AddDays(-10),  archivedAt: null);
        await InsertNotificationAsync(attendantId, now.AddDays(-1),   archivedAt: null);

        var job = BuildJob();

        await job.RunAsync(default);

        // 3 archived, 2 active.
        var archived = await _db!.Notifications.CountAsync(n => n.ArchivedAt != null);
        var active   = await _db.Notifications.CountAsync(n => n.ArchivedAt == null);
        Assert.Equal(3, archived);
        Assert.Equal(2, active);

        // Second run is idempotent — same counts.
        await job.RunAsync(default);
        var archivedSecond = await _db.Notifications.CountAsync(n => n.ArchivedAt != null);
        Assert.Equal(3, archivedSecond);
    }

    private NotificationArchiverJob BuildJob()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:ArchiveRetentionDays"] = "90",
            })
            .Build();
        return new NotificationArchiverJob(_db!, config, NullLogger<NotificationArchiverJob>.Instance);
    }

    private async Task<Guid> SeedAttendantAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var user = new omniDesk.Api.Domain.Users.User
        {
            Id = Guid.NewGuid(),
            Email = $"att-{Guid.NewGuid():N}@test.local",
            Name = "Att",
            PasswordHash = "x",
            Role = omniDesk.Api.Domain.Users.UserRole.Attendant,
            IsActive = true,
            EmailVerified = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db!.Users.Add(user);
        await _db.SaveChangesAsync();

        var att = new omniDesk.Api.Domain.Attendants.Attendant
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Name = user.Name,
            MaxSimultaneousChats = 5,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Attendants.Add(att);
        await _db.SaveChangesAsync();
        return att.Id;
    }

    private async Task InsertNotificationAsync(
        Guid attendantId, DateTimeOffset createdAt, DateTimeOffset? archivedAt)
    {
        _db!.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            AttendantId = attendantId,
            EventType = NotificationEventTypes.TicketAssigned,
            Title = "old",
            Body  = "old",
            EntityType = NotificationEntityTypes.Ticket,
            EntityId = Guid.NewGuid(),
            IsRead = false,
            CreatedAt = createdAt,
            ArchivedAt = archivedAt,
        });
        await _db.SaveChangesAsync();
    }
}
