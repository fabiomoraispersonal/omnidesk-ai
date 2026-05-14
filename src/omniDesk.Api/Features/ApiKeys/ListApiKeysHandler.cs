using omniDesk.Api.Infrastructure.Audit;

namespace omniDesk.Api.Features.ApiKeys;

public class ListApiKeysHandler(ApiKeyRepository repo)
{
    public async Task<IReadOnlyList<ApiKeyResponse>> ExecuteAsync(Guid tenantId, CancellationToken ct)
    {
        var keys = await repo.ListActiveAsync(tenantId, ct);
        return keys.Select(k => new ApiKeyResponse(
            k.Id, k.Name, k.Scopes, k.LastUsedAt, k.ExpiresAt, k.Revoked, k.CreatedAt))
            .ToList();
    }
}
