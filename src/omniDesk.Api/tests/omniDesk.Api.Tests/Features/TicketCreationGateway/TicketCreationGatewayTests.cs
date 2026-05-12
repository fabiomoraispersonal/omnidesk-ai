using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Npgsql;
using omniDesk.Api.Domain.Attendants;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Domain.Users;
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
/// Spec 009 T072 [US1] — TicketCreationGateway integration tests.
/// Requires Testcontainers (Docker) for Postgres + Redis + Mongo.
/// </summary>
[Collection("Spec006-TenantSchema")]
public class TicketCreationGatewayTests : IAsyncLifetime
{
    private const string Slug = TenantSchemaFixture.TenantSlug;
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;
    private IMongoClient? _mongo;

    public TicketCreationGatewayTests(TenantSchemaFixture fx) => _fx = fx;

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
    public async Task Handoff_CreatesTicket_WithProtocol_And_CorrectChannel()
    {
        var dept = await SeedDepartmentAsync();
        var gateway = BuildGateway();

        var request = new TicketHandoffRequest(
            ConversationId: Guid.NewGuid(),
            ThreadId: Guid.NewGuid(),
            DepartmentId: dept.Id,
            Reason: "test handoff",
            OriginatingAgentId: Guid.NewGuid(),
            Channel: TicketChannel.LiveChat,
            ContactHints: null,
            SubjectSuggestion: "Consulta odontológica",
            History: [],
            ExternalConversationRef: "livechat:test-001");

        var result = await gateway.CreateTicketFromAiHandoffAsync(request, CancellationToken.None);

        Assert.NotNull(result.Protocol);
        Assert.Matches(@"^TK-\d{8}-\d{5}$", result.Protocol);
        Assert.Equal(dept.Id, result.DepartmentId);

        var ticket = await _db!.Tickets.AsNoTracking().FirstAsync(t => t.Id == result.TicketId);
        Assert.Equal(TicketChannel.LiveChat, ticket.Channel);
        Assert.Equal("Consulta odontológica", ticket.Subject);
    }

    [Fact]
    public async Task Handoff_WithContactHints_DeduplicatesContact()
    {
        var dept = await SeedDepartmentAsync();
        var gateway = BuildGateway();

        var hints = new ContactHints("joao@example.com", "11999990001", "João");

        var r1 = await gateway.CreateTicketFromAiHandoffAsync(MakeRequest(dept.Id, hints), CancellationToken.None);
        var r2 = await gateway.CreateTicketFromAiHandoffAsync(MakeRequest(dept.Id, hints), CancellationToken.None);

        Assert.NotNull(r1.ContactId);
        Assert.Equal(r1.ContactId, r2.ContactId);

        var contacts = await _db!.Contacts.ToListAsync();
        Assert.Single(contacts);
    }

    [Fact]
    public async Task Handoff_RoundRobin_AssignsTicketsAcrossAttendants()
    {
        var (dept, attendantIds) = await SeedDeptWithOnlineAttendantsAsync(2);
        var gateway = BuildGateway();

        var r1 = await gateway.CreateTicketFromAiHandoffAsync(MakeRequest(dept.Id), CancellationToken.None);
        var r2 = await gateway.CreateTicketFromAiHandoffAsync(MakeRequest(dept.Id), CancellationToken.None);

        Assert.NotNull(r1.AttendantId);
        Assert.NotNull(r2.AttendantId);
        Assert.NotEqual(r1.AttendantId, r2.AttendantId);
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

    private async Task<(Department dept, Guid[] attendantIds)> SeedDeptWithOnlineAttendantsAsync(int count)
    {
        var dept = await SeedDepartmentAsync();
        var ids = new List<Guid>();
        var presence = new PresenceCache(_redis!);
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < count; i++)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = $"att{i}-{Guid.NewGuid():N}@test.local",
                Name = $"Att{i}",
                PasswordHash = "x",
                Role = UserRole.Attendant,
                IsActive = true,
                EmailVerified = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db!.Users.Add(user);
            await _db.SaveChangesAsync();

            var att = new Attendant
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Name = user.Name,
                MaxSimultaneousChats = 5,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Attendants.Add(att);
            _db.AttendantDepartments.Add(new AttendantDepartment
            {
                AttendantId = att.Id,
                DepartmentId = dept.Id,
                IsPrimary = i == 0,
                CreatedAt = now,
            });
            await _db.SaveChangesAsync();

            await presence.SetAsync(Slug, att.Id, new PresenceSnapshot(
                AttendanceStatus.Online, now, AttendanceStatusChangedBy.Manual, now));

            ids.Add(att.Id);
        }

        return (dept, ids.ToArray());
    }

    private static TicketHandoffRequest MakeRequest(Guid deptId, ContactHints? hints = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), deptId, "test", Guid.NewGuid(),
            TicketChannel.LiveChat, hints, "Consulta", [], "livechat:test");

    private sealed class TestSlugAccessor(string slug) : ITenantSlugAccessor
    {
        public string Slug { get; } = slug;
    }
}
