using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Queues;

namespace omniDesk.Api.Features.AgentRuntime;

/// <summary>
/// DEV-only shortcut for QS-2: enqueues a synthetic incoming message without depending on Spec 007.
/// Mounted at /api/internal/test-incoming when env == Development.
/// </summary>
public static class InternalTestEndpoint
{
    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        group.MapPost("/test-incoming", async (
            TestIncomingRequest req,
            AppDbContext db,
            IncomingMessagePublisher publisher,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var slug = httpContext.Request.Headers["X-Tenant-Slug"].ToString();
            if (string.IsNullOrEmpty(slug)) return Results.BadRequest(new { error = "X-Tenant-Slug header required" });

            var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug, ct);
            if (tenant is null) return Results.NotFound(new { error = "tenant not found" });

            var msg = new IncomingMessage(
                tenant.Id,
                tenant.Slug,
                req.ExternalRef,
                Guid.NewGuid().ToString("n"),
                req.Content,
                DateTimeOffset.UtcNow);
            var jobId = publisher.Enqueue(msg);
            return Results.Accepted("/test-incoming", new { jobId, messageId = msg.MessageId });
        });
        return group;
    }

    public record TestIncomingRequest(string ExternalRef, string Content);
}
