namespace omniDesk.Api.Features.LiveChat.Public;

/// <summary>Request body for POST /api/public/widget/conversations.</summary>
public record StartConversationRequest(
    Guid AnonymousId,
    bool LgpdConsent,
    StartConversationIdentification? Identification,
    StartConversationMetadata? Metadata);

public record StartConversationIdentification(string? Name, string? Email, string? Phone);

public record StartConversationMetadata(
    string? PageUrl,
    string? PageTitle,
    string? Referrer);
