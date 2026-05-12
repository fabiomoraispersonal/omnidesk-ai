using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Features.Notifications.Commands;
using omniDesk.Api.Infrastructure.Notifications;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Notifications;

/// <summary>
/// Spec 010 T085 — preferences upsert with event-type validation. Requires Testcontainers.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class PreferencesEndpointsTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public PreferencesEndpointsTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task GetWithNoRow_ReturnsDefaults()
    {
        var att = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var repo = new AttendantPreferencesRepository(_db!);

        var prefs = await repo.GetAsync(att, default);
        Assert.True(prefs.PushEnabled);
        Assert.Empty(prefs.EventPushFlags);
        // Default-on for any event type (absent key = true).
        Assert.True(prefs.ShouldPush(NotificationEventTypes.TicketAssigned));
    }

    [Fact]
    public async Task Upsert_PersistsFlags_AndCommandRejectsUnknownEventType()
    {
        var att = await NotificationTestHelpers.SeedAttendantAsync(_db!);
        var repo = new AttendantPreferencesRepository(_db!);
        var cmd = new UpdatePreferencesCommand(repo);

        var bad = await cmd.ExecuteAsync(att, true, new Dictionary<string, bool>
        {
            ["not.an.event"] = false,
        }, default);
        Assert.Equal(UpdatePreferencesError.InvalidEventType, bad.Error);
        Assert.Equal("not.an.event", bad.InvalidKey);

        var good = await cmd.ExecuteAsync(att, true, new Dictionary<string, bool>
        {
            [NotificationEventTypes.TicketQueued] = false,
        }, default);
        Assert.Equal(UpdatePreferencesError.None, good.Error);
        Assert.NotNull(good.Preferences);
        Assert.False(good.Preferences!.ShouldPush(NotificationEventTypes.TicketQueued));
        Assert.True(good.Preferences.ShouldPush(NotificationEventTypes.TicketAssigned));
    }
}
