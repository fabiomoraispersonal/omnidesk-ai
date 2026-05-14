using omniDesk.Api.Infrastructure.Audit;

namespace omniDesk.Api.Features.ApiKeys;

public class RevokeApiKeyHandler(ApiKeyRepository repo)
{
    public async Task<bool> ExecuteAsync(Guid id, Guid tenantId, CancellationToken ct)
        => await repo.RevokeAsync(id, tenantId, ct);
}
