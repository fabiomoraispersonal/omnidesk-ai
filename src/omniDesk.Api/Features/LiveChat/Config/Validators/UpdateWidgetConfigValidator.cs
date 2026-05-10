using System.Text.RegularExpressions;
using FluentValidation;
using omniDesk.Api.Domain.LiveChat;

namespace omniDesk.Api.Features.LiveChat.Config.Validators;

/// <summary>
/// Spec 007 — body validator for PUT /api/widget/config. Mirrors contracts/widget-config-api.md
/// §Validações: hex color regex, copy length caps, hour bounds, identification fields
/// allowlist + uniqueness, domain length cap.
/// </summary>
public class UpdateWidgetConfigValidator : AbstractValidator<UpdateWidgetConfigRequest>
{
    private static readonly Regex HexColorRx = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private static readonly string[] AllowedFields = ["name", "email", "phone"];

    public UpdateWidgetConfigValidator()
    {
        RuleFor(x => x.PrimaryColor)
            .NotEmpty()
            .Must(v => HexColorRx.IsMatch(v ?? ""))
            .WithMessage("Must match #RRGGBB.");

        RuleFor(x => x.CompanyName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.WelcomeMessage)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(x => x.InputPlaceholder)
            .MaximumLength(150)
            .When(x => x.InputPlaceholder is not null);

        RuleFor(x => x.AbandonmentTimeoutHours)
            .InclusiveBetween(1, 168)
            .WithMessage("Must be between 1 and 168.");

        RuleFor(x => x.InactivityCloseHours)
            .InclusiveBetween(1, 168)
            .WithMessage("Must be between 1 and 168.");

        RuleFor(x => x.PrivacyPolicyUrl)
            .Must(BeValidUrl).When(x => !string.IsNullOrWhiteSpace(x.PrivacyPolicyUrl))
            .WithMessage("Must be a valid http(s) URL.");

        When(x => x.IdentificationFields is not null && x.IdentificationFields.Count > 0, () =>
        {
            RuleFor(x => x.IdentificationFields!)
                .Must(fields => fields.Select(f => f.Field).Distinct().Count() == fields.Count)
                .WithMessage("identification_fields must not contain duplicates.")
                .Must(fields => fields.All(f => AllowedFields.Contains(f.Field, StringComparer.OrdinalIgnoreCase)))
                .WithMessage("identification_fields entries must be one of: name, email, phone.");
        });

        RuleForEach(x => x.AllowedDomains)
            .NotEmpty()
            .MaximumLength(255)
            .When(x => x.AllowedDomains is not null);
    }

    private static bool BeValidUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
}
