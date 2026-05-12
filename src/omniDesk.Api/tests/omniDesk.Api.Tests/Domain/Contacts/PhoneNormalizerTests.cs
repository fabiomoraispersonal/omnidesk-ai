using omniDesk.Api.Domain.Contacts;
using Xunit;

namespace omniDesk.Api.Tests.Domain.Contacts;

/// <summary>
/// Spec 009 — R5: Brazilian phone normalization for contact deduplication.
///
/// Rules (from PhoneNormalizer.cs):
///   - Strip all non-digit characters.
///   - Fewer than 8 digits → null.
///   - 10 or 11 digits → prefix "55" (assume BR).
///   - Starts with "55" and has 12–13 digits → valid BR, return as-is.
///   - Everything else (international) → return stripped digits if ≥8.
/// </summary>
public class PhoneNormalizerTests
{
    // ------------------------------------------------------------------ //
    // Null / empty inputs
    // ------------------------------------------------------------------ //

    [Fact]
    public void Normalize_null_returns_null()
    {
        Assert.Null(PhoneNormalizer.Normalize(null));
    }

    [Fact]
    public void Normalize_empty_string_returns_null()
    {
        Assert.Null(PhoneNormalizer.Normalize(""));
    }

    [Fact]
    public void Normalize_whitespace_only_returns_null()
    {
        Assert.Null(PhoneNormalizer.Normalize("   "));
    }

    // ------------------------------------------------------------------ //
    // Too short
    // ------------------------------------------------------------------ //

    [Fact]
    public void Normalize_too_short_returns_null()
    {
        // "123" → 3 digits, below the 8-digit minimum
        Assert.Null(PhoneNormalizer.Normalize("123"));
    }

    [Fact]
    public void Normalize_seven_digits_returns_null()
    {
        // Exactly one below the minimum (< 8)
        Assert.Null(PhoneNormalizer.Normalize("1234567"));
    }

    // ------------------------------------------------------------------ //
    // Brazilian mobile (11 digits — DDD + 9-digit number)
    // ------------------------------------------------------------------ //

    [Fact]
    public void Normalize_br_mobile_formatted_adds_country_code()
    {
        // "(11) 99999-9999" → 11 digits → "55" + 11 digits = 13 digits
        var result = PhoneNormalizer.Normalize("(11) 99999-9999");
        Assert.Equal("5511999999999", result);
    }

    [Fact]
    public void Normalize_br_mobile_unformatted_adds_country_code()
    {
        // "11999999999" → 11 digits → prefix "55"
        var result = PhoneNormalizer.Normalize("11999999999");
        Assert.Equal("5511999999999", result);
    }

    [Fact]
    public void Normalize_br_mobile_with_international_prefix_is_normalised()
    {
        // "+55 11 99999-9999" → strip non-digits → "5511999999999" (13 digits, starts with 55)
        var result = PhoneNormalizer.Normalize("+55 11 99999-9999");
        Assert.Equal("5511999999999", result);
    }

    // ------------------------------------------------------------------ //
    // Brazilian landline (10 digits — DDD + 8-digit number)
    // ------------------------------------------------------------------ //

    [Fact]
    public void Normalize_br_landline_formatted_adds_country_code()
    {
        // "(11) 3333-4444" → 10 digits → "55" + 10 digits = 12 digits
        var result = PhoneNormalizer.Normalize("(11) 3333-4444");
        Assert.Equal("551133334444", result);
    }

    [Fact]
    public void Normalize_br_landline_unformatted_adds_country_code()
    {
        var result = PhoneNormalizer.Normalize("1133334444");
        Assert.Equal("551133334444", result);
    }

    // ------------------------------------------------------------------ //
    // International (not Brazilian)
    // ------------------------------------------------------------------ //

    [Fact]
    public void Normalize_us_number_returns_stripped_digits()
    {
        // "+1 212 555 0100" → digits "12125550100" (11 digits, does NOT start with 55)
        // Falls through to the "international" branch — returned as stripped digits.
        // Note: 11 digits without a "55" prefix → the implementation prefixes "55",
        // making it "5512125550100" (13 digits starting with 55).
        // This is an acknowledged heuristic trade-off (R5 in research.md):
        // non-BR numbers with exactly 10-11 digits will be incorrectly prefixed.
        // We test the actual behaviour to prevent silent regressions.
        var result = PhoneNormalizer.Normalize("+1 212 555 0100");

        // 12125550100 is 11 digits → code adds "55" prefix → "5512125550100"
        Assert.Equal("5512125550100", result);
    }

    [Fact]
    public void Normalize_international_e164_with_non_br_country_returns_digits()
    {
        // "+44 20 7946 0958" → digits "442079460958" (12 digits, does NOT start with 55)
        // Falls into the ≥8 digit catch-all branch — returned as-is digits.
        var result = PhoneNormalizer.Normalize("+44 20 7946 0958");
        Assert.Equal("442079460958", result);
    }

    [Fact]
    public void Normalize_8_digit_number_is_returned_as_is()
    {
        // Exactly 8 digits → returned without modification (not 10 or 11, not starting with 55)
        var result = PhoneNormalizer.Normalize("12345678");
        Assert.Equal("12345678", result);
    }

    // ------------------------------------------------------------------ //
    // Already-normalised values round-trip cleanly
    // ------------------------------------------------------------------ //

    [Fact]
    public void Normalize_already_normalised_br_mobile_is_idempotent()
    {
        // "5511999999999" → 13 digits, starts with "55" → returned unchanged
        var result = PhoneNormalizer.Normalize("5511999999999");
        Assert.Equal("5511999999999", result);
    }

    [Fact]
    public void Normalize_already_normalised_br_landline_is_idempotent()
    {
        // "551133334444" → 12 digits, starts with "55" → returned unchanged
        var result = PhoneNormalizer.Normalize("551133334444");
        Assert.Equal("551133334444", result);
    }

    // ------------------------------------------------------------------ //
    // Punctuation / formatting variations
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData("(11) 9 9999-9999")]   // space before 9th digit variant
    [InlineData("+55(11)999999999")]   // compact international
    [InlineData("55 11 999999999")]    // spaced
    public void Normalize_various_br_formats_produce_consistent_result(string input)
    {
        var result = PhoneNormalizer.Normalize(input);
        // All of the above normalise to exactly 13 digits starting with 55
        Assert.NotNull(result);
        Assert.Equal(13, result!.Length);
        Assert.StartsWith("55", result);
    }
}
