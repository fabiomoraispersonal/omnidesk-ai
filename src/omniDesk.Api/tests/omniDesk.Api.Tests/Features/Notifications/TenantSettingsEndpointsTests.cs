using Microsoft.EntityFrameworkCore;
using Npgsql;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Features.Notifications.Commands;
using omniDesk.Api.Features.Notifications.Schedulers;
using omniDesk.Api.Infrastructure.Notifications;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.Notifications;

/// <summary>
/// Spec 010 T091 — tenant settings upsert + scheduler bridge. Requires Testcontainers.
/// HTTP-level role enforcement (TenantAdmin) is verified in the endpoint code path; this
/// test exercises the command directly.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class TenantSettingsEndpointsTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;

    public TenantSettingsEndpointsTests(TenantSchemaFixture fx) => _fx = fx;

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
        var repo = new TenantSettingsRepository(_db!);
        var settings = await repo.GetAsync(_fx.TenantId, default);

        Assert.False(settings.FollowUpEnabled);
        Assert.False(settings.ReminderEnabled);
        Assert.Equal(new TimeOnly(20, 0), settings.ReminderTime);
    }

    [Fact]
    public async Task UpsertViaCommand_InvokesScheduler_AndPersists()
    {
        var repo = new TenantSettingsRepository(_db!);
        var spy = new SpyScheduler();
        var cmd = new UpdateTenantSettingsCommand(repo, spy);

        var settings = await cmd.ExecuteAsync(
            _fx.TenantId, followUpEnabled: true, reminderEnabled: true,
            reminderTime: new TimeOnly(7, 30), default);

        Assert.True(settings.FollowUpEnabled);
        Assert.Equal(new TimeOnly(7, 30), settings.ReminderTime);
        Assert.Equal(1, spy.ApplyCalls);

        // Toggling off triggers another Apply (which the real scheduler maps to RemoveIfExists).
        await cmd.ExecuteAsync(
            _fx.TenantId, false, false, new TimeOnly(20, 0), default);
        Assert.Equal(2, spy.ApplyCalls);
    }

    private sealed class SpyScheduler : IAppointmentReminderScheduler
    {
        public int ApplyCalls { get; private set; }
        public Task ApplyAsync(Guid tenantId, TenantNotificationSettings settings, CancellationToken ct)
        {
            ApplyCalls++;
            return Task.CompletedTask;
        }
    }
}
