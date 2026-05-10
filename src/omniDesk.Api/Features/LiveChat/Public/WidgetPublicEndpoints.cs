using FluentValidation;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Domain.Tenants;
using omniDesk.Api.Features.LiveChat.Public.Commands;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.LiveChat.Public;

/// <summary>
/// Spec 007 — public widget HTTP surface mounted at <c>/api/public/widget</c>.
/// Authenticated by <see cref="WidgetTokenAuthHandler"/>; rate-limited and origin-checked
/// by endpoint filters (<c>/init</c> exempt from rate limit per contract).
/// </summary>
public static class WidgetPublicEndpoints
{
    public const string GroupPath = "/public/widget";

    public static RouteGroupBuilder MapWidgetPublicEndpoints(this RouteGroupBuilder group)
    {
        group
            .RequireAuthorization(WidgetTokenAuthHandler.SchemeName)
            .AddEndpointFilter<OriginValidator>();

        group.MapGet("/init", GetInitAsync);
        group.MapPost("/conversations", StartConversationAsync)
            .AddEndpointFilter<PublicRateLimiter>();
        group.MapGet("/conversations/{id:guid}/messages", GetMessagesAsync)
            .AddEndpointFilter<PublicRateLimiter>();

        return group;
    }

    private static async Task<IResult> GetInitAsync(
        HttpContext http,
        AppDbContext db,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var (tenantId, slug) = ReadTenant(http);

        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.NomeFantasia, t.RazaoSocial, t.Slug })
            .FirstOrDefaultAsync(ct);
        if (tenant is null) return InvalidWidgetToken();

        var config = await db.WidgetConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
        if (config is null) return WidgetConfigNotFound();

        var anonHeader = http.Request.Headers[PublicRateLimiter.AnonymousIdHeader].ToString();
        Conversation? activeConversation = null;
        if (Guid.TryParse(anonHeader, out var anonymousId))
        {
            activeConversation = await db.Conversations.AsNoTracking()
                .Where(c => c.Channel == ChannelType.LiveChat
                         && c.Status == ConversationStatus.Open
                         && db.Visitors.Any(v => v.Id == c.VisitorId && v.AnonymousId == anonymousId))
                .OrderByDescending(c => c.LastMessageAt)
                .FirstOrDefaultAsync(ct);
        }

        if (!config.IsEnabled)
        {
            return Results.Ok(new
            {
                success = true,
                data = new
                {
                    tenant = new { slug = tenant.Slug, company_name = config.CompanyName },
                    config = new { is_enabled = false },
                    active_conversation = (object?)null,
                    disabled_message = "No momento o atendimento está indisponível.",
                },
            });
        }

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                tenant = new { slug = tenant.Slug, company_name = config.CompanyName },
                config = new
                {
                    is_enabled = config.IsEnabled,
                    primary_color = config.PrimaryColor,
                    launcher_icon = config.LauncherIcon.ToString().ToLowerInvariant(),
                    welcome_message = config.WelcomeMessage,
                    input_placeholder = config.InputPlaceholder,
                    position = config.Position == WidgetPosition.BottomRight ? "bottom_right" : "bottom_left",
                    require_identification = config.RequireIdentification,
                    identification_fields = config.IdentificationFields,
                    privacy_policy_text = config.PrivacyPolicyText,
                    privacy_policy_url = config.PrivacyPolicyUrl,
                },
                active_conversation = activeConversation is null ? null : new
                {
                    id = activeConversation.Id,
                    status = activeConversation.Status.ToString().ToLowerInvariant(),
                    has_attendant = activeConversation.AttendantId.HasValue,
                    lgpd_consent_at = activeConversation.LgpdConsentAt,
                },
            },
        });
    }

    private static async Task<IResult> StartConversationAsync(
        StartConversationRequest request,
        HttpContext http,
        AppDbContext db,
        StartConversationCommand command,
        IValidator<StartConversationRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var lgpd = validation.Errors.FirstOrDefault(e => e.ErrorCode == "LGPD_CONSENT_REQUIRED");
            if (lgpd is not null)
                return Results.Json(Error("LGPD_CONSENT_REQUIRED", "Consent must be granted."), statusCode: 422);

            return Results.Json(new
            {
                success = false,
                error = new
                {
                    code = "VALIDATION_ERROR",
                    message = "Validation failed.",
                    details = validation.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage }),
                },
            }, statusCode: 400);
        }

        var (tenantId, _) = ReadTenant(http);
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return InvalidWidgetToken();

        var ipPartial = ConversationMetadata.PartialIp(http.Connection.RemoteIpAddress);
        var userAgent = http.Request.Headers.UserAgent.ToString();

        var result = await command.ExecuteAsync(tenant, request, userAgent, ipPartial, ct);

        if (result.Outcome == "widget_disabled")
            return Results.Json(Error("WIDGET_DISABLED", "Service unavailable."), statusCode: 503);

        return Results.Json(new
        {
            success = true,
            data = new
            {
                conversation_id = result.ConversationId,
                status = "open",
                ws_url = $"/ws/widget/{result.ConversationId}",
                ws_token = tenant.WidgetToken,
                outcome = result.Outcome,
            },
        }, statusCode: 201);
    }

    private static async Task<IResult> GetMessagesAsync(
        Guid id,
        HttpContext http,
        AppDbContext db,
        IMessageRepository messages,
        CancellationToken ct,
        int limit = 50,
        Guid? before = null)
    {
        if (limit < 1 || limit > 100) limit = 50;

        var conv = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conv is null)
            return Results.Json(Error("CONVERSATION_NOT_FOUND", "Conversation not found."), statusCode: 404);

        // Ownership check: anonymous_id from header must match the visitor that owns the conversation.
        var anonHeader = http.Request.Headers[PublicRateLimiter.AnonymousIdHeader].ToString();
        if (!Guid.TryParse(anonHeader, out var anonymousId))
            return Results.Json(Error("ANONYMOUS_ID_REQUIRED", "X-Anonymous-Id header missing."), statusCode: 400);

        var owns = await db.Visitors.AsNoTracking().AnyAsync(
            v => v.Id == conv.VisitorId && v.AnonymousId == anonymousId, ct);
        if (!owns)
            return Results.Json(Error("FORBIDDEN", "Conversation does not belong to this visitor."), statusCode: 403);

        var rows = await messages.GetByConversationAsync(id, limit, before, ct);
        var hasMore = rows.Count == limit;
        var nextCursor = hasMore ? rows[0].Id : (Guid?)null;

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                messages = rows.Select(m => new
                {
                    id = m.Id,
                    sender_type = m.SenderType.ToWire(),
                    sender_id = m.SenderId,
                    content_type = m.ContentType.ToWire(),
                    content = m.Content,
                    attachment_url = m.AttachmentUrl,
                    attachment_name = m.AttachmentName,
                    attachment_size_bytes = m.AttachmentSizeBytes,
                    created_at = m.CreatedAt,
                }),
                has_more = hasMore,
                next_cursor = nextCursor,
            },
        });
    }

    private static (Guid TenantId, string Slug) ReadTenant(HttpContext http)
    {
        var slug = http.User.FindFirst(WidgetTokenAuthHandler.TenantSlugClaim)!.Value;
        var tenantId = Guid.Parse(http.User.FindFirst(WidgetTokenAuthHandler.TenantIdClaim)!.Value);
        return (tenantId, slug);
    }

    private static IResult InvalidWidgetToken()
        => Results.Json(Error("INVALID_WIDGET_TOKEN", "Widget token did not resolve to an active tenant."), statusCode: 401);

    private static IResult WidgetConfigNotFound()
        => Results.Json(Error("WIDGET_CONFIG_NOT_FOUND", "Widget config not provisioned for this tenant."), statusCode: 404);

    private static object Error(string code, string message)
        => new { success = false, error = new { code, message } };
}
