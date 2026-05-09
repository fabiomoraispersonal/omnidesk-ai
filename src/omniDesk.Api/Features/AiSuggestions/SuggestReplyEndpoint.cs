using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Tickets;
using omniDesk.Api.Features.Distribution;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.AiSuggestions;

public record SuggestReplyRequestDto(int? ContextMessageCount);

public record SuggestReplyResponseDto(
    string SuggestionId,
    string Text,
    string Model,
    long ElapsedMs,
    int InputTokens,
    int OutputTokens,
    object ContextUsed);

public record UpdateSuggestionActionRequest(string HumanAction, string? FinalMessageText);

public static class SuggestReplyEndpoint
{
    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        group.MapPost("/{conversationId:guid}/suggest-reply", HandleAsync)
             .RequireAuthorization()
             .RequireRateLimiting("ai-suggestion");
        group.MapPatch("/{conversationId:guid}/suggestions/{suggestionId}", HandleActionAsync)
             .RequireAuthorization();
        return group;
    }

    private static async Task<IResult> HandleAsync(
        Guid conversationId,
        SuggestReplyRequestDto? request,
        SuggestReplyService service,
        AppDbContext db,
        ICurrentUser currentUser,
        IConfiguration config,
        CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId) return Results.Unauthorized();

        var attendant = await db.Attendants.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.IsActive, ct);
        if (attendant is null)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "NOT_AN_ATTENDANT", message = "Apenas atendentes podem solicitar sugestão." }
            });

        var slug = await AssignTicketEndpoint.ResolveTenantSlugAsync(currentUser, db, ct);
        if (slug is null)
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "TENANT_SLUG_NOT_RESOLVED", message = "Não foi possível resolver o tenant." }
            });

        // Resolve dept + ticket from conversation_id (Spec 008 owns ConversationId mapping;
        // pragma: assume conversation_id == ticket_id in MVP scaffold).
        var ticket = await db.Tickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == conversationId, ct);
        if (ticket is null)
            return Results.NotFound(new
            {
                success = false,
                error = new { code = "CONVERSATION_NOT_FOUND", message = "Conversa não encontrada." }
            });

        if (ticket.AssignedAttendantId != attendant.Id)
        {
            // Allow supervisors with CanViewAllConversations to also suggest.
            // The proper check happens via the Spec 004 policy when implemented.
            if (currentUser.Role != "supervisor" && currentUser.Role != "tenant_admin")
                return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var defaultMax = int.TryParse(config["Ai:MaxSuggestionContextMessages"], out var n) ? n : 20;
        var ctxRequest = new SuggestionRequestContext(
            ConversationId: conversationId,
            AttendantId: attendant.Id,
            DepartmentId: ticket.DepartmentId,
            TicketId: ticket.Id,
            MaxContextMessages: request?.ContextMessageCount ?? defaultMax);

        var outcome = await service.SuggestAsync(slug, ctxRequest, ct);

        if (outcome.Failure is { } failure)
        {
            return failure switch
            {
                SuggestionFailure.Timeout => Results.Json(new
                {
                    success = false,
                    error = new { code = "AI_PROVIDER_TIMEOUT", message = "O provedor de IA demorou demais. Tente novamente em instantes." }
                }, statusCode: StatusCodes.Status504GatewayTimeout),
                SuggestionFailure.RateLimit => Results.Json(new
                {
                    success = false,
                    error = new { code = "AI_RATE_LIMIT", message = "Muitas sugestões em pouco tempo. Aguarde antes de tentar novamente." }
                }, statusCode: StatusCodes.Status429TooManyRequests),
                _ => Results.Json(new
                {
                    success = false,
                    error = new { code = "AI_PROVIDER_ERROR", message = "Não foi possível gerar a sugestão agora. A conversa segue normal." }
                }, statusCode: StatusCodes.Status502BadGateway),
            };
        }

        var resp = outcome.Response!;
        return Results.Ok(new
        {
            success = true,
            data = new SuggestReplyResponseDto(
                resp.SuggestionId,
                resp.Text,
                resp.Model,
                resp.ElapsedMs,
                resp.InputTokens,
                resp.OutputTokens,
                new
                {
                    sub_agent_id = resp.SubAgentId,
                    sub_agent_name = resp.SubAgentName,
                    messages_used = resp.MessagesUsed,
                })
        });
    }

    private static async Task<IResult> HandleActionAsync(
        Guid conversationId,
        string suggestionId,
        UpdateSuggestionActionRequest request,
        AiSuggestionLogger logger,
        AppDbContext db,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        if (currentUser.UserId is not Guid userId) return Results.Unauthorized();

        var attendant = await db.Attendants.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (attendant is null)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (!Enum.TryParse<HumanAction>(request.HumanAction, ignoreCase: true, out var action))
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = new { code = "INVALID_HUMAN_ACTION", message = "Ação inválida." }
            });

        var slug = await AssignTicketEndpoint.ResolveTenantSlugAsync(currentUser, db, ct);
        if (slug is null)
            return Results.UnprocessableEntity(new { success = false, error = new { code = "TENANT_SLUG_NOT_RESOLVED" } });

        var ok = await logger.RecordHumanActionAsync(
            slug, suggestionId, attendant.Id, action, request.FinalMessageText, ct);
        if (!ok)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        // Note (FR-038, SC-007): we DO NOT create a message in the conversation here.
        // The frontend separately posts the approved/edited text via the messages API of Spec 008.
        return Results.NoContent();
    }
}
