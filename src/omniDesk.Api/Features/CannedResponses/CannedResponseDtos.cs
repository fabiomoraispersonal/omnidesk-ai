namespace omniDesk.Api.Features.CannedResponses;

public record CreateCannedResponseRequest(string Title, string Content, Guid? DepartmentId);

public record UpdateCannedResponseRequest(string Title, string Content, Guid? DepartmentId);

public record CannedResponseAuthor(Guid Id, string Name);

public record CannedResponseResponse(
    Guid Id,
    string Title,
    string Content,
    Guid? DepartmentId,
    string Scope,
    CannedResponseAuthor CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record RenderCannedResponseRequest(Guid TemplateId, RenderContextRequest Context);

public record RenderContextRequest(Guid? ConversationId, Guid? TicketId, Guid? AttendantId);

public record RenderCannedResponseResponse(string Rendered, IReadOnlyList<string> MissingVariables);
