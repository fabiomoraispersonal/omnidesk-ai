using System.Text.RegularExpressions;
using FluentValidation;
using omniDesk.Api.Features.WhatsApp.Templates.Requests;

namespace omniDesk.Api.Features.WhatsApp.Templates.Validators;

/// <summary>
/// Spec 008 US5 — valida update de template existente (apenas status=draft).
/// Tipo é imutável após criação — só body e variable_labels podem mudar.
/// Validação de placeholders sequenciais idêntica à criação.
/// </summary>
public class UpdateTemplateValidator : AbstractValidator<UpdateTemplateRequest>
{
    private static readonly Regex PlaceholderRx = new(@"\{\{(\d+)\}\}", RegexOptions.Compiled);
    private const int MaxBodyLength = 1024;
    private const int MaxLabelLength = 60;

    public UpdateTemplateValidator()
    {
        RuleFor(x => x.BodyTemplate)
            .NotEmpty()
            .MaximumLength(MaxBodyLength);

        RuleFor(x => x.VariableLabels)
            .NotNull();

        RuleForEach(x => x.VariableLabels)
            .NotEmpty()
            .MaximumLength(MaxLabelLength);

        RuleFor(x => x)
            .Must(HaveSequentialPlaceholders)
            .WithMessage(
                "Placeholders {{N}} no body devem ser numerados sequencialmente de 1 e " +
                "casar com a quantidade de variable_labels.");
    }

    private static bool HaveSequentialPlaceholders(UpdateTemplateRequest req)
    {
        if (req.VariableLabels is null) return false;

        var distinctIndexes = PlaceholderRx.Matches(req.BodyTemplate)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        if (distinctIndexes.Count != req.VariableLabels.Count) return false;

        for (var i = 0; i < distinctIndexes.Count; i++)
            if (distinctIndexes[i] != i + 1) return false;

        return true;
    }
}
