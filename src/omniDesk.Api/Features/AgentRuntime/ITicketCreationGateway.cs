using omniDesk.Api.Domain.Tickets;

namespace omniDesk.Api.Features.AgentRuntime;

/// <summary>
/// Bridge between AI handoff (Spec 006) and the Ticket layer (Spec 009).
/// Spec 009 delivers the real implementation; stub in Infrastructure/AgentRuntime/ is kept for rollback.
/// </summary>
public interface ITicketCreationGateway
{
    Task<TicketHandoffResult> CreateTicketFromAiHandoffAsync(
        TicketHandoffRequest request, CancellationToken ct);
}

public record TicketHandoffRequest(
    Guid ConversationId,
    Guid ThreadId,
    Guid DepartmentId,
    string Reason,
    Guid? OriginatingAgentId,
    TicketChannel Channel,
    ContactHints? ContactHints,
    string? SubjectSuggestion,
    IReadOnlyList<ConversationMessage> History,
    string ExternalConversationRef);

public record ContactHints(
    string? Email,
    string? Phone,
    string? Name);

public record TicketHandoffResult(
    Guid TicketId,
    string Protocol,
    Guid DepartmentId,
    string DepartmentName,
    Guid? AttendantId,
    string Status,
    Guid? ContactId);
