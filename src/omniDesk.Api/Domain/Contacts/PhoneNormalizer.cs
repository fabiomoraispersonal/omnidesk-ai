using System.Text.RegularExpressions;

namespace omniDesk.Api.Domain.Contacts;

// Brazilian phone normalization heuristic (R5 from research.md).
// Strips non-digits, validates minimum length, prefixes +55 for 10-11 digit BR numbers.
public static class PhoneNormalizer
{
    private static readonly Regex NonDigits = new(@"\D", RegexOptions.Compiled);

    public static string? Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        // Explicit international prefix: skip BR heuristic, return digits as-is.
        var hasExplicitIntl = phone.TrimStart().StartsWith('+');

        var digits = NonDigits.Replace(phone, "");

        if (digits.Length < 8)
            return null;

        if (!hasExplicitIntl)
        {
            // 10-11 digits without country code → assume BR (+55)
            if (digits.Length is 10 or 11)
                digits = "55" + digits;

            // Already starts with 55 and has 12-13 digits → valid BR
            if (digits.StartsWith("55") && digits.Length is 12 or 13)
                return digits;
        }

        // International or unrecognised: return as-is if at least 8 digits
        return digits.Length >= 8 ? digits : null;
    }
}
