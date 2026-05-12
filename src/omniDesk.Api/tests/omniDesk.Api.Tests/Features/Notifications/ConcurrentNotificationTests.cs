using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.Notifications;

/// <summary>
/// Spec 010 T101 — concurrency smoke test: 50 NotifyTicketAssignedAsync calls in parallel
/// across 5 attendants produce 50 rows without races. Each call needs its own DbContext
/// (EF Core DbContext is not thread-safe). Requires Testcontainers.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ConcurrentNotificationTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private string? _connString;
    private AppDbContext? _setupDb;
    private ConnectionMultiplexer? _redis;

    public ConcurrentNotificationTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        _connString = csb.ConnectionString;
        _setupDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connString).Options);
        _redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_setupDb is not null) await _setupDb.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }

    [Fact]
    public async Task FiftyParallelNotifies_ProduceFiftyRows()
    {
        // Seed 5 attendants up-front.
        var attendants = new List<Guid>();
        for (var i = 0; i < 5; i++)
            attendants.Add(await NotificationTestHelpers.SeedAttendantAsync(_setupDb!));

        // Launch 50 notifications in parallel; each task uses its own DbContext.
        var tasks = new List<Task>();
        for (var i = 0; i < 50; i++)
        {
            var att = attendants[i % 5];
            var ticketId = Guid.NewGuid();
            tasks.Add(Task.Run(async () =>
            {
                await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(_connString!).Options);
                var svc = NotificationTestHelpers.BuildService(db, _redis!, TenantSchemaFixture.TenantSlug);
                await svc.NotifyTicketAssignedAsync(att, ticketId, "TK-CONC", default);
            }));
        }

        await Task.WhenAll(tasks);

        var total = await _setupDb!.Notifications.CountAsync();
        Assert.Equal(50, total);
    }
}
