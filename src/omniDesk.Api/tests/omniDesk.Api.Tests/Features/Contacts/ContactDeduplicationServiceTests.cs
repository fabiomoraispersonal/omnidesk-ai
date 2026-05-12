using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Contacts;
using omniDesk.Api.Features.Contacts;
using omniDesk.Api.Infrastructure.Contacts;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.Contacts;

/// <summary>
/// Spec 009 T155 — ContactDeduplicationService race-safety and merge tests.
/// Requires Testcontainers (Docker) for Postgres + Redis.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ContactDeduplicationServiceTests : IAsyncLifetime
{
    private const string Slug = TenantSchemaFixture.TenantSlug;
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;

    public ContactDeduplicationServiceTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task ThreeParallelInserts_SameEmail_ProduceOneContact()
    {
        var email = $"race-{Guid.NewGuid():N}@test.local";
        var hints = new ContactDeduplicationService.ContactHints(
            email, null, "Test User", ContactSourceChannel.LiveChat);

        // Parallel inserts — only one contact should be created due to Redis lock + unique index
        var tasks = Enumerable.Range(0, 3)
            .Select(_ =>
            {
                // Each task needs its own AppDbContext to simulate concurrent requests
                var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
                {
                    SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
                };
                var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(csb.ConnectionString).Options);
                var svc = new ContactDeduplicationService(new ContactRepository(db), _redis!, db);
                return (svc.FindOrCreateAsync(Slug, hints, CancellationToken.None), db);
            })
            .ToList();

        var results = await Task.WhenAll(tasks.Select(t => t.Item1));
        foreach (var (_, db) in tasks) await db.DisposeAsync();

        // All calls should return the same contact id
        var distinctIds = results.Select(c => c.Id).Distinct().ToList();
        Assert.Single(distinctIds);

        // Only one contact in the database
        var count = await _db!.Contacts.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task FindOrCreate_ExistingEmail_ReturnsExistingContact()
    {
        var email = "existing@test.local";
        var svc = BuildService();

        var first = await svc.FindOrCreateAsync(Slug,
            new ContactDeduplicationService.ContactHints(email, null, "Alice", ContactSourceChannel.LiveChat),
            CancellationToken.None);

        var second = await svc.FindOrCreateAsync(Slug,
            new ContactDeduplicationService.ContactHints(email, null, "Alice Updated", ContactSourceChannel.WhatsApp),
            CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await _db!.Contacts.CountAsync());
    }

    [Fact]
    public async Task FindOrCreate_NoEmailNoPhone_CreatesAnonymousContact()
    {
        var svc = BuildService();

        var contact = await svc.FindOrCreateAsync(Slug,
            new ContactDeduplicationService.ContactHints(null, null, "Anonymous", ContactSourceChannel.LiveChat),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, contact.Id);
        // Each anonymous call creates a new contact (no key to dedup on)
        var contact2 = await svc.FindOrCreateAsync(Slug,
            new ContactDeduplicationService.ContactHints(null, null, "Anonymous2", ContactSourceChannel.LiveChat),
            CancellationToken.None);

        Assert.NotEqual(contact.Id, contact2.Id);
        Assert.Equal(2, await _db!.Contacts.CountAsync());
    }

    private ContactDeduplicationService BuildService() =>
        new(new ContactRepository(_db!), _redis!, _db!);
}
