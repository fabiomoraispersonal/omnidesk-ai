using omniDesk.Api.Features.WhatsApp.Config;
using omniDesk.Api.Features.WhatsApp.Config.Validators;
using Xunit;

namespace omniDesk.Api.Tests.Features.WhatsApp.Config;

/// <summary>
/// Spec 008 T060 — validador FluentValidation do PUT /api/whatsapp/config.
/// </summary>
public class UpdateWhatsAppConfigValidatorTests
{
    private readonly UpdateWhatsAppConfigValidator _sut = new();

    private static string GoodAccessToken => "EAA" + new string('x', 100);
    private const string GoodAppSecret = "abcdef0123456789abcdef0123456789";
    private const string GoodPhoneNumberId = "123456789012345";

    private static UpdateWhatsAppConfigRequest Empty() =>
        new(null, null, null, null, null, null, null);

    [Fact]
    public void Empty_request_is_valid()
    {
        Assert.True(_sut.Validate(Empty()).IsValid);
    }

    [Theory]
    [InlineData("+5511999999999")]
    [InlineData("+12025551234")]
    public void E164_valid_phone_passes(string phone)
    {
        var req = Empty() with { PhoneNumber = phone };
        Assert.True(_sut.Validate(req).IsValid);
    }

    [Theory]
    [InlineData("11999999999")]   // sem +
    [InlineData("+05511999")]      // começa com 0
    [InlineData("+abcdefg")]       // não-numérico
    [InlineData("+1")]             // muito curto
    public void E164_invalid_phone_fails(string phone)
    {
        var req = Empty() with { PhoneNumber = phone };
        var result = _sut.Validate(req);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateWhatsAppConfigRequest.PhoneNumber));
    }

    [Fact]
    public void Access_token_valid_format_passes()
    {
        var req = Empty() with { AccessToken = GoodAccessToken };
        Assert.True(_sut.Validate(req).IsValid);
    }

    [Fact]
    public void Access_token_without_EAA_prefix_fails()
    {
        var req = Empty() with { AccessToken = "XYZ" + new string('x', 100) };
        var result = _sut.Validate(req);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("EAA"));
    }

    [Fact]
    public void Access_token_too_short_fails()
    {
        var req = Empty() with { AccessToken = "EAA" + new string('x', 50) };
        Assert.False(_sut.Validate(req).IsValid);
    }

    [Fact]
    public void Access_token_too_long_fails()
    {
        var req = Empty() with { AccessToken = "EAA" + new string('x', 600) };
        Assert.False(_sut.Validate(req).IsValid);
    }

    [Fact]
    public void Empty_access_token_skipped_validation()
    {
        // String vazia significa "manter o existente" — não deve falhar.
        var req = Empty() with { AccessToken = string.Empty };
        Assert.True(_sut.Validate(req).IsValid);
    }

    [Fact]
    public void App_secret_valid_hex_passes()
    {
        var req = Empty() with { AppSecret = GoodAppSecret };
        Assert.True(_sut.Validate(req).IsValid);
    }

    [Theory]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")] // not hex
    [InlineData("abc")]                              // too short
    public void App_secret_invalid_fails(string secret)
    {
        var req = Empty() with { AppSecret = secret };
        Assert.False(_sut.Validate(req).IsValid);
    }

    [Fact]
    public void App_secret_too_long_fails()
    {
        var req = Empty() with { AppSecret = new string('a', 100) };
        Assert.False(_sut.Validate(req).IsValid);
    }

    [Fact]
    public void Empty_app_secret_skipped_validation()
    {
        var req = Empty() with { AppSecret = string.Empty };
        Assert.True(_sut.Validate(req).IsValid);
    }

    [Fact]
    public void Phone_number_id_digits_only_required()
    {
        var req = Empty() with { PhoneNumberId = "abc123" };
        var result = _sut.Validate(req);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateWhatsAppConfigRequest.PhoneNumberId));
    }

    [Fact]
    public void Phone_number_id_digits_passes()
    {
        var req = Empty() with { PhoneNumberId = GoodPhoneNumberId };
        Assert.True(_sut.Validate(req).IsValid);
    }

    [Fact]
    public void Waba_id_alphanumeric_passes()
    {
        var req = Empty() with { WabaId = "WABA_ID_12345" };
        Assert.True(_sut.Validate(req).IsValid);
    }

    [Fact]
    public void Waba_id_with_special_chars_fails()
    {
        var req = Empty() with { WabaId = "WABA-ID-123" };
        Assert.False(_sut.Validate(req).IsValid);
    }

    [Fact]
    public void Display_name_too_long_fails()
    {
        var req = Empty() with { DisplayName = new string('x', 101) };
        Assert.False(_sut.Validate(req).IsValid);
    }

    [Fact]
    public void Full_valid_request_passes()
    {
        var req = new UpdateWhatsAppConfigRequest(
            PhoneNumber: "+5511999999999",
            DisplayName: "Clínica ABC",
            WabaId: "WABA_ABC_123",
            PhoneNumberId: GoodPhoneNumberId,
            AccessToken: GoodAccessToken,
            AppSecret: GoodAppSecret,
            BusinessHoursEnabled: false);

        Assert.True(_sut.Validate(req).IsValid);
    }
}
