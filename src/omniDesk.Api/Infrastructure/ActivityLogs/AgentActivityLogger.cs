using MongoDB.Driver;
using omniDesk.Api.Domain.AiAgents;

namespace omniDesk.Api.Infrastructure.ActivityLogs;

public class AgentActivityLogger
{
    private readonly IMongoClient _mongo;
    private readonly ILogger<AgentActivityLogger> _logger;

    public AgentActivityLogger(IMongoClient mongo, ILogger<AgentActivityLogger> logger)
    {
        _mongo = mongo;
        _logger = logger;
    }

    public async Task LogAsync(AgentActivityLog entry, CancellationToken ct)
    {
        try
        {
            var db = _mongo.GetDatabase($"tenant_{Sanitize(entry.TenantSlug)}");
            var collection = db.GetCollection<AgentActivityLog>("agent_activity_logs");
            await collection.InsertOneAsync(entry, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // Never throw from logging — degrade gracefully.
            _logger.LogError(ex, "Failed to write agent_activity_logs for tenant {Slug} action {Action}.",
                entry.TenantSlug, entry.Action);
        }
    }

    private static string Sanitize(string slug) => slug.Replace('-', '_');
}
