using StackExchange.Redis;

namespace omniDesk.Api.Infrastructure.Security;

public class SessionInvalidationService(IConnectionMultiplexer redis)
{
    public async Task InvalidateAllTenantSessionsAsync(string slug, CancellationToken ct = default)
    {
        var pattern = $"{slug}:session:*";
        var servers = redis.GetServers();

        foreach (var server in servers.Where(s => s.IsConnected))
        {
            var keys = server.Keys(pattern: pattern).ToArray();
            if (keys.Length > 0)
                await redis.GetDatabase().KeyDeleteAsync(keys);
        }
    }
}
