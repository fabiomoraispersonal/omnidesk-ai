using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Features.Notifications.Commands;
using omniDesk.Api.Features.Notifications.Queries;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Notifications;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.WebSockets;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.Notifications;

/// <summary>
/// Spec 010 T102 — security audit: notifications are strictly attendant-scoped. Cross-attendant
/// reads/writes must never succeed (a "404" should be indistinguishable from "not yours").
/// Requires Testcontainers.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class NotificationSecurityTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;

    public NotificationSecurityTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task List_DoesNotReturnOtherAttendantsNotifications()
    {
        var att1 = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var att2 = await NotificationTestHelpers.SeedAttendantAsync(_db!);

        for (var i = 0; i < 3; i++) await SeedAsync(att1);
        for (var i = 0; i < 7; i++) await SeedAsync(att2);

        var query = new ListNotificationsQuery(new NotificationRepository(_db!));
        var (items1, total1) = await query.ExecuteAsync(att1, 1, 50, false, default);
        var (items2, total2) = await query.ExecuteAsync(att2, 1, 50, false, default);

        Assert.Equal(3, total1);
        Assert.Equal(7, total2);
        Assert.All(items1, n => Assert.Equal(att1, n.AttendantId));
        Assert.All(items2, n => Assert.Equal(att2, n.AttendantId));
    }

    [Fact]
    public async Task UnreadCount_IsPerAttendant_NotGlobal()
    {
        var att1 = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var att2 = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        for (var i = 0; i < 5; i++) await SeedAsync(att1, isRead: false);
        for (var i = 0; i < 9; i++) await SeedAsync(att2, isRead: false);

        var query = new UnreadCountQuery(new NotificationRepository(_db!));
        Assert.Equal(5, await query.ExecuteAsync(att1, default));
        Assert.Equal(9, await query.ExecuteAsync(att2, default));
    }

    [Fact]
    public async Task MarkAsRead_CrossAttendant_DoesNotFlipFlag()
    {
        var att1 = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var att2 = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var notif = await SeedAsync(att1, isRead: false);

        var repo = new NotificationRepository(_db!);
        var publisher = new NotificationEventPublisher(_redis!);
        var slug = new NotificationTestHelpers.TestSlugAccessor(TenantSchemaFixture.TenantSlug);
        var cmd = new MarkAsReadCommand(repo, publisher, slug);

        var result = await cmd.ExecuteAsync(notif, att2, userId: Guid.NewGuid(), default);
        Assert.Equal(MarkAsReadResult.NotFound, result);

        var row = await _db!.Notifications.AsNoTracking().FirstAsync(n => n.Id == notif);
        Assert.False(row.IsRead);
    }

    [Fact]
    public async Task MarkAllAsRead_OnlyAffectsCallerAttendant()
    {
        var att1 = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var att2 = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        for (var i = 0; i < 3; i++) await SeedAsync(att1, isRead: false);
        for (var i = 0; i < 3; i++) await SeedAsync(att2, isRead: false);

        var repo = new NotificationRepository(_db!);
        var publisher = new NotificationEventPublisher(_redis!);
        var slug = new NotificationTestHelpers.TestSlugAccessor(TenantSchemaFixture.TenantSlug);
        var cmd = new MarkAllAsReadCommand(repo, publisher, slug);

        var marked = await cmd.ExecuteAsync(att1, userId: Guid.NewGuid(), default);
        Assert.Equal(3, marked);

        // att2's notifications still unread.
        var att2Unread = await _db!.Notifications.CountAsync(n => n.AttendantId == att2 && !n.IsRead);
        Assert.Equal(3, att2Unread);
    }

    private async Task<Guid> SeedAsync(Guid attendantId, bool isRead = false)
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
}
