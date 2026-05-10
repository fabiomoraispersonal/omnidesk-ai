using FluentValidation;
using Microsoft.AspNetCore.Http;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.LiveChat.Public;
using omniDesk.Api.Features.LiveChat.Uploads.Validators;

namespace omniDesk.Api.Features.LiveChat.Uploads;

/// <summary>
/// Spec 007 US6 — POST /api/public/widget/upload. Multipart form-data; the visitor must
/// own the conversation, the conversation must be open, the file must be ≤ MaxUploadBytes,
/// and the bytes must match an allowlisted MIME (sniffed, not trusted from the client).
/// </summary>
public static class UploadEndpoint
{
    public static RouteGroupBuilder MapWidgetUpload(this RouteGroupBuilder group)
    {
        group.MapPost("/upload", HandleAsync)
            .DisableAntiforgery()
            .AddEndpointFilter<PublicRateLimiter>();
        return group;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        IFormFile file,
        Guid conversationId,
        IConversationRepository conversations,
        IMessageRepository messages,
        IVisitorRepository visitors,
        MimeTypeDetector detector,
        MinioUploader uploader,
        IValidator<UploadRequest> validator,
        CancellationToken ct)
    {
        var request = new UploadRequest(conversationId, file);
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var first = validation.Errors[0];
            var status = first.ErrorCode switch
            {
                "FILE_TOO_LARGE" => 413,
                "FILE_REQUIRED" or "EMPTY_FILE" => 400,
                _ => 422,
            };
            return Results.Json(Error(first.ErrorCode!, first.ErrorMessage), statusCode: status);
        }

        var conv = await conversations.GetByIdAsync(conversationId, ct);
        if (conv is null)
            return Results.Json(Error("CONVERSATION_NOT_FOUND", "Conversation not found."), statusCode: 404);
        if (conv.Status != ConversationStatus.Open)
            return Results.Json(Error("CONVERSATION_CLOSED", "Conversation is no longer open."), statusCode: 409);

        // Ownership check: anonymous_id from header must match the visitor that owns the conversation.
        var anonHeader = http.Request.Headers[PublicRateLimiter.AnonymousIdHeader].ToString();
        if (!Guid.TryParse(anonHeader, out var anonymousId))
            return Results.Json(Error("ANONYMOUS_ID_REQUIRED", "X-Anonymous-Id header missing."), statusCode: 400);
        var visitor = await visitors.GetByAnonymousIdAsync(anonymousId, ct);
        if (visitor is null || visitor.Id != conv.VisitorId)
            return Results.Json(Error("FORBIDDEN", "Conversation does not belong to this visitor."), statusCode: 403);

        // Content-sniff the bytes against the 7-MIME allowlist.
        await using var stream = file.OpenReadStream();
        if (!stream.CanSeek)
        {
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            ms.Position = 0;
            return await ProcessAsync(ms);
        }
        return await ProcessAsync(stream);

        async Task<IResult> ProcessAsync(Stream s)
        {
            var detectedMime = await detector.DetectAsync(s, ct);
            if (detectedMime is null)
                return Results.Json(Error("UNSUPPORTED_MIME_TYPE", "File type not allowed."), statusCode: 415);

            var slug = http.User.FindFirst(WidgetTokenAuthHandler.TenantSlugClaim)!.Value;
            var result = await uploader.UploadAsync(
                slug, conversationId, file.FileName, detectedMime, s, file.Length, ct);

            // Persist a Message row referencing the attachment. The visitor's WS client will
            // pick this up via the standard message.new flow on its next replay.
            var contentType = detectedMime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? MessageContentType.Image
                : MessageContentType.File;
            await messages.CreateAsync(new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                SenderType = MessageSenderType.Visitor,
                ContentType = contentType,
                Content = file.FileName,
                AttachmentUrl = result.PresignedUrl,
                AttachmentName = result.FileName,
                AttachmentSizeBytes = (int)Math.Min(file.Length, int.MaxValue),
                CreatedAt = DateTimeOffset.UtcNow,
            }, ct);

            return Results.Json(new
            {
                success = true,
                data = new
                {
                    attachment_url = result.PresignedUrl,
                    attachment_name = result.FileName,
                    attachment_size_bytes = result.Size,
                    mime = result.Mime,
                    content_type = contentType.ToWire(),
                },
            }, statusCode: 201);
        }
    }

    private static object Error(string code, string message)
        => new { success = false, error = new { code, message } };
}
