using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace omniDesk.Api.Infrastructure.Security;

public static class RateLimitingExtensions
{
    public const string AuthLoginPolicy = "auth_login";
    public const string AuthForgotPasswordPolicy = "auth_forgot_password";

    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;

            options.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.ContentType = "application/problem+json";
                await ctx.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://omnideskcrm.com.br/errors/rate-limit-exceeded",
                    title = "Too Many Requests",
                    status = 429,
                    detail = "Too many attempts. Please wait 15 minutes before trying again.",
                    code = "rate_limit_exceeded"
                }, ct);
            };

            options.AddSlidingWindowLimiter(AuthLoginPolicy, limiterOptions =>
            {
                limiterOptions.Window = TimeSpan.FromMinutes(10);
                limiterOptions.SegmentsPerWindow = 5;
                limiterOptions.PermitLimit = 5;
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit = 0;
            });

            options.AddSlidingWindowLimiter(AuthForgotPasswordPolicy, limiterOptions =>
            {
                limiterOptions.Window = TimeSpan.FromMinutes(10);
                limiterOptions.SegmentsPerWindow = 5;
                limiterOptions.PermitLimit = 5;
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiterOptions.QueueLimit = 0;
            });
        });

        return services;
    }

    public static string ComputeRateLimitKey(HttpContext context, string email)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var raw = $"{ip}:{email.ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
