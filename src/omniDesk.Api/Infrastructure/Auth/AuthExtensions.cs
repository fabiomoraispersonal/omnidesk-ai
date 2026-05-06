using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using omniDesk.Api.Domain.InviteTokens;
using omniDesk.Api.Domain.PasswordResetTokens;
using omniDesk.Api.Domain.RefreshTokens;
using omniDesk.Api.Domain.TotpRecoveryCodes;
using omniDesk.Api.Domain.Users;
using omniDesk.Api.Infrastructure.Email;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Persistence.Repositories;
using omniDesk.Api.Infrastructure.Security;
using OmniDesk.Api.Infrastructure.Security;

namespace omniDesk.Api.Infrastructure.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Default")
                ?? Environment.GetEnvironmentVariable("DATABASE_URL")
                ?? throw new InvalidOperationException("Database connection string not configured.")));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IInviteTokenRepository, InviteTokenRepository>();
        services.AddPasswordResetTokenRepository();
        services.AddScoped<ITotpRecoveryCodeRepository, TotpRecoveryCodeRepository>();

        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<JwtService>();
        services.AddSingleton<TotpEncryptionService>();
        services.AddSingleton<TotpService>();

        services.AddScoped<IEmailService, SendGridEmailService>();

        services.AddHttpClient<ITurnstileService, TurnstileService>();

        services.AddScoped<ImpersonationAuditFilter>();

        services.AddJwtBearer(configuration);

        return services;
    }

    private static IServiceCollection AddPasswordResetTokenRepository(this IServiceCollection services)
    {
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        return services;
    }

    private static IServiceCollection AddJwtBearer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var publicKeyPem = configuration["JWT_PUBLIC_KEY_PEM"]
            ?? Environment.GetEnvironmentVariable("JWT_PUBLIC_KEY_PEM")
            ?? throw new InvalidOperationException("JWT_PUBLIC_KEY_PEM is not configured.");

        var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var publicKey = new RsaSecurityKey(rsa);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = publicKey,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                };

                options.Events = new JwtBearerEvents
                {
                    OnChallenge = ctx =>
                    {
                        ctx.HandleResponse();
                        ctx.Response.StatusCode = 401;
                        ctx.Response.ContentType = "application/problem+json";
                        return ctx.Response.WriteAsJsonAsync(new
                        {
                            type = "https://omnideskcrm.com.br/errors/unauthorized",
                            title = "Unauthorized",
                            status = 401,
                            detail = "Authentication required.",
                            code = "unauthorized"
                        });
                    },
                    OnForbidden = ctx =>
                    {
                        ctx.Response.StatusCode = 403;
                        ctx.Response.ContentType = "application/problem+json";
                        return ctx.Response.WriteAsJsonAsync(new
                        {
                            type = "https://omnideskcrm.com.br/errors/forbidden",
                            title = "Forbidden",
                            status = 403,
                            detail = "You do not have permission to perform this action.",
                            code = "forbidden"
                        });
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }
}
