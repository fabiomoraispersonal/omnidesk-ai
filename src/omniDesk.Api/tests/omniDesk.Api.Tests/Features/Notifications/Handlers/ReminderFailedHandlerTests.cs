using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Npgsql;
using omniDesk.Api.Domain.Notifications;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Features.Notifications;
using omniDesk.Api.Features.Notifications.Handlers;
using omniDesk.Api.Infrastructure.ActivityLogs;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Tickets;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.Notifications.Handlers;

/// <summary>
/// Spec 010 T073 — ReminderFailedHandler branches on ticket-linked vs standalone. Both
/// paths must produce immutable audit + notification. Requires Testcontainers.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class ReminderFailedHandlerTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;
    private IMongoClient? _mongo;

    public ReminderFailedHandlerTests(TenantSchemaFixture fx) => _fx = fx;

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

    private ReminderFailedHandler Build()
    {
        var svc = NotificationTestHelpers.BuildService(_db!, _redis!, TenantSchemaFixture.TenantSlug);
        var sup = new SupervisorLookupService(_db!, new MemoryCache(new MemoryCacheOptions()));
        return new ReminderFailedHandler(
            _db!,
            new MongoTicketEventStore(_mongo!),
            new AgentActivityLogger(_mongo!, NullLogger<AgentActivityLogger>.Instance),
            svc, sup,
            NullLogger<ReminderFailedHandler>.Instance);
    }

    [Fact]
    public async Task TicketLinkedFailure_SetsAlertFlag_AndNotifiesAttendant()
    {
        var dept = await NotificationTestHelpers.SeedDepartmentAsync(_db!);
        var attendantId = await NotificationTestHelpers.SeedAttendantAsync(_db!, deptId: dept.Id);
        var ticket = await SeedTicketAsync(dept.Id, attendantId);

        var handler = Build();
        await handler.HandleAsync(
            TenantSchemaFixture.TenantSlug,
            appointmentId: Guid.NewGuid(),
            ticketId: ticket.Id,
            contactId: null,
            departmentId: dept.Id,
            contactName: "Maria",
            reason: "no_phone",
            default);

        // Flag set on ticket.
        var refreshed = await _db!.Tickets.AsNoTracking().FirstAsync(t => t.Id == ticket.Id);
        Assert.True(refreshed.HasReminderAlert);

        // Notification dispatched to the assigned attendant.
        var notif = await _db.Notifications.AsNoTracking()
            .FirstAsync(n => n.AttendantId == attendantId
                             && n.EventType == NotificationEventTypes.TicketReminderFailed);
        Assert.Equal(ticket.Id, notif.EntityId);
    }

    [Fact]
    public async Task StandaloneFailure_NotifiesDepartmentSupervisors()
    {
        var dept = await NotificationTestHelpers.SeedDepartmentAsync(_db!);
        var admin = await NotificationTestHelpers.SeedAttendantAsync(_db!, UserRole.TenantAdmin);

        var handler = Build();
        await handler.HandleAsync(
            TenantSchemaFixture.TenantSlug,
            appointmentId: Guid.NewGuid(),
            ticketId: null,
            contactId: Guid.NewGuid(),
            departmentId: dept.Id,
            contactName: "Pedro",
            reason: "no_phone",
            default);

        var notif = await _db!.Notifications.AsNoTracking()
            .FirstAsync(n => n.AttendantId == admin
                             && n.EventType == NotificationEventTypes.TicketReminderFailed);
        Assert.Contains("Pedro", notif.Body);
    }

    [Fact]
    public async Task TicketWithoutAttendant_FallsBackToSupervisors()
    {
        var dept = await NotificationTestHelpers.SeedDepartmentAsync(_db!);
        var admin = await NotificationTestHelpers.SeedAttendantAsync(_db!, UserRole.TenantAdmin);
        var ticket = await SeedTicketAsync(dept.Id, attendantId: null);

        var handler = Build();
        await handler.HandleAsync(
            TenantSchemaFixture.TenantSlug,
            appointmentId: Guid.NewGuid(),
            ticketId: ticket.Id,
            contactId: null,
            departmentId: dept.Id,
            contactName: "Ana",
            reason: "no_phone",
            default);

        // Admin is a supervisor-class recipient.
        var rows = await _db!.Notifications.AsNoTracking()
            .Where(n => n.EventType == NotificationEventTypes.TicketReminderFailed)
            .ToListAsync();
        Assert.Contains(rows, r => r.AttendantId == admin);
    }

    private async Task<Ticket> SeedTicketAsync(Guid deptId, Guid? attendantId)
    {
        var now = DateTimeOffset.UtcNow;
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Protocol = $"TK-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            DepartmentId = deptId,
            AttendantId = attendantId,
            Status = TicketStatus.InProgress,
            Subject = "agendamento",
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db!.Tickets.Add(ticket);
        await _db.SaveChangesAsync();
        return ticket;
    }
}
