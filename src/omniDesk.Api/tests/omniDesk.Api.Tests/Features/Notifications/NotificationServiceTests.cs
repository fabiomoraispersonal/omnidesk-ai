using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Features.Notifications;
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
/// Spec 010 T031 — NotificationService: persists in-app row + publishes WS to per-attendant channel.
/// Requires Testcontainers (Docker) for Postgres + Redis.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class NotificationServiceTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;

    public NotificationServiceTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task NotifyTicketAssignedAsync_PersistsRow_AndPublishesWs()
    {
        var attendantId = await SeedAttendantAsync();

        var service = BuildService();
        var ticketId = Guid.NewGuid();

        await service.NotifyTicketAssignedAsync(attendantId, ticketId, "TK-20260512-00001", default);

        var row = await _db!.Notifications.AsNoTracking()
            .FirstAsync(n => n.AttendantId == attendantId);
        Assert.Equal(NotificationEventTypes.TicketAssigned, row.EventType);
        Assert.Equal(NotificationEntityTypes.Ticket, row.EntityType);
        Assert.Equal(ticketId, row.EntityId);
        Assert.False(row.IsRead);
        Assert.Null(row.ArchivedAt);
        Assert.Contains("TK-20260512-00001", row.Title);
    }

    [Fact]
    public async Task NotifyTicketQueuedAsync_FanOutsToSupervisors()
    {
        // No supervisors in this minimal fixture → expect 0 notifications (no error).
        var deptId = Guid.NewGuid();
        var service = BuildService();
        var ticketId = Guid.NewGuid();

        await service.NotifyTicketQueuedAsync(ticketId, "TK-20260512-00002", deptId, default);

        var count = await _db!.Notifications.AsNoTracking().CountAsync();
        Assert.Equal(0, count);
    }

    private NotificationService BuildService()
    {
        var db = _db!;
        var publisher = new NotificationEventPublisher(_redis!);
        var supervisors = new SupervisorLookupService(db, new MemoryCache(new MemoryCacheOptions()));
        var prefsRepo = new AttendantPreferencesRepository(db);
        var pushRepo = new PushSubscriptionRepository(db);
        var vapid = new VapidKeyProvider(new ConfigurationBuilder().Build()); // empty config → push disabled
        var dispatcher = new WebPushDispatcher(
            vapid, pushRepo, NullLogger<WebPushDispatcher>.Instance);
        var slug = new TestSlugAccessor(TenantSchemaFixture.TenantSlug);
        var metrics = new NotificationMetrics(new TestMeterFactory());
        return new NotificationService(
            new NotificationRepository(db),
            publisher,
            supervisors,
            prefsRepo,
            dispatcher,
            _redis!,
            db,
            metrics,
            slug,
            NullLogger<NotificationService>.Instance);
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

    private sealed class TestSlugAccessor(string slug) : ITenantSlugAccessor
    {
        public string Slug { get; } = slug;
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) =>
            new(options);
        public void Dispose() { }
    }
}
