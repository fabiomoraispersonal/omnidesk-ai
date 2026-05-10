using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace omniDesk.Api.Features.LiveChat.Uploads.Validators;

/// <summary>
/// Spec 007 — pre-content-sniff validation: file presence, size cap, conversation id format.
/// MIME validation lives in <see cref="MimeTypeDetector"/> and runs on the actual bytes.
/// </summary>
public class UploadValidator : AbstractValidator<UploadRequest>
{
    public UploadValidator(IConfiguration configuration)
    {
        var maxBytes = configuration.GetValue<long?>("Widget:MaxUploadBytes") ?? 10L * 1024 * 1024;

        RuleFor(x => x.ConversationId)
            .NotEqual(Guid.Empty).WithErrorCode("CONVERSATION_ID_REQUIRED");

        RuleFor(x => x.File)
            .NotNull().WithErrorCode("FILE_REQUIRED")
            .Must(f => f is not null && f.Length > 0).WithErrorCode("EMPTY_FILE")
            .Must(f => f is null || f.Length <= maxBytes).WithErrorCode("FILE_TOO_LARGE")
                .WithMessage($"File exceeds the configured limit of {maxBytes} bytes.");
    }
}

public record UploadRequest(Guid ConversationId, IFormFile File);
