using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace omniDesk.Api.Infrastructure.Security;

public class PasswordHasher
{
    private const int MemorySize = 65536;
    private const int Iterations = 3;
    private const int Parallelism = 1;
    private const int SaltLength = 16;
    private const int HashLength = 32;

    public async Task<string> HashAsync(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = await ComputeHashAsync(Encoding.UTF8.GetBytes(password), salt);
        return $"$argon2id$v=19$m={MemorySize},t={Iterations},p={Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public async Task<bool> VerifyAsync(string password, string storedHash)
    {
        var parts = storedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5 || parts[0] != "argon2id") return false;

        var salt = Convert.FromBase64String(parts[3]);
        var expectedHash = Convert.FromBase64String(parts[4]);
        var actualHash = await ComputeHashAsync(Encoding.UTF8.GetBytes(password), salt);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static Task<byte[]> ComputeHashAsync(byte[] password, byte[] salt)
    {
        return Task.Run(() =>
        {
            using var argon2 = new Argon2id(password)
            {
                Salt = salt,
                MemorySize = MemorySize,
                Iterations = Iterations,
                DegreeOfParallelism = Parallelism
            };
            return argon2.GetBytes(HashLength);
        });
    }
}
