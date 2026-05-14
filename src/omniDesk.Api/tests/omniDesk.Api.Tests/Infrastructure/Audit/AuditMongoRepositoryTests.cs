using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Infrastructure.Audit;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Audit;

/// <summary>
/// Spec 012 T038 — AuditMongoRepository: insert + query filters + tenant isolation.
/// Requires Testcontainers (Docker) for MongoDB.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AuditMongoRepositoryTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AuditMongoRepository _repo = null!;

    public AuditMongoRepositoryTests(TenantSchemaFixture fx) => _fx = fx;

    public Task InitializeAsync()
    {
        _repo = new AuditMongoRepository(_fx.MongoClient);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── helpers ───────────────────────────────────────────────────────────────

    private static AuditLog MakeLog(
        string tenantSlug,
        string @event = "auth.login_success",
        Guid? userId = null,
        DateTime? timestamp = null) => new()
    {
        TenantSlug = tenantSlug,
        TenantId   = Guid.NewGuid(),
        Event      = @event,
        Actor      = new AuditActor { UserId = userId, Role = "tenant_admin" },
        Timestamp  = timestamp?.ToUniversalTime() ?? DateTime.UtcNow,
    };

    // ── insert ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertAsync_PersistsDocument()
    {
        var log = MakeLog("tenant-insert-test");

        await _repo.InsertAsync(log, CancellationToken.None);

        var (items, total) = await _repo.QueryAsync(
            "tenant-insert-test", null, null, null, null, 1, 10, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Single(items, i => i.Event == "auth.login_success");
    }

    // ── event filter ──────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_EventFilter_ReturnsOnlyMatchingEvent()
    {
        var slug = "tenant-event-filter";
        await _repo.InsertAsync(MakeLog(slug, "auth.login_success"), CancellationToken.None);
        await _repo.InsertAsync(MakeLog(slug, "ticket.created"), CancellationToken.None);

        var (items, total) = await _repo.QueryAsync(
            slug, "ticket.created", null, null, null, 1, 10, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.All(items, i => Assert.Equal("ticket.created", i.Event));
    }

    // ── actor filter ──────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_ActorFilter_ReturnsOnlyMatchingUser()
    {
        var slug = "tenant-actor-filter";
        var userId = Guid.NewGuid();
        await _repo.InsertAsync(MakeLog(slug, userId: userId), CancellationToken.None);
        await _repo.InsertAsync(MakeLog(slug, userId: Guid.NewGuid()), CancellationToken.None);

        var (items, total) = await _repo.QueryAsync(
            slug, null, userId, null, null, 1, 10, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.All(items, i => Assert.Equal(userId, i.Actor.UserId));
    }

    // ── date range filter ─────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_DateRange_ReturnsOnlyLogsInRange()
    {
        var slug = "tenant-date-filter";
        var yesterday = DateTime.UtcNow.AddDays(-1);
        var today     = DateTime.UtcNow;
        var tomorrow  = DateTime.UtcNow.AddDays(1);

        await _repo.InsertAsync(MakeLog(slug, timestamp: yesterday), CancellationToken.None);
        await _repo.InsertAsync(MakeLog(slug, timestamp: today),     CancellationToken.None);

        // from=today → should miss yesterday's log
        var (items, total) = await _repo.QueryAsync(
            slug, null, null, today.Date, null, 1, 10, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.All(items, i => Assert.True(i.Timestamp >= today.Date.ToUniversalTime()));
    }

    // ── pagination ────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_Pagination_ReturnsCorrectPage()
    {
        var slug = "tenant-pagination";
        for (int i = 0; i < 5; i++)
            await _repo.InsertAsync(MakeLog(slug), CancellationToken.None);

        var (itemsP1, total) = await _repo.QueryAsync(slug, null, null, null, null, 1, 3, CancellationToken.None);
        var (itemsP2, _)     = await _repo.QueryAsync(slug, null, null, null, null, 2, 3, CancellationToken.None);

        Assert.Equal(5, total);
        Assert.Equal(3, itemsP1.Count);
        Assert.Equal(2, itemsP2.Count);
    }

    // ── tenant isolation ──────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_TenantIsolation_CannotSeeCrossTenantLogs()
    {
        await _repo.InsertAsync(MakeLog("tenant-alpha", "ticket.created"), CancellationToken.None);
        await _repo.InsertAsync(MakeLog("tenant-beta",  "ticket.created"), CancellationToken.None);

        var (itemsAlpha, totalAlpha) = await _repo.QueryAsync(
            "tenant-alpha", null, null, null, null, 1, 100, CancellationToken.None);

        Assert.All(itemsAlpha, i => Assert.Equal("tenant-alpha", i.TenantSlug));
        // beta's log is not returned to alpha
        Assert.DoesNotContain(itemsAlpha, i => i.TenantSlug == "tenant-beta");
    }
}
