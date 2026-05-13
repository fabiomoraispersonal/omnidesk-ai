using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using omniDesk.Api.Infrastructure.Agenda;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Agenda;

/// <summary>
/// Spec 011 T079 — testa AppointmentEventStore: append imutável; query cronológica.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AppointmentEventStoreTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private IMongoClient? _mongo;

    public AppointmentEventStoreTests(TenantSchemaFixture fx) => _fx = fx;

    public Task InitializeAsync()
    {
        _mongo = new MongoClient(_fx.MongoConnectionString);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AppointmentEventStore BuildStore() =>
        new(_mongo!, NullLogger<AppointmentEventStore>.Instance);

    [Fact]
    public async Task Append_ThenGet_ReturnsCronologicalOrder()
    {
        var store         = BuildStore();
        var appointmentId = Guid.NewGuid();
        var actor         = Guid.NewGuid();

        await store.AppendAsync(new AppointmentEvent
        {
            TenantSlug     = TenantSchemaFixture.TenantSlug,
            AppointmentId  = appointmentId,
            Action         = "created",
            ActorType      = "attendant",
            ActorId        = actor,
        }, default);

        await store.AppendAsync(new AppointmentEvent
        {
            TenantSlug     = TenantSchemaFixture.TenantSlug,
            AppointmentId  = appointmentId,
            Action         = "confirmed",
            ActorType      = "attendant",
            ActorId        = actor,
        }, default);

        var events = await store.GetForAppointmentAsync(TenantSchemaFixture.TenantSlug, appointmentId, default);

        Assert.Equal(2, events.Count);
        Assert.Equal("created",   events[0].Action);
        Assert.Equal("confirmed", events[1].Action);
        Assert.True(events[0].CreatedAt <= events[1].CreatedAt);
    }

    [Fact]
    public async Task Append_DoesNotMutateExistingEvents()
    {
        var store         = BuildStore();
        var appointmentId = Guid.NewGuid();

        await store.AppendAsync(new AppointmentEvent
        {
            TenantSlug    = TenantSchemaFixture.TenantSlug,
            AppointmentId = appointmentId,
            Action        = "created",
            ActorType     = "attendant",
        }, default);

        var firstSnapshot = await store.GetForAppointmentAsync(TenantSchemaFixture.TenantSlug, appointmentId, default);

        await store.AppendAsync(new AppointmentEvent
        {
            TenantSlug    = TenantSchemaFixture.TenantSlug,
            AppointmentId = appointmentId,
            Action        = "confirmed",
            ActorType     = "attendant",
        }, default);

        var secondSnapshot = await store.GetForAppointmentAsync(TenantSchemaFixture.TenantSlug, appointmentId, default);

        Assert.Single(firstSnapshot);
        Assert.Equal(2, secondSnapshot.Count);
    }
}
