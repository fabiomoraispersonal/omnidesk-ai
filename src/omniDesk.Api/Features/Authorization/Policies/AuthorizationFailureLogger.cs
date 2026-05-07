using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using omniDesk.Api.Domain.Authorization;

namespace omniDesk.Api.Features.Authorization.Policies;

public static class AuthorizationFailureLoggerExtensions
{
    public static IApplicationBuilder UseAuthorizationFailureLogging(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            await next();

            if (ctx.Response.StatusCode == StatusCodes.Status403Forbidden
                && !ctx.Response.HasStarted)
            {
                var logger = ctx.RequestServices.GetRequiredService<ILogger<AuthorizationFailureMarker>>();
                var env = ctx.RequestServices.GetRequiredService<IHostEnvironment>();
                var user = ctx.User;
                var role = Roles.Normalize(user.FindFirst("role")?.Value);
                var userId = user.FindFirst("sub")?.Value
                    ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var tenantSlug = user.FindFirst("tenant_slug")?.Value;
                logger.LogWarning(
                    "AuthorizationDenied {UserId} {Role} {TenantSlug} {Method} {Path}",
                    userId, role, tenantSlug, ctx.Request.Method, ctx.Request.Path.Value);

                if (!ctx.Response.HasStarted)
                {
                    ctx.Response.ContentType = "application/json";
                    var body = env.IsDevelopment()
                        ? JsonSerializer.Serialize(new
                        {
                            success = false,
                            error = new
                            {
                                code = "FORBIDDEN",
                                message = "Você não tem permissão para executar esta ação.",
                                role,
                            }
                        })
                        : JsonSerializer.Serialize(new
                        {
                            success = false,
                            error = new
                            {
                                code = "FORBIDDEN",
                                message = "Você não tem permissão para executar esta ação."
                            }
                        });
                    await ctx.Response.WriteAsync(body);
                }
            }
        });
    }
}

internal sealed class AuthorizationFailureMarker { }
