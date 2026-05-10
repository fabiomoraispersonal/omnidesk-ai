using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.LiveChat.Config;
using omniDesk.Api.Features.LiveChat.Config.Validators;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Config;

/// <summary>
/// Spec 007 T097 — UpdateWidgetConfigValidator covers contracts/widget-config-api.md
/// validation rules. This test runs without infrastructure (pure FluentValidation).
/// </summary>
public class UpdateWidgetConfigValidatorTests
{
    private readonly UpdateWidgetConfigValidator _validator = new();

    [Fact]
    public void Accepts_valid_payload()
    {
        var result = _validator.Validate(Valid());
        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData("not-a-color")]
    [InlineData("#ABC")]
    [InlineData("#GGGGGG")]
    public void Rejects_invalid_primary_color(string color)
    {
        var result = _validator.Validate(Valid() with { PrimaryColor = color });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateWidgetConfigRequest.PrimaryColor));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(169)]
    public void Rejects_out_of_range_abandonment_hours(int hours)
    {
        var result = _validator.Validate(Valid() with { AbandonmentTimeoutHours = hours });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateWidgetConfigRequest.AbandonmentTimeoutHours));
    }

    [Fact]
    public void Rejects_duplicate_identification_fields()
    {
        var dup = new[]
        {
            new IdentificationField("name", "Nome", true),
            new IdentificationField("name", "Outro", false),
        };
        var result = _validator.Validate(Valid() with { IdentificationFields = dup });
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("duplicates", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_unknown_identification_field_key()
    {
        var bogus = new[] { new IdentificationField("nickname", "Apelido", false) };
        var result = _validator.Validate(Valid() with { IdentificationFields = bogus });
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("name, email, phone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_invalid_privacy_url()
    {
        var result = _validator.Validate(Valid() with { PrivacyPolicyUrl = "not-a-url" });
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateWidgetConfigRequest.PrivacyPolicyUrl));
    }

    private static UpdateWidgetConfigRequest Valid() => new(
        PrimaryColor: "#7A9E7E",
        LauncherIcon: LauncherIcon.Support,
        CompanyName: "Clínica Teste",
        WelcomeMessage: "Olá!",
        InputPlaceholder: "Digite aqui",
        Position: WidgetPosition.BottomLeft,
        RequireIdentification: true,
        IdentificationFields: new[]
        {
            new IdentificationField("name", "Nome", true),
            new IdentificationField("email", "E-mail", false),
        },
        AllowedDomains: new[] { "www.clinica-teste.com.br" },
        PrivacyPolicyText: "Política…",
        PrivacyPolicyUrl: "https://www.clinica-teste.com.br/privacidade",
        AbandonmentTimeoutHours: 8,
        InactivityCloseHours: 24);
}
