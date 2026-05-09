using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StackExchange.Redis;

namespace omniDesk.Api.Features.AgentRuntime;

/// <summary>
/// DEV-only fault injector for QS-7. Stores a fault counter in Redis under
/// `__faultinj:openai_status` that <see cref="omniDesk.Api.Infrastructure.OpenAi.AssistantsApi"/>
/// can read at request time. Mounted only in Development env.
/// </summary>
public static class InternalFaultInjector
{
    public const string CounterKey = "__faultinj:openai_status";

    public static RouteGroupBuilder MapFaultInjector(this RouteGroupBuilder group)
    {
        group.MapPost("/fault-injector", async (FaultInjectionRequest req, IConnectionMultiplexer redis, CancellationToken ct) =>
        {
            var db = redis.GetDatabase();
            await db.StringSetAsync($"{CounterKey}:status", req.OpenAiStatusCode);
            await db.StringSetAsync($"{CounterKey}:count", req.Count);
            return Results.Ok(new { success = true, configured = req });
        });

        group.MapDelete("/fault-injector", async (IConnectionMultiplexer redis, CancellationToken ct) =>
        {
            var db = redis.GetDatabase();
            await db.KeyDeleteAsync($"{CounterKey}:status");
            await db.KeyDeleteAsync($"{CounterKey}:count");
            return Results.NoContent();
        });

        return group;
    }

    public record FaultInjectionRequest(int OpenAiStatusCode, int Count);
}
