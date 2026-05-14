using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Infrastructure.Audit;

/// <summary>Spec 012 — CRUD for <see cref="ApiKey"/> in tenant schema.</summary>
public class ApiKeyRepository(AppDbContext db)
{
    public async Task<ApiKey> CreateAsync(ApiKey key, CancellationToken ct)
    {
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);
        return key;
    }

    public Task<List<ApiKey>> ListActiveAsync(Guid tenantId, CancellationToken ct) =>
        db.ApiKeys
            .AsNoTracking()
            .Where(k => k.TenantId == tenantId && !k.Revoked)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

    public Task<ApiKey?> FindByHashAsync(string keyHash, CancellationToken ct) =>
        db.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash && !k.Revoked, ct);

    public async Task<bool> RevokeAsync(Guid id, Guid tenantId, CancellationToken ct)
    {
        var key = await db.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == id && k.TenantId == tenantId && !k.Revoked, ct);
        if (key is null) return false;
        key.Revoked = true;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct) =>
        db.ApiKeys.CountAsync(k => k.TenantId == tenantId && !k.Revoked, ct);

    public async Task UpdateLastUsedAtAsync(string keyHash, CancellationToken ct)
    {
        await db.ApiKeys
            .Where(k => k.KeyHash == keyHash)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, DateTime.UtcNow), ct);
    }

    public static string HashKey(string rawKey)
    {
        var bytes = Encoding.UTF8.GetBytes(rawKey);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static string GenerateRawKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "omni_" + Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
