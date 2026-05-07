using OtpNet;
using omniDesk.Api.Infrastructure.Security;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Security;

public class TotpServiceTests
{
    private readonly TotpService _totpService = new();
    private static readonly HashSet<char> AmbiguousChars = new() { 'I', 'O', '0', '1', 'L' };

    [Fact]
    public void GenerateSecret_ReturnsBase32String()
    {
        var secret = _totpService.GenerateSecret();
        Assert.False(string.IsNullOrEmpty(secret));
        Assert.Matches("^[A-Z2-7]+=*$", secret);
    }

    [Fact]
    public void ValidateCode_ValidCode_ReturnsTrue()
    {
        var secret = _totpService.GenerateSecret();
        var secretBytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(secretBytes);
        var code = totp.ComputeTotp();

        var result = _totpService.ValidateCode(secret, code);

        Assert.True(result);
    }

    [Fact]
    public void ValidateCode_InvalidCode_ReturnsFalse()
    {
        var secret = _totpService.GenerateSecret();
        var result = _totpService.ValidateCode(secret, "000000");
        Assert.False(result);
    }

    [Fact]
    public void GenerateRecoveryCodes_Returns8Codes()
    {
        var codes = _totpService.GenerateRecoveryCodes(8);
        Assert.Equal(8, codes.Length);
    }

    [Fact]
    public void GenerateRecoveryCodes_Each8Chars()
    {
        var codes = _totpService.GenerateRecoveryCodes(8);
        Assert.All(codes, code => Assert.Equal(8, code.Length));
    }

    [Fact]
    public void GenerateRecoveryCodes_NoAmbiguousChars()
    {
        var codes = _totpService.GenerateRecoveryCodes(100);
        var allChars = string.Concat(codes);
        Assert.DoesNotContain(allChars, c => AmbiguousChars.Contains(c));
    }
}
