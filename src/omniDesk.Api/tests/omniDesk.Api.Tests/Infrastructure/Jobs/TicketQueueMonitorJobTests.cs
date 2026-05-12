using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Features.Notifications;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Authorization;
using omniDesk.Api.Infrastructure.Jobs;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Jobs;

/// <summary>
/// Spec 010 T065 — TicketQueueMonitorJob notifies supervisors only after the 5-min
/// threshold + is idempotent via Redis NX. Requires Testcontainers (Postgres + Redis).
/// </summary>
[Collection("Spec006-TenantSchema")]
public class TicketQueueMonitorJobTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;

    public TicketQueueMonitorJobTests(TenantSchemaFixture fx) => _fx = fx;

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

    private TicketQueueMonitorJob BuildJob()
    {
        var notifs = NotificationTestHelpers.BuildService(_db!, _redis!, TenantSchemaFixture.TenantSlug);
        var tenantHolder = new TenantContextHolder();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Notifications:QueueAlertThresholdMinutes"] = "5" })
            .Build();
        return new TicketQueueMonitorJob(
            _db!, _redis!, notifs, tenantHolder, cfg,
            NullLogger<TicketQueueMonitorJob>.Instance);
    }

    [Fact]
    public async Task TicketYoungerThan5Min_NotNotified()
    {
        var dept = await NotificationTestHelpers.SeedDepartmentAsync(_db!);
        await NotificationTestHelpers.SeedAttendantAsync(_db!, UserRole.TenantAdmin); // would-be supervisor
        await SeedQueueTicketAsync(dept.Id, createdAt: DateTimeOffset.UtcNow.AddMinutes(-2));

        var job = BuildJob();
        await job.RunAsync(default);

        var notifs = await _db!.Notifications.CountAsync();
        Assert.Equal(0, notifs);
    }

    [Fact]
    public async Task TicketOlderThan5Min_NotifiesSupervisors_Idempotent()
    {
        var dept = await NotificationTestHelpers.SeedDepartmentAsync(_db!);
        var admin = await NotificationTestHelpers.SeedAttendantAsync(_db!, UserRole.TenantAdmin);
        var ticket = await SeedQueueTicketAsync(dept.Id, createdAt: DateTimeOffset.UtcNow.AddMinutes(-6));

        var job = BuildJob();
        await job.RunAsync(default);

        // 1 admin → 1 notification.
        var notifs1 = await _db!.Notifications.CountAsync();
        Assert.Equal(1, notifs1);

        // Second run is idempotent via Redis NX flag.
        await job.RunAsync(default);
        var notifs2 = await _db.Notifications.CountAsync();
        Assert.Equal(1, notifs2);

        // Verify the NX flag.
        var key = RedisKeys.NotificationQueueAlert(TenantSchemaFixture.TenantSlug, ticket);
        var flag = await _redis!.GetDatabase().StringGetAsync(key);
        Assert.Equal("1", flag.ToString());
    }

    private async Task<Guid> SeedQueueTicketAsync(Guid deptId, DateTimeOffset createdAt)
    {
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Protocol = $"TK-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            DepartmentId = deptId,
            Status = TicketStatus.New,
            Subject = "fila",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
        _db!.Tickets.Add(ticket);
        await _db.SaveChangesAsync();
        return ticket.Id;
    }
}
