using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Infrastructure.Persistence;
using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.AgentRuntime;

/// <summary>
/// Stub for ITicketCreationGateway used by Spec 006 until Spec 008 (Tickets V2) replaces it.
/// Inserts a row in `tickets` (Spec 005 scaffold) and persists conversation history snapshot.
/// </summary>
public class StubTicketCreationGateway : ITicketCreationGateway
{
    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ITenantSlugAccessor _slug;
    private readonly ILogger<StubTicketCreationGateway> _logger;

    public StubTicketCreationGateway(
        AppDbContext db,
        IConnectionMultiplexer redis,
        ITenantSlugAccessor slug,
        ILogger<StubTicketCreationGateway> logger)
    {
        _db = db;
        _redis = redis;
        _slug = slug;
        _logger = logger;
    }

    public async Task<TicketHandoffResult> CreateTicketFromAiHandoffAsync(
        TicketHandoffRequest request,
        CancellationToken ct)
    {
        var department = await _db.Departments
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId, ct)
            ?? throw new DepartmentNotFoundException(request.DepartmentId);

        var subject = string.IsNullOrWhiteSpace(request.Reason)
            ? "Atendimento humano solicitado"
            : (request.Reason.Length > 255 ? request.Reason[..255] : request.Reason);

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Subject = subject,
            DepartmentId = request.DepartmentId,
            Status = TicketStatus.Queued,
            SlaStartedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(ct);

        // Snapshot history into transitional table (Spec 008 will absorb).
        var historyJson = JsonSerializer.Serialize(request.History.Select(m => new
        {
            role = m.Role,
            content = m.Content,
            sent_at = m.SentAt,
        }));
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ai_handoff_snapshots (id, ticket_id, thread_id, history_json, created_at)
            VALUES (gen_random_uuid(), {ticket.Id}, {request.ThreadId}, {historyJson}::jsonb, now())
        ", ct);

        // Publish event for downstream consumers (Spec 005 round-robin, Spec 010 notifications).
        var sub = _redis.GetSubscriber();
        var channel = RedisChannel.Literal($"{_slug.Slug}:ws:dept:{request.DepartmentId}");
        var payload = JsonSerializer.Serialize(new
        {
            type = "ticket_created_from_ai",
            ticket_id = ticket.Id,
            ticket_number = $"TKT-{ticket.Number}",
            originating_agent_id = request.OriginatingAgentId,
            department_id = request.DepartmentId,
            timestamp = DateTimeOffset.UtcNow,
        });
        await sub.PublishAsync(channel, payload);

        _logger.LogInformation(
            "AI handoff ticket {TicketId} created in department {DepartmentName} from thread {ThreadId}",
            ticket.Id, department.Name, request.ThreadId);

        return new TicketHandoffResult(ticket.Id, $"TKT-{ticket.Number}", department.Name, ticket.Status.ToWireValue());
    }
}

public class DepartmentNotFoundException : Exception
{
    public Guid DepartmentId { get; }
    public DepartmentNotFoundException(Guid id) : base($"Department {id} not found or inactive.")
        => DepartmentId = id;
}
