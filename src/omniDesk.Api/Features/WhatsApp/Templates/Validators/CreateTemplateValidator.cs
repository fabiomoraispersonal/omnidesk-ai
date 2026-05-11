using System.Text.RegularExpressions;
using FluentValidation;
using omniDesk.Api.Domain.WhatsApp;
using omniDesk.Api.Features.WhatsApp.Templates.Requests;

namespace omniDesk.Api.Features.WhatsApp.Templates.Validators;

/// <summary>
/// Spec 008 US5 — valida payload de criação de template.
/// contracts/whatsapp-templates-api.md §2 Validação.
/// </summary>
public class CreateTemplateValidator : AbstractValidator<CreateTemplateRequest>
{
    private static readonly Regex SnakeCaseRx       = new(@"^[a-z0-9_]+$", RegexOptions.Compiled);
    private static readonly Regex PlaceholderRx     = new(@"\{\{(\d+)\}\}", RegexOptions.Compiled);
    private const int MaxBodyLength = 1024;     // Limite Meta
    private const int MaxLabelLength = 60;

    public CreateTemplateValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(BeValidType)
            .WithMessage("Tipo inválido. Use: appointment_reminder, appointment_confirmation, appointment_cancellation, follow_up, custom.");

        RuleFor(x => x.BodyTemplate)
            .NotEmpty()
            .MaximumLength(MaxBodyLength);

        RuleFor(x => x.VariableLabels)
            .NotNull();

        RuleForEach(x => x.VariableLabels)
            .NotEmpty()
            .MaximumLength(MaxLabelLength);

        // Para tipos pré-definidos: VariableCount fixo por tipo + placeholders no body
        // devem casar com VariableLabels.Count e ser sequenciais.
        // Para Custom: variável count livre, mas placeholders ainda devem ser sequenciais.
        RuleFor(x => x)
            .Must(HaveSequentialPlaceholdersMatchingLabels)
            .WithMessage(
                "Placeholders {{N}} no body devem ser numerados sequencialmente de 1 e " +
                "casar com a quantidade de variable_labels.");

        // Tipos pré-definidos: VariableCount deve casar com PredefinedTemplates spec.
        When(x => BeValidType(x.Type) && TryParseType(x.Type, out var t) && t != TemplateType.Custom, () =>
        {
            RuleFor(x => x.VariableLabels)
                .Must((req, labels) =>
                {
                    var t = ParseType(req.Type);
                    var expected = PredefinedTemplates.For(t).VariableCount;
                    return labels.Count == expected;
                })
                .WithMessage(req =>
                {
                    var t = ParseType(req.Type);
                    var expected = PredefinedTemplates.For(t).VariableCount;
                    return $"Tipo {req.Type} exige exatamente {expected} variáveis.";
                });
        });

        // Custom: name_suffix obrigatório e válido.
        When(x => string.Equals(x.Type, "custom", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.NameSuffix)
                .NotEmpty()
                .MinimumLength(1).MaximumLength(40)
                .Must(v => SnakeCaseRx.IsMatch(v!))
                .WithMessage("name_suffix deve ser snake_case (a-z, 0-9, _) com 1-40 caracteres.");
        });
    }

    private static bool BeValidType(string? type) =>
        !string.IsNullOrEmpty(type)
        && (type == "appointment_reminder"
         || type == "appointment_confirmation"
         || type == "appointment_cancellation"
         || type == "follow_up"
         || type == "custom");

    private static bool TryParseType(string type, out TemplateType result)
    {
        try { result = TemplateTypeExtensions.ParseWire(type); return true; }
        catch { result = default; return false; }
    }

    private static TemplateType ParseType(string type) => TemplateTypeExtensions.ParseWire(type);

    private static bool HaveSequentialPlaceholdersMatchingLabels(CreateTemplateRequest req)
    {
        if (req.VariableLabels is null) return false;

        var matches = PlaceholderRx.Matches(req.BodyTemplate);
        var distinctIndexes = matches
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        if (distinctIndexes.Count != req.VariableLabels.Count) return false;

        // Sequenciais começando em 1: 1,2,3,...N
        for (var i = 0; i < distinctIndexes.Count; i++)
        {
            if (distinctIndexes[i] != i + 1) return false;
        }
        return true;
    }
}
