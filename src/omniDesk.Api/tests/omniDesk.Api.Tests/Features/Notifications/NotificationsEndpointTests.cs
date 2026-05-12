using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Features.Notifications;
using omniDesk.Api.Features.Notifications.Commands;
using omniDesk.Api.Features.Notifications.Queries;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Metrics;
using omniDesk.Api.Infrastructure.Notifications;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Push;
using omniDesk.Api.Infrastructure.WebSockets;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.Notifications;

/// <summary>
/// Spec 010 T033 — exercises the command/query layer used by NotificationsEndpoints.
/// Tests run service-level rather than HTTP because the integration WebApplicationFactory
/// requires a manually-passed connection string (see TestWebApplicationFactory).
/// Requires Testcontainers for Postgres + Redis.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class NotificationsEndpointTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;

    public NotificationsEndpointTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(csb.ConnectionString).Options);
        _redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }

    [Fact]
    public async Task ListAndMark_PaginatesAndFlipsIsRead()
    {
        var att = await SeedAttendantAsync();

        // Seed 25 notifications; expect 20 on page 1, 5 on page 2.
        for (var i = 0; i < 25; i++)
            await SeedNotificationAsync(att, isRead: i % 2 == 0);

        var repo = new NotificationRepository(_db!);
        var listQuery = new ListNotificationsQuery(repo);

        var (page1, total) = await listQuery.ExecuteAsync(att, 1, 20, unreadOnly: false, default);
        Assert.Equal(25, total);
        Assert.Equal(20, page1.Count);

        var (page2, _) = await listQuery.ExecuteAsync(att, 2, 20, unreadOnly: false, default);
        Assert.Equal(5, page2.Count);

        // unread_only filter — half (every other) is unread.
        var (unread, unreadTotal) = await listQuery.ExecuteAsync(att, 1, 50, unreadOnly: true, default);
        Assert.True(unreadTotal > 0);
        Assert.All(unread, n => Assert.False(n.IsRead));
    }

    [Fact]
    public async Task UnreadCount_IsLive_AndCappedAt99()
    {
        var att = await SeedAttendantAsync();
        for (var i = 0; i < 120; i++) await SeedNotificationAsync(att, isRead: false);

        var query = new UnreadCountQuery(new NotificationRepository(_db!));
        var count = await query.ExecuteAsync(att, default);

        Assert.Equal(99, count); // capped
    }

    [Fact]
    public async Task MarkAsRead_FlipsFlag_AndEmitsUnreadCount()
    {
        var att = await SeedAttendantAsync();
        var notif = await SeedNotificationAsync(att, isRead: false);

        var repo = new NotificationRepository(_db!);
        var publisher = new NotificationEventPublisher(_redis!);
        var slug = new TestSlugAccessor(TenantSchemaFixture.TenantSlug);
        var cmd = new MarkAsReadCommand(repo, publisher, slug);

        var result = await cmd.ExecuteAsync(notif, att, userId: Guid.NewGuid(), default);
        Assert.Equal(MarkAsReadResult.Ok, result);

        var refreshed = await _db!.Notifications.AsNoTracking().FirstAsync(n => n.Id == notif);
        Assert.True(refreshed.IsRead);
    }

    [Fact]
    public async Task MarkAsRead_OtherAttendantsNotification_ReturnsNotFound()
    {
        var att1 = await SeedAttendantAsync();
        var att2 = await SeedAttendantAsync();
        var notif = await SeedNotificationAsync(att1, isRead: false);

        var repo = new NotificationRepository(_db!);
        var publisher = new NotificationEventPublisher(_redis!);
        var slug = new TestSlugAccessor(TenantSchemaFixture.TenantSlug);
        var cmd = new MarkAsReadCommand(repo, publisher, slug);

        // att2 tries to mark att1's notification — must return NotFound (not even acknowledge existence).
        var result = await cmd.ExecuteAsync(notif, att2, userId: Guid.NewGuid(), default);
        Assert.Equal(MarkAsReadResult.NotFound, result);

        // Verify the notification is still unread.
        var refreshed = await _db!.Notifications.AsNoTracking().FirstAsync(n => n.Id == notif);
        Assert.False(refreshed.IsRead);
    }

    [Fact]
    public async Task MarkAllAsRead_ZeroesUnreadCount()
    {
        var att = await SeedAttendantAsync();
        for (var i = 0; i < 10; i++) await SeedNotificationAsync(att, isRead: false);

        var repo = new NotificationRepository(_db!);
        var publisher = new NotificationEventPublisher(_redis!);
        var slug = new TestSlugAccessor(TenantSchemaFixture.TenantSlug);
        var cmd = new MarkAllAsReadCommand(repo, publisher, slug);

        var marked = await cmd.ExecuteAsync(att, userId: Guid.NewGuid(), default);
        Assert.Equal(10, marked);

        var remaining = await new UnreadCountQuery(repo).ExecuteAsync(att, default);
        Assert.Equal(0, remaining);
    }

    private async Task<Guid> SeedAttendantAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"u-{Guid.NewGuid():N}@test.local",
            Name = "Att",
            PasswordHash = "x",
            Role = UserRole.Attendant,
            IsActive = true,
            EmailVerified = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db!.Users.Add(user);
        await _db.SaveChangesAsync();

        var att = new Attendant
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

    private async Task<Guid> SeedNotificationAsync(Guid attendantId, bool isRead)
    {
        var n = new Notification
        {
            Id = Guid.NewGuid(),
            AttendantId = attendantId,
            EventType = NotificationEventTypes.TicketAssigned,
            Title = "t",
            Body = "b",
            EntityType = NotificationEntityTypes.Ticket,
            EntityId = Guid.NewGuid(),
            IsRead = isRead,
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-Random.Shared.Next(0, 1_000_000)),
        };
        _db!.Notifications.Add(n);
        await _db.SaveChangesAsync();
        return n.Id;
    }

    private sealed class TestSlugAccessor(string slug) : ITenantSlugAccessor
    {
        public string Slug { get; } = slug;
    }
}
