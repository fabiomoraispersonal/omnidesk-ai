using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Npgsql;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Features.Notifications.Handlers;
using omniDesk.Api.Features.WhatsApp.Jobs;
using omniDesk.Api.Infrastructure.ActivityLogs;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Appointments;
using omniDesk.Api.Infrastructure.Metrics;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Tickets;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Jobs;

/// <summary>
/// Spec 010 T072 — AppointmentReminderJob early-exit paths. The success path requires
/// a full WhatsApp outgoing adapter (Meta API client, AES encryption, status repository,
/// session window guard) which is heavy to spin up; success-path verification is left to
/// a future end-to-end harness. These tests cover the FR-017 gates.
/// Requires Testcontainers (Postgres + Redis + Mongo).
/// </summary>
[Collection("Spec006-TenantSchema")]
public class AppointmentReminderJobTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;
    private IMongoClient? _mongo;

    public AppointmentReminderJobTests(TenantSchemaFixture fx) => _fx = fx;

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
        _mongo = new MongoClient(_fx.MongoConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }

    [Fact]
    public async Task RunAsync_TenantWithoutSettingsRow_ExitsEarly()
    {
        // No tenant_notification_settings row exists by default. Job must early-exit
        // without any side-effects (no exceptions, no notifications).
        var job = BuildJob();
        await job.RunAsync(TenantSchemaFixture.TenantSlug, default);

        var notifs = await _db!.Notifications.CountAsync();
        Assert.Equal(0, notifs);
    }

    [Fact]
    public async Task RunAsync_UnknownTenant_LogsAndExits()
    {
        var job = BuildJob();
        // Must not throw on unknown tenant slug.
        await job.RunAsync("does-not-exist", default);
    }

    [Fact]
    public async Task RunAsync_ReminderDisabled_DoesNothing()
    {
        var tenant = await _db!.Tenants.FirstAsync(t => t.Slug == TenantSchemaFixture.TenantSlug);
        _db.TenantNotificationSettings.Add(new TenantNotificationSettings
        {
            TenantId = tenant.Id,
            FollowUpEnabled = false,
            ReminderEnabled = false,
            ReminderTime = new TimeOnly(20, 0),
        });
        await _db.SaveChangesAsync();

        var job = BuildJob();
        await job.RunAsync(tenant.Slug, default);

        var notifs = await _db.Notifications.CountAsync();
        Assert.Equal(0, notifs);
    }

    private AppointmentReminderJob BuildJob()
    {
        // Null appointment repo → 0 appointments → no dispatch attempts.
        var appts = new NullAppointmentReadRepository();
        // Build a minimal failure handler with the real Mongo store; the success path
        // (which would invoke WhatsAppOutgoingAdapter) isn't reachable in these tests
        // because we never seed appointments.
        var failureHandler = new ReminderFailedHandler(
            _db!,
            new MongoTicketEventStore(_mongo!),
            new AgentActivityLogger(_mongo!, NullLogger<AgentActivityLogger>.Instance),
            NotificationTestHelpers.BuildService(_db!, _redis!, TenantSchemaFixture.TenantSlug),
            new omniDesk.Api.Features.Notifications.SupervisorLookupService(
                _db!, new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())),
            NullLogger<ReminderFailedHandler>.Instance);

        var metrics = new NotificationMetrics(new NotificationTestHelpers.TestMeterFactory());
        var tenantHolder = new TenantContextHolder();

        // The job needs a WhatsAppOutgoingAdapter, but with the null appointment repo it
        // never gets called. We pass `null!` (typed) — only reachable code paths matter.
        // If a future test wants success-path coverage, it must construct the full adapter.
        return new AppointmentReminderJob(
            _db!, _redis!,
            appts,
            outgoingAdapter: null!,
            failureHandler,
            tenantHolder,
            metrics,
            NullLogger<AppointmentReminderJob>.Instance);
    }
}
