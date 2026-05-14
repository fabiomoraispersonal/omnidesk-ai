using omniDesk.Api.Domain.Audit;
using omniDesk.Api.Infrastructure.Audit;

namespace omniDesk.Api.Features.ApiKeys;

public class CreateApiKeyHandler(ApiKeyRepository repo)
{
    private const int MaxActiveKeys = 5;
    private static readonly string[] DefaultScopes = ["audit_logs:read"];

    public async Task<(CreatedApiKeyResponse? Key, string? Error)> ExecuteAsync(
        Guid tenantId,
        string name,
        CancellationToken ct)
    {
        var count = await repo.CountActiveAsync(tenantId, ct);
        if (count >= MaxActiveKeys)
            return (null, "API_KEY_LIMIT_REACHED");

        var rawKey = ApiKeyRepository.GenerateRawKey();
        var hash   = ApiKeyRepository.HashKey(rawKey);

        var entity = new ApiKey
        {
            Id        = Guid.NewGuid(),
            TenantId  = tenantId,
            Name      = name.Trim(),
            KeyHash   = hash,
            Scopes    = DefaultScopes,
            Revoked   = false,
            CreatedAt = DateTime.UtcNow,
        };

        await repo.CreateAsync(entity, ct);

        return (new CreatedApiKeyResponse(
            entity.Id,
            entity.Name,
            entity.Scopes,
            entity.LastUsedAt,
            entity.ExpiresAt,
            entity.Revoked,
            entity.CreatedAt,
            rawKey), null);
    }
}
