using FluentValidation;

namespace omniDesk.Api.Features.LiveChat.Public.Validators;

/// <summary>
/// Spec 007 — body validator for POST /api/public/widget/conversations.
/// Enforces LGPD consent, valid anonymous_id, and metadata.page_url shape.
/// </summary>
public class StartConversationValidator : AbstractValidator<StartConversationRequest>
{
    public StartConversationValidator()
    {
        RuleFor(x => x.LgpdConsent)
            .Equal(true).WithErrorCode("LGPD_CONSENT_REQUIRED")
            .WithMessage("Consent must be granted.");

        RuleFor(x => x.AnonymousId)
            .NotEqual(Guid.Empty).WithErrorCode("ANONYMOUS_ID_REQUIRED")
            .WithMessage("anonymous_id is required.");

        When(x => x.Metadata is not null, () =>
        {
            RuleFor(x => x.Metadata!.PageUrl)
                .NotEmpty().WithMessage("metadata.page_url is required.")
                .Must(BeHttpsUrl).WithMessage("metadata.page_url must be an https URL.");
        });

        When(x => x.Identification is not null, () =>
        {
            RuleFor(x => x.Identification!.Email)
                .EmailAddress()
                .When(x => !string.IsNullOrWhiteSpace(x.Identification!.Email))
                .WithMessage("identification.email must be a valid email.");
        });
    }

    private static bool BeHttpsUrl(string? url)
        => !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);
}
