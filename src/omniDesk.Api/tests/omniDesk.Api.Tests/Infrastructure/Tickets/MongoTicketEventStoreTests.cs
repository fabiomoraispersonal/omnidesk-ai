using MongoDB.Driver;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Infrastructure.Tickets;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Tickets;

/// <summary>
/// Spec 009 T068 — MongoTicketEventStore append + read-back via real Mongo (Testcontainers).
/// Requires Docker.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class MongoTicketEventStoreTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private ITicketEventStore? _store;

    public MongoTicketEventStoreTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        _store = new MongoTicketEventStore(new MongoClient(_fx.MongoConnectionString));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Append_ThenReadBack_ReturnsEvent()
    {
        var ticketId = Guid.NewGuid();
        var slug     = TenantSchemaFixture.TenantSlug;

        var evt = new TicketEvent(slug, ticketId, "TK-20260101-00001",
            TicketEventType.TicketCreated, "system", DateTimeOffset.UtcNow)
        {
            ActorId = Guid.NewGuid(),
        };

        await _store!.AppendAsync(evt);

        var events = await _store.GetByTicketAsync(slug, ticketId);

        Assert.Single(events);
        Assert.Equal(ticketId, events[0].TicketId);
        Assert.Equal(TicketEventType.TicketCreated, events[0].EventType);
    }

    [Fact]
    public async Task MultipleAppends_OrderedByTimestamp()
    {
        var ticketId = Guid.NewGuid();
        var slug     = TenantSchemaFixture.TenantSlug;
        var now      = DateTimeOffset.UtcNow;

        await _store!.AppendAsync(new TicketEvent(slug, ticketId, "TK-20260101-00002",
            TicketEventType.AttendantAssigned, "system", now.AddSeconds(-5)));
        await _store!.AppendAsync(new TicketEvent(slug, ticketId, "TK-20260101-00002",
            TicketEventType.StatusChanged, "system", now));

        var events = await _store.GetByTicketAsync(slug, ticketId);

        Assert.Equal(2, events.Count);
        Assert.True(events[0].Timestamp <= events[1].Timestamp);
    }
}
