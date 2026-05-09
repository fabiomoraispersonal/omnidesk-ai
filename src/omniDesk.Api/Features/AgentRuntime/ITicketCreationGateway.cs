namespace omniDesk.Api.Features.AgentRuntime;

/// <summary>
/// Bridge between AI handoff (Spec 006) and the Ticket layer (Spec 008).
/// Spec 006 ships a Stub that writes minimal rows in `tickets` (scaffold from Spec 005) plus
/// a snapshot of conversation history in `ai_handoff_snapshots`.
/// </summary>
public interface ITicketCreationGateway
{
    Task<TicketHandoffResult> CreateTicketFromAiHandoffAsync(
        TicketHandoffRequest request, CancellationToken ct);
}

public record TicketHandoffRequest(
    Guid ThreadId,
    Guid DepartmentId,
    string Reason,
    Guid? OriginatingAgentId,
    IReadOnlyList<ConversationMessage> History,
    string ExternalConversationRef);

public record TicketHandoffResult(
    Guid TicketId,
    string TicketNumber,
    string DepartmentName,
    string Status);
