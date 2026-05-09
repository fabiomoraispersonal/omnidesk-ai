using System.Globalization;
using System.Text;

namespace omniDesk.Api.Domain.AiAgents;

public static class HandoffKeywords
{
    public static readonly IReadOnlyList<string> PtBr = new[]
    {
        "atendente",
        "humano",
        "gerente",
        "responsavel",
        "quero falar com alguem",
        "falar com alguem",
        "atendimento humano",
    };

    public static bool Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = Normalize(text);
        foreach (var keyword in PtBr)
        {
            if (normalized.Contains(keyword, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string Normalize(string input)
    {
        var stripped = new StringBuilder(input.Length);
        var formD = input.Normalize(NormalizationForm.FormD);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                stripped.Append(ch);
        }
        return stripped.ToString().ToLowerInvariant();
    }
}
