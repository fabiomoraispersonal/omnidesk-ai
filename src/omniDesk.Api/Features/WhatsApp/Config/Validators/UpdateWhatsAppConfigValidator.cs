using System.Text.RegularExpressions;
using FluentValidation;

namespace omniDesk.Api.Features.WhatsApp.Config.Validators;

/// <summary>
/// Spec 008 — body validator para PUT /api/whatsapp/config. Regras de
/// contracts/whatsapp-config-api.md §2: E.164 phone, ranges de comprimento,
/// access_token prefix EAA, app_secret 32-64 hex chars.
///
/// Strings vazias OU null em <c>AccessToken</c>/<c>AppSecret</c> significam "manter
/// o existente" — não devem ser validadas. Apenas valores não-empty disparam validação.
/// </summary>
public class UpdateWhatsAppConfigValidator : AbstractValidator<UpdateWhatsAppConfigRequest>
{
    private static readonly Regex E164Rx        = new(@"^\+[1-9]\d{6,18}$", RegexOptions.Compiled);
    private static readonly Regex HexRx         = new(@"^[0-9a-fA-F]+$",     RegexOptions.Compiled);
    private static readonly Regex AlphaNumUnder = new(@"^[A-Za-z0-9_]+$",    RegexOptions.Compiled);
    private static readonly Regex DigitsOnly    = new(@"^\d+$",              RegexOptions.Compiled);

    public UpdateWhatsAppConfigValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .Must(v => E164Rx.IsMatch(v!))
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
            .WithMessage("phone_number deve estar em formato E.164 (ex: +5511999999999).");

        RuleFor(x => x.DisplayName)
            .MinimumLength(1).MaximumLength(100)
            .When(x => x.DisplayName is not null);

        RuleFor(x => x.WabaId)
            .Must(v => AlphaNumUnder.IsMatch(v!))
            .When(x => !string.IsNullOrWhiteSpace(x.WabaId))
            .WithMessage("waba_id deve ser alfanumérico (com underscore opcional).");

        RuleFor(x => x.WabaId)
            .MinimumLength(1).MaximumLength(100)
            .When(x => x.WabaId is not null);

        RuleFor(x => x.PhoneNumberId)
            .Must(v => DigitsOnly.IsMatch(v!))
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumberId))
            .WithMessage("phone_number_id deve conter apenas dígitos (formato Meta).");

        RuleFor(x => x.PhoneNumberId)
            .MinimumLength(1).MaximumLength(100)
            .When(x => x.PhoneNumberId is not null);

        RuleFor(x => x.AccessToken)
            .Must(v => v!.StartsWith("EAA", StringComparison.Ordinal))
            .When(x => !string.IsNullOrEmpty(x.AccessToken))
            .WithMessage("access_token deve começar com 'EAA' (formato Meta).");

        RuleFor(x => x.AccessToken)
            .Must(v => v!.Length is >= 100 and <= 500)
            .When(x => !string.IsNullOrEmpty(x.AccessToken))
            .WithMessage("access_token deve ter entre 100 e 500 caracteres.");

        RuleFor(x => x.AppSecret)
            .Must(v => HexRx.IsMatch(v!))
            .When(x => !string.IsNullOrEmpty(x.AppSecret))
            .WithMessage("app_secret deve conter apenas dígitos hexadecimais.");

        RuleFor(x => x.AppSecret)
            .Must(v => v!.Length is >= 32 and <= 64)
            .When(x => !string.IsNullOrEmpty(x.AppSecret))
            .WithMessage("app_secret deve ter entre 32 e 64 caracteres.");
    }
}
