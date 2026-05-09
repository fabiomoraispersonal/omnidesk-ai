using StackExchange.Redis;

namespace omniDesk.Api.Features.AiAgents.Playground;

public record PlaygroundSession(string SessionId, string OpenAiThreadId, Guid AgentId, DateTimeOffset LastUsedAt);

public class PlaygroundSessionStore
{
    private const string FieldThread = "thread";
    private const string FieldAgent = "agent";
    private const string FieldLastUsed = "last_used";

    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration _config;

    public PlaygroundSessionStore(IConnectionMultiplexer redis, IConfiguration config)
    {
        _redis = redis;
        _config = config;
    }

    private TimeSpan Ttl => TimeSpan.FromSeconds(_config.GetValue<int>("Ai:PlaygroundTtlSeconds", 1800));

    private static string Key(string slug, string sessionId) => $"{slug}:playground:{sessionId}";

    public async Task<PlaygroundSession?> GetAsync(string slug, string sessionId)
    {
        var db = _redis.GetDatabase();
        var key = Key(slug, sessionId);
        var hash = await db.HashGetAllAsync(key);
        if (hash.Length == 0) return null;
        await db.KeyExpireAsync(key, Ttl);
        var dict = hash.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        return new PlaygroundSession(
            sessionId,
            dict.GetValueOrDefault(FieldThread, ""),
            Guid.TryParse(dict.GetValueOrDefault(FieldAgent), out var aid) ? aid : Guid.Empty,
            DateTimeOffset.TryParse(dict.GetValueOrDefault(FieldLastUsed), out var dt) ? dt : DateTimeOffset.UtcNow);
    }

    public async Task<PlaygroundSession> CreateAsync(string slug, Guid agentId, string openAiThreadId)
    {
        var sessionId = Guid.NewGuid().ToString("n");
        var db = _redis.GetDatabase();
        var key = Key(slug, sessionId);
        var now = DateTimeOffset.UtcNow;
        await db.HashSetAsync(key, new HashEntry[]
        {
            new(FieldThread, openAiThreadId),
            new(FieldAgent, agentId.ToString()),
            new(FieldLastUsed, now.ToString("o")),
        });
        await db.KeyExpireAsync(key, Ttl);
        return new PlaygroundSession(sessionId, openAiThreadId, agentId, now);
    }

    public async Task<bool> DeleteAsync(string slug, string sessionId)
    {
        var db = _redis.GetDatabase();
        return await db.KeyDeleteAsync(Key(slug, sessionId));
    }

    public DateTimeOffset Now() => DateTimeOffset.UtcNow;
    public TimeSpan ConfiguredTtl => Ttl;
}
