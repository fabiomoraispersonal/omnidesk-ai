using System.Security.Cryptography;
using System.Text;
using omniDesk.Api.Features.WhatsApp.Webhook;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.WhatsApp.Webhook;

/// <summary>
/// Spec 008 FR-006 / contracts/whatsapp-webhook.md §4.
/// </summary>
public class MetaWebhookSignatureValidatorTests
{
    private readonly MetaWebhookSignatureValidator _sut = new();

    [Fact]
    public void Valid_signature_returns_true()
    {
        var body = Encoding.UTF8.GetBytes("{\"foo\":\"bar\"}");
        var secret = Encoding.UTF8.GetBytes(WhatsAppTestHelpers.FakeAppSecret);
        var header = WhatsAppTestHelpers.ComputeMetaSignature(body, WhatsAppTestHelpers.FakeAppSecret);

        Assert.True(_sut.Validate(header, body, secret));
    }

    [Fact]
    public void Invalid_signature_returns_false()
    {
        var body = Encoding.UTF8.GetBytes("{\"foo\":\"bar\"}");
        var secret = Encoding.UTF8.GetBytes(WhatsAppTestHelpers.FakeAppSecret);
        var header = "sha256=" + new string('a', 64);

        Assert.False(_sut.Validate(header, body, secret));
    }

    [Fact]
    public void Missing_prefix_returns_false()
    {
        var body = Encoding.UTF8.GetBytes("{\"foo\":\"bar\"}");
        var secret = Encoding.UTF8.GetBytes(WhatsAppTestHelpers.FakeAppSecret);
        var bare   = WhatsAppTestHelpers.ComputeMetaSignature(body, WhatsAppTestHelpers.FakeAppSecret)
                                        .Replace("sha256=", string.Empty);

        Assert.False(_sut.Validate(bare, body, secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Null_or_empty_header_returns_false(string? header)
    {
        var body = Encoding.UTF8.GetBytes("{}");
        var secret = Encoding.UTF8.GetBytes(WhatsAppTestHelpers.FakeAppSecret);

        Assert.False(_sut.Validate(header, body, secret));
    }

    [Fact]
    public void Wrong_length_signature_returns_false()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        var secret = Encoding.UTF8.GetBytes(WhatsAppTestHelpers.FakeAppSecret);
        var header = "sha256=" + new string('a', 32); // half length

        Assert.False(_sut.Validate(header, body, secret));
    }

    [Fact]
    public void Empty_secret_returns_false()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        var header = "sha256=" + new string('0', 64);

        Assert.False(_sut.Validate(header, body, Array.Empty<byte>()));
    }

    [Fact]
    public void Different_body_same_secret_returns_false()
    {
        var body1 = Encoding.UTF8.GetBytes("{\"foo\":\"bar\"}");
        var body2 = Encoding.UTF8.GetBytes("{\"foo\":\"baz\"}");
        var secret = Encoding.UTF8.GetBytes(WhatsAppTestHelpers.FakeAppSecret);
        var header = WhatsAppTestHelpers.ComputeMetaSignature(body1, WhatsAppTestHelpers.FakeAppSecret);

        Assert.False(_sut.Validate(header, body2, secret));
    }

    [Fact]
    public void Header_with_uppercase_hex_validates()
    {
        var body = Encoding.UTF8.GetBytes("{\"foo\":\"bar\"}");
        var secret = Encoding.UTF8.GetBytes(WhatsAppTestHelpers.FakeAppSecret);

        // Compute lowercase, then uppercase the hex part — Meta can send either.
        var lower = WhatsAppTestHelpers.ComputeMetaSignature(body, WhatsAppTestHelpers.FakeAppSecret);
        var upper = "sha256=" + lower["sha256=".Length..].ToUpperInvariant();

        Assert.True(_sut.Validate(upper, body, secret));
    }

    [Fact]
    public void Constant_time_comparison_smoke_test()
    {
        var body = Encoding.UTF8.GetBytes("{}");
        var secret = Encoding.UTF8.GetBytes(WhatsAppTestHelpers.FakeAppSecret);

        // Wrong-by-1-char vs wrong-by-many-chars should both return false.
        // We don't measure timing here (flaky in CI) — just verify behavior is identical.
        var almost = "sha256=" + new string('a', 63) + "b";
        var fully = "sha256=" + new string('a', 64);

        Assert.False(_sut.Validate(almost, body, secret));
        Assert.False(_sut.Validate(fully, body, secret));
    }
}
