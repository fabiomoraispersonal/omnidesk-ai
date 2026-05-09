namespace omniDesk.Api.Features.AgentRuntime;

public record IncomingMessage(
    Guid TenantId,
    string TenantSlug,
    string ExternalConversationRef,
    string MessageId,
    string Content,
    DateTimeOffset SentAt);
