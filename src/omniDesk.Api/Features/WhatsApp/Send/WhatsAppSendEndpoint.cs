using omniDesk.Api.Features.WhatsApp.Send.Commands;

namespace omniDesk.Api.Features.WhatsApp.Send;

/// <summary>
/// Spec 008 — POST /api/whatsapp/send. Endpoint interno do CRM para envio de
/// mensagem (US3: texto livre; US4: + template). JWT-authenticated.
/// Conversation ownership e RBAC validados implicitamente — atendente só envia
/// em conversas atribuídas ao seu departamento/usuário (mesmo padrão da Spec 007
/// Inbox endpoints).
/// </summary>
public static class WhatsAppSendEndpoint
{
    public static RouteGroupBuilder MapWhatsAppSendEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/", SendAsync);
        return group;
    }

    private static async Task<IResult> SendAsync(
        SendWhatsAppMessageRequest request,
        HttpContext http,
        SendWhatsAppMessageCommand command,
        CancellationToken ct)
    {
        var attendantId = ResolveUserId(http);

        var result = await command.ExecuteAsync(attendantId, request.ConversationId, request.Content, ct);

        return result.Status switch
        {
            SendResultStatus.Sent =>
                Results.Ok(new { success = true }),

            SendResultStatus.NotFound =>
                Results.NotFound(Error("CONVERSATION_NOT_FOUND", "Conversa não encontrada.")),

            SendResultStatus.WrongChannel =>
                Results.BadRequest(Error("WRONG_CHANNEL", "Esta conversa não é do canal WhatsApp.")),

            SendResultStatus.ConversationClosed =>
                Results.BadRequest(Error("CONVERSATION_CLOSED", "Não é possível enviar em conversa encerrada.")),

            SendResultStatus.InvalidContent =>
                Results.BadRequest(Error("INVALID_CONTENT", "Conteúdo da mensagem é obrigatório.")),

            SendResultStatus.WindowExpired =>
                Results.UnprocessableEntity(Error(
                    "WA_OUTSIDE_WINDOW",
                    "A janela de 24h da Meta expirou. Selecione um template aprovado para enviar.")),

            SendResultStatus.AiTemplateForbidden =>
                Results.UnprocessableEntity(Error(
                    "WA_AI_TEMPLATE_FORBIDDEN",
                    "IA não pode enviar templates (FR-016).")),

            SendResultStatus.Failed =>
                Results.StatusCode(500),

            _ => Results.StatusCode(500),
        };
    }

    private static Guid ResolveUserId(HttpContext http)
    {
        var raw = http.User.FindFirst("user_id")?.Value
              ?? http.User.FindFirst("sub")?.Value
              ?? throw new InvalidOperationException("user_id claim missing.");
        return Guid.Parse(raw);
    }

    private static object Error(string code, string message)
        => new { success = false, error = new { code, message } };
}

public sealed record SendWhatsAppMessageRequest(Guid ConversationId, string Content);
