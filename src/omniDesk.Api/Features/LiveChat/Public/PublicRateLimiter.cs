using omniDesk.Api.Infrastructure.LiveChat;
using StackExchange.Redis;

namespace omniDesk.Api.Features.LiveChat.Public;

/// <summary>
/// Spec 007 FR-006 — Redis-backed sliding window rate limiter applied to public widget
/// endpoints (excluding <c>/init</c>). Increments <c>{slug}:widget:rate:{anonymous_id}</c>
/// with a 60-second TTL; budget is configurable via <c>Widget:PublicRateLimitPerMinute</c>
/// (default 30).
///
/// Header <c>X-Anonymous-Id</c> identifies the visitor; missing header ⇒ 400.
/// Over budget ⇒ 429 <c>RATE_LIMIT_EXCEEDED</c>.
/// </summary>
public class PublicRateLimiter : IEndpointFilter
{
    public const string AnonymousIdHeader = "X-Anonymous-Id";

    private readonly IConnectionMultiplexer _redis;
    private readonly int _limitPerMinute;

    public PublicRateLimiter(IConnectionMultiplexer redis, IConfiguration configuration)
    {
        _redis = redis;
        _limitPerMinute = configuration.GetValue<int?>("Widget:PublicRateLimitPerMinute") ?? 30;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var slug = http.User.FindFirst(WidgetTokenAuthHandler.TenantSlugClaim)?.Value;
        if (string.IsNullOrWhiteSpace(slug))
            return Results.Json(Error("INVALID_WIDGET_TOKEN", "Tenant context missing."), statusCode: 401);

        var anonymousIdRaw = http.Request.Headers[AnonymousIdHeader].ToString();
        if (!Guid.TryParse(anonymousIdRaw, out var anonymousId))
            return Results.Json(Error("ANONYMOUS_ID_REQUIRED", "X-Anonymous-Id header missing or invalid."), statusCode: 400);

        var key = RedisChannelNames.WidgetRateLimit(slug, anonymousId);
        var db = _redis.GetDatabase();
        var count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(60));

        if (count > _limitPerMinute)
            return Results.Json(Error("RATE_LIMIT_EXCEEDED", $"Limit of {_limitPerMinute} requests/min exceeded."), statusCode: 429);

        return await next(context);
    }

    private static object Error(string code, string message)
        => new { success = false, error = new { code, message } };
}
