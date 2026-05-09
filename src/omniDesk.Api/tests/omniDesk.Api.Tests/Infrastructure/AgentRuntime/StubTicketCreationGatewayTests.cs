using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using omniDesk.Api.Domain.Departments;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.AgentRuntime;

/// <summary>
/// Cross-spec §005-D / contracts/ticket-creation-gateway.md:
/// — cria ticket em queued
/// — anexa snapshot em ai_handoff_snapshots
/// — publica evento Redis no canal {slug}:ws:dept:{id}
/// </summary>
[Collection("Spec006-TenantSchema")]
public class StubTicketCreationGatewayTests : IAsyncLifetime
{
    private readonly TenantSchemaFixture _fx;
    private AppDbContext? _db;
    private ConnectionMultiplexer? _redis;
    private TestSlugAccessor? _slug;

    public StubTicketCreationGatewayTests(TenantSchemaFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.TruncateTenantTablesAsync();
        var csb = new NpgsqlConnectionStringBuilder(_fx.PostgresConnectionString)
        {
            SearchPath = $"{TenantSchemaFixture.TenantSchema},public",
        };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(csb.ConnectionString).Options;
        _db = new AppDbContext(options);
        _redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        _slug = new TestSlugAccessor(TenantSchemaFixture.TenantSlug);
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }

    [Fact]
    public async Task CreateTicket_Inserts_QueuedTicket_And_Snapshot()
    {
        var dept = await SeedDepartmentAsync();
        var threadId = await SeedThreadAsync();

        var gateway = new StubTicketCreationGateway(_db!, _redis!, _slug!,
            NullLogger<StubTicketCreationGateway>.Instance);

        var result = await gateway.CreateTicketFromAiHandoffAsync(
            new TicketHandoffRequest(
                threadId, dept.Id, "Cliente solicitou humano",
                Guid.NewGuid(),
                new[]
                {
                    new ConversationMessage("user", "Olá", DateTimeOffset.UtcNow),
                    new ConversationMessage("assistant", "Em que posso ajudar?", DateTimeOffset.UtcNow),
                },
                "livechat:abc-123"),
            CancellationToken.None);

        Assert.Equal("queued", result.Status);
        Assert.Equal("Comercial", result.DepartmentName);
        Assert.StartsWith("TKT-", result.TicketNumber);

        var ticket = await _db!.Tickets.FirstAsync(t => t.Id == result.TicketId);
        Assert.Equal(dept.Id, ticket.DepartmentId);
        Assert.NotNull(ticket.SlaStartedAt);

        // Verify snapshot row.
        await using var conn = new NpgsqlConnection(_fx.PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $@"SELECT history_json FROM ""{TenantSchemaFixture.TenantSchema}"".ai_handoff_snapshots
               WHERE ticket_id = @tid", conn);
        cmd.Parameters.AddWithValue("tid", result.TicketId);
        var json = (string?)await cmd.ExecuteScalarAsync();
        Assert.NotNull(json);
        Assert.Contains("\"role\":\"user\"", json);
        Assert.Contains("\"role\":\"assistant\"", json);
    }

    [Fact]
    public async Task CreateTicket_Truncates_Subject_To_255()
    {
        var dept = await SeedDepartmentAsync();
        var threadId = await SeedThreadAsync();
        var gateway = new StubTicketCreationGateway(_db!, _redis!, _slug!,
            NullLogger<StubTicketCreationGateway>.Instance);

        var longReason = new string('x', 400);
        var result = await gateway.CreateTicketFromAiHandoffAsync(
            new TicketHandoffRequest(threadId, dept.Id, longReason, null,
                Array.Empty<ConversationMessage>(), "livechat:long"),
            CancellationToken.None);

        var ticket = await _db!.Tickets.FirstAsync(t => t.Id == result.TicketId);
        Assert.Equal(255, ticket.Subject.Length);
    }

    [Fact]
    public async Task CreateTicket_PublishesRedisEvent_OnTenantDeptChannel()
    {
        var dept = await SeedDepartmentAsync();
        var threadId = await SeedThreadAsync();
        var gateway = new StubTicketCreationGateway(_db!, _redis!, _slug!,
            NullLogger<StubTicketCreationGateway>.Instance);

        var sub = _redis!.GetSubscriber();
        var received = new TaskCompletionSource<string>();
        var channel = RedisChannel.Literal($"{TenantSchemaFixture.TenantSlug}:ws:dept:{dept.Id}");
        await sub.SubscribeAsync(channel, (_, value) => received.TrySetResult(value!));

        await gateway.CreateTicketFromAiHandoffAsync(
            new TicketHandoffRequest(threadId, dept.Id, "humano", null,
                Array.Empty<ConversationMessage>(), "livechat:evt"),
            CancellationToken.None);

        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Contains("\"type\":\"ticket_created_from_ai\"", payload);
        Assert.Contains("\"ticket_number\":\"TKT-", payload);
    }

    [Fact]
    public async Task CreateTicket_UnknownDepartment_Throws()
    {
        var threadId = await SeedThreadAsync();
        var gateway = new StubTicketCreationGateway(_db!, _redis!, _slug!,
            NullLogger<StubTicketCreationGateway>.Instance);

        await Assert.ThrowsAsync<DepartmentNotFoundException>(() =>
            gateway.CreateTicketFromAiHandoffAsync(
                new TicketHandoffRequest(threadId, Guid.NewGuid(), "x", null,
                    Array.Empty<ConversationMessage>(), "livechat:404"),
                CancellationToken.None));
    }

    private async Task<Department> SeedDepartmentAsync()
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Comercial",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.Departments.Add(dept);
        await _db.SaveChangesAsync();
        return dept;
    }

    private async Task<Guid> SeedThreadAsync()
    {
        var thread = new omniDesk.Api.Domain.AiThreads.AiThread
        {
            Id = Guid.NewGuid(),
            ExternalConversationRef = $"livechat:{Guid.NewGuid():n}",
            OpenAiThreadId = $"thread_{Guid.NewGuid():n}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db!.AiThreads.Add(thread);
        await _db.SaveChangesAsync();
        return thread.Id;
    }

    private sealed class TestSlugAccessor : ITenantSlugAccessor
    {
        public TestSlugAccessor(string slug) => Slug = slug;
        public string Slug { get; }
    }
}
