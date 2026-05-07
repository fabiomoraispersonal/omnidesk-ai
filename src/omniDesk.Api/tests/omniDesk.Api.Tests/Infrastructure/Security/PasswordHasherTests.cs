using omniDesk.Api.Infrastructure.Security;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.Security;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public async Task HashAndVerify_RoundTrip_ReturnsTrue()
    {
        var password = "MySecurePass123!";
        var hash = await _hasher.HashAsync(password);
        var result = await _hasher.VerifyAsync(password, hash);
        Assert.True(result);
    }

    [Fact]
    public async Task Verify_WrongPassword_ReturnsFalse()
    {
        var hash = await _hasher.HashAsync("correct-password");
        var result = await _hasher.VerifyAsync("wrong-password", hash);
        Assert.False(result);
    }

    [Fact]
    public async Task Verify_EmptyPassword_HandledGracefully()
    {
        var hash = await _hasher.HashAsync("somepassword");
        var result = await _hasher.VerifyAsync("", hash);
        Assert.False(result);
    }

    [Fact]
    public async Task Hash_SpecialCharacters_Succeeds()
    {
        var password = "P@$$w0rd!#%&*()_+-=[]{}|;':\",./<>?";
        var hash = await _hasher.HashAsync(password);
        var result = await _hasher.VerifyAsync(password, hash);
        Assert.True(result);
    }

    [Fact]
    public async Task Hash_ProducesPhcFormat()
    {
        var hash = await _hasher.HashAsync("testpassword");
        Assert.StartsWith("$argon2id$", hash);
    }

    [Fact]
    public async Task TwoHashes_SamePassword_ProduceDifferentHashes()
    {
        var password = "same-password";
        var hash1 = await _hasher.HashAsync(password);
        var hash2 = await _hasher.HashAsync(password);
        Assert.NotEqual(hash1, hash2);
    }
}
