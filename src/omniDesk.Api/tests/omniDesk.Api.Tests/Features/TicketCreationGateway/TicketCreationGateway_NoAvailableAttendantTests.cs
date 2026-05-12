using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Npgsql;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Features.Contacts;
using omniDesk.Api.Features.Distribution;
using omniDesk.Api.Features.Notifications;
using omniDesk.Api.Features.Tickets;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Contacts;
using omniDesk.Api.Infrastructure.Distribution;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Presence;
using omniDesk.Api.Infrastructure.Tickets;
using omniDesk.Api.Infrastructure.WebSockets;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Features.TicketCreationGatewayTests;

/// <summary>
/// Spec 009 T074 — No available attendant scenarios in TicketCreationGateway.
/// Requires Testcontainers (Docker) for Postgres + Redis + Mongo.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class TicketCreationGateway_NoAvailableAttendantTests : IAsyncLifetime
{
    private const string Slug = TenantSchemaFixture.TenantSlug;
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;
    private IMongoClient? _mongo;

    public TicketCreationGateway_NoAvailableAttendantTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task NoAttendantOnline_TicketIsNew_AttendantIdNull()
    {
        var dept = await SeedDepartmentAsync();
        var gateway = BuildGateway();

        var result = await gateway.CreateTicketFromAiHandoffAsync(
            MakeRequest(dept.Id), CancellationToken.None);

        Assert.Equal("new", result.Status);
        Assert.Null(result.AttendantId);

        var ticket = await _db!.Tickets.AsNoTracking().FirstAsync(t => t.Id == result.TicketId);
        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Null(ticket.AttendantId);
    }

    [Fact]
    public async Task UnknownDepartment_ThrowsTicketCreationException()
    {
        var gateway = BuildGateway();

        await Assert.ThrowsAsync<TicketCreationException>(() =>
            gateway.CreateTicketFromAiHandoffAsync(
                MakeRequest(Guid.NewGuid()), CancellationToken.None));
    }

    private TicketCreationGateway BuildGateway()
    {
        var redis = _redis!;
        var db = _db!;
        var slug = new TestSlugAccessor(Slug);
        var presence = new PresenceCache(redis);
        var ticketEvents = new TicketEventPublisher(redis);
        var assignmentSvc = new TicketAssignmentService(
            db,
            new TicketLock(redis),
            new RoundRobinCursorRedis(redis),
            new EligibleAttendantsQuery(db, presence),
            new DepartmentEventBus(redis),
            ticketEvents,
            NullLogger<TicketAssignmentService>.Instance);

        return new TicketCreationGateway(
            db,
            slug,
            new TicketProtocolService(db),
            assignmentSvc,
            new ContactDeduplicationService(new ContactRepository(db), redis, db),
            new MongoTicketEventStore(_mongo!),
            ticketEvents,
            new NoOpNotificationService());
    }

    private async Task<Department> SeedDepartmentAsync()
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = $"Dept-{Guid.NewGuid():N}"[..14],
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.Departments.Add(dept);
        await _db.SaveChangesAsync();
        return dept;
    }

    private static TicketHandoffRequest MakeRequest(Guid deptId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), deptId, "test", Guid.NewGuid(),
            TicketChannel.LiveChat, null, "Consulta", [], "livechat:test");

    private sealed class TestSlugAccessor(string slug) : ITenantSlugAccessor
    {
        public string Slug { get; } = slug;
    }
}
