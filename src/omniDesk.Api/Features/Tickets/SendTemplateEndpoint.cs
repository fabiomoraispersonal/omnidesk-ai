using omniDesk.Api.Features.Tickets.Commands;
using omniDesk.Api.Features.WhatsApp.Send.Commands;

namespace omniDesk.Api.Features.Tickets;

/// <summary>Spec 010 US5 T082 — POST /api/tickets/{ticket_id}/send-template.</summary>
public static class SendTemplateEndpoint
{
    public record SendTemplateRequest(
        Guid TemplateId,
        Dictionary<string, string>? Variables);

    public static RouteGroupBuilder MapSendTemplateEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/{ticketId:guid}/send-template", HandleAsync).WithName("Tickets_SendTemplate");
        return group;
    }

    private static async Task<IResult> HandleAsync(
        Guid ticketId,
        SendTemplateRequest request,
        SendManualTemplateCommand command,
        CancellationToken ct)
    {
        if (request.TemplateId == Guid.Empty)
        {
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "TEMPLATE_REQUIRED", message = "template_id is required." },
            });
        }

        var variables = request.Variables ?? new Dictionary<string, string>();
        var outcome = await command.ExecuteAsync(ticketId, request.TemplateId, variables, ct);

        return outcome switch
        {
            { Error: SendManualTemplateError.None } =>
                Results.Accepted(value: new { success = true, data = new { status = "enqueued" } }),

            { Error: SendManualTemplateError.TicketNotFound } =>
                Results.NotFound(SemanticError("TICKET_NOT_FOUND", "Ticket não encontrado.")),

            { Error: SendManualTemplateError.NotAuthorized } =>
                Results.Json(SemanticError("TICKET_NOT_ASSIGNED_TO_USER",
                    "Apenas o atendente responsável ou um administrador pode enviar templates neste ticket."),
                    statusCode: 403),

            { Error: SendManualTemplateError.TicketHasNoConversation } =>
                Results.UnprocessableEntity(SemanticError("TICKET_HAS_NO_CONVERSATION",
                    "O ticket não está vinculado a uma conversa WhatsApp.")),

            { Error: SendManualTemplateError.DelegationFailed } =>
                MapDelegated(outcome.Delegated, outcome.Detail),

            _ => Results.StatusCode(500),
        };
    }

    private static IResult MapDelegated(SendResult? r, string? detail)
    {
        if (r is null) return Results.StatusCode(500);
        return r.Status switch
        {
            SendResultStatus.NotFound =>
                Results.NotFound(SemanticError("CONVERSATION_NOT_FOUND", "Conversa não encontrada.")),
            SendResultStatus.WrongChannel =>
                Results.UnprocessableEntity(SemanticError("WRONG_CHANNEL",
                    "A conversa vinculada não é do canal WhatsApp.")),
            SendResultStatus.ConversationClosed =>
                Results.UnprocessableEntity(SemanticError("CONVERSATION_CLOSED",
                    "Não é possível enviar em conversa encerrada.")),
            SendResultStatus.TemplateNotFound =>
                Results.NotFound(SemanticError("TEMPLATE_NOT_FOUND", "Template não encontrado.")),
            SendResultStatus.TemplateNotApproved =>
                Results.UnprocessableEntity(SemanticError("TEMPLATE_NOT_APPROVED",
                    "Apenas templates aprovados podem ser enviados.")),
            SendResultStatus.TemplateVariableMismatch =>
                Results.UnprocessableEntity(SemanticError("TEMPLATE_VARIABLES_MISSING",
                    "Variáveis preenchidas não correspondem ao template.")),
            SendResultStatus.AiTemplateForbidden =>
                Results.UnprocessableEntity(SemanticError("WA_AI_TEMPLATE_FORBIDDEN",
                    "IA não pode enviar templates.")),
            SendResultStatus.WindowExpired =>
                // The whole point of manual template send is the window-expired case, so this
                // shouldn't trigger when called from the ticket detail flow. Surface as a 4xx anyway.
                Results.UnprocessableEntity(SemanticError("WA_OUTSIDE_WINDOW",
                    "Janela de 24h expirada — selecione um template aprovado.")),
            SendResultStatus.InvalidContent =>
                Results.UnprocessableEntity(SemanticError("INVALID_CONTENT", "Conteúdo inválido.")),
            _ =>
                Results.StatusCode(500),
        };
    }

    private static object SemanticError(string code, string message) =>
        new { success = false, error = new { code, message } };
}
