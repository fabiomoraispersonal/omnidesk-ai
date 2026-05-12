using System.Text.RegularExpressions;

namespace omniDesk.Api.Domain.WhatsApp;

/// <summary>
/// Gera nomes de templates em snake_case combinando tipo + slug do tenant.
/// Convenção Meta: nomes únicos por WABA, lowercase + underscores.
/// </summary>
public static class TemplateNameGenerator
{
    private static readonly Regex InvalidChars = new(@"[^a-z0-9_]", RegexOptions.Compiled);
    private static readonly Regex ConsecutiveUnderscores = new(@"_+", RegexOptions.Compiled);

    /// <param name="type">Tipo do template (define o prefixo semântico).</param>
    /// <param name="slug">Slug do tenant (ex.: <c>clinica-abc</c>).</param>
    /// <param name="customSuffix">Apenas para <see cref="TemplateType.Custom"/>: snake_case 1–40 chars.</param>
    public static string Generate(TemplateType type, string slug, string? customSuffix = null)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug is required.", nameof(slug));

        var slugSnake = Sanitize(slug);

        var prefix = type switch
        {
            TemplateType.AppointmentReminder     => "lembrete_consulta",
            TemplateType.AppointmentConfirmation => "confirmacao_consulta",
            TemplateType.AppointmentCancellation => "cancelamento_consulta",
            TemplateType.FollowUp                => "follow_up",
            TemplateType.Custom                  => "custom",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown template type."),
        };

        if (type == TemplateType.Custom)
        {
            if (string.IsNullOrWhiteSpace(customSuffix))
                throw new ArgumentException("customSuffix is required for Custom type.", nameof(customSuffix));

            var suffixSnake = Sanitize(customSuffix);
            return $"{prefix}_{suffixSnake}_{slugSnake}";
        }

        return $"{prefix}_{slugSnake}";
    }

    private static string Sanitize(string value)
    {
        var lower = value.ToLowerInvariant().Replace('-', '_');
        var cleaned = InvalidChars.Replace(lower, "_");
        cleaned = ConsecutiveUnderscores.Replace(cleaned, "_").Trim('_');
        return cleaned;
    }
}
