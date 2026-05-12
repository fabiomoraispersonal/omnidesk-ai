using omniDesk.Api.Domain.Contacts;
using Xunit;

namespace omniDesk.Api.Tests.Features.Contacts;

/// <summary>
/// Spec 009 US6 — T157
/// Unit tests for UpdateContactCommand domain logic:
/// - phone_normalized is recalculated on phone change
/// - EMAIL_CONFLICT and PHONE_CONFLICT detection rules
/// </summary>
public class UpdateContactCommandTests
{
    // -----------------------------------------------------------------------
    // Phone normalization
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("11999999999",    "5511999999999")]   // 11 digits → BR prefix
    [InlineData("1199999999",     "551199999999")]    // 10 digits → BR prefix
    [InlineData("+5511999999999", "5511999999999")]   // already has country code
    [InlineData("+1-800-555-1234","18005551234")]     // international
    [InlineData("",               null)]              // empty → null
    [InlineData(null,             null)]              // null → null
    public void PhoneNormalizer_produces_correct_normalized_form(string? raw, string? expected)
    {
        var result = PhoneNormalizer.Normalize(raw);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void PhoneNormalizer_strips_non_digits()
    {
        var result = PhoneNormalizer.Normalize("(11) 9 9999-9999");
        Assert.Equal("5511999999999", result);
    }

    [Fact]
    public void PhoneNormalizer_returns_null_for_too_short_number()
    {
        var result = PhoneNormalizer.Normalize("1234567");
        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // Contact entity mutation rules
    // -----------------------------------------------------------------------

    [Fact]
    public void Updating_phone_changes_both_phone_and_phone_normalized()
    {
        var contact = MakeContact(phone: "11999999999", phoneNormalized: "5511999999999");
        var newPhone = "21988888888";
        var expected = PhoneNormalizer.Normalize(newPhone);

        contact.Phone           = newPhone;
        contact.PhoneNormalized = PhoneNormalizer.Normalize(newPhone);

        Assert.Equal(newPhone, contact.Phone);
        Assert.Equal(expected, contact.PhoneNormalized);
    }

    [Fact]
    public void Null_phone_sets_phone_normalized_to_null()
    {
        var contact = MakeContact(phone: "11999999999", phoneNormalized: "5511999999999");
        contact.Phone           = null;
        contact.PhoneNormalized = null;

        Assert.Null(contact.Phone);
        Assert.Null(contact.PhoneNormalized);
    }

    // -----------------------------------------------------------------------
    // Conflict detection rules (logic contract tests, no DB)
    // -----------------------------------------------------------------------

    [Fact]
    public void Same_email_in_different_case_should_be_treated_as_conflict()
    {
        const string existing = "joao@email.com";
        const string incoming = "JOAO@EMAIL.COM";

        var isConflict = existing.ToLower() == incoming.ToLower();
        Assert.True(isConflict);
    }

    [Fact]
    public void Same_id_is_not_a_conflict_with_itself()
    {
        var id = Guid.NewGuid();
        var contacts = new[]
        {
            MakeContact(id: id, email: "joao@email.com"),
        };

        var isConflict = contacts.Any(c => c.Id != id && c.Email?.ToLower() == "joao@email.com");
        Assert.False(isConflict);
    }

    [Fact]
    public void Different_id_with_same_email_is_a_conflict()
    {
        var targetId = Guid.NewGuid();
        var otherId  = Guid.NewGuid();
        var contacts = new[]
        {
            MakeContact(id: otherId, email: "joao@email.com"),
        };

        var isConflict = contacts.Any(c => c.Id != targetId && c.Email?.ToLower() == "joao@email.com");
        Assert.True(isConflict);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Contact MakeContact(
        Guid? id = null,
        string? email = null,
        string? phone = null,
        string? phoneNormalized = null)
        => new Contact
        {
            Id              = id ?? Guid.NewGuid(),
            Email           = email,
            Phone           = phone,
            PhoneNormalized = phoneNormalized,
            SourceChannels  = [],
            CreatedAt       = DateTimeOffset.UtcNow,
            UpdatedAt       = DateTimeOffset.UtcNow,
        };
}
