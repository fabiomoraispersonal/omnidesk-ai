using System.Security.Cryptography;
using OtpNet;

namespace omniDesk.Api.Infrastructure.Security;

public sealed class TotpService
{
    private const string UnambiguousChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public string GenerateSecret()
    {
        var secretBytes = new byte[20];
        RandomNumberGenerator.Fill(secretBytes);
        return Base32Encoding.ToString(secretBytes);
    }

    public string GenerateQrCodeUri(string email, string secret, string issuer = "OmniDesk")
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedEmail = Uri.EscapeDataString(email);
        return $"otpauth://totp/{encodedIssuer}:{encodedEmail}?secret={secret}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
    }

    public bool ValidateCode(string secret, string code)
    {
        try
        {
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes);
            return totp.VerifyTotp(code, out _, new VerificationWindow(1, 1));
        }
        catch
        {
            return false;
        }
    }

    public string[] GenerateRecoveryCodes(int count = 8)
    {
        var codes = new string[count];
        for (var i = 0; i < count; i++)
        {
            var bytes = new byte[8];
            RandomNumberGenerator.Fill(bytes);
            codes[i] = new string(bytes.Select(b => UnambiguousChars[b % UnambiguousChars.Length]).ToArray());
        }
        return codes;
    }
}
