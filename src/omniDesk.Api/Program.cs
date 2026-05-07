using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Minio;
using MongoDB.Driver;
using omniDesk.Api.Features.Authorization.Impersonation;
using omniDesk.Api.Features.Authorization.Policies;
using omniDesk.Api.Features.Authorization.UserLifecycle;
using omniDesk.Api.Infrastructure.Auth;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.Authorization;
using omniDesk.Api.Infrastructure.Jobs;
using omniDesk.Api.Infrastructure.Security;
using omniDesk.Api.Infrastructure.Tenants;
using Serilog;
using StackExchange.Redis;
using omniDesk.Api.Features.Admin;
using omniDesk.Api.Features.Auth;
using omniDesk.Api.Features.Me;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.With<ImpersonationAuditEnricher>()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

builder.Services.AddAuthInfrastructure(builder.Configuration);
builder.Services.AddAuthRateLimiting();
builder.Services.AddTenantInfrastructure(builder.Configuration);

// Spec 004 — Authorization framework (Roles e Permissões)
ImpersonationTokenIssuer.ValidateStartupConfig(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ClaimsCache>();
builder.Services.AddScoped<IClaimsTransformation, ClaimsTransformer>();
builder.Services.AddSingleton<IAuthorizationHandler, RoleRequirementHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, ForbidsDuringImpersonationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, DepartmentScopeHandler>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ImpersonationTokenIssuer>();
builder.Services.AddScoped<LastTenantAdminGuard>();
builder.Services.AddScoped<DeactivateUserCommandHandler>();
builder.Services.AddScoped<ReactivateUserCommandHandler>();
AuthorizationPoliciesRegistration.Register(builder.Services);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration["CORS_ALLOWED_ORIGINS"]
            ?? "http://localhost:4200,http://localhost:4201";

        policy.WithOrigins(origins.Split(',', StringSplitOptions.RemoveEmptyEntries))
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
// Spec 004 (R7) — capture authorization denials with PT-BR body and Warning log.
app.UseAuthorizationFailureLogging();
app.UseRateLimiter();

app.UseHangfireDashboard("/hangfire");

RecurringJob.AddOrUpdate<TenantMetricsCollectorJob>(
    "tenant-metrics-collector",
    job => job.RunAsync(CancellationToken.None),
    "*/5 * * * *");

await app.SeedDatabaseAsync();

var api = app.MapGroup("/api");

// Auth endpoints — registered in US1–US6 implementation phases
var auth = api.MapGroup("/auth");
app.MapAuthEndpoints(auth);

var me = api.MapGroup("/me")
            .RequireAuthorization()
            .AddEndpointFilter<ImpersonationAuditFilter>();
app.MapMeEndpoints(me);

var admin = api.MapGroup("/admin")
               .RequireAuthorization(Policies.PainelAdminAccess)
               .AddEndpointFilter<ImpersonationAuditFilter>();
app.MapAdminEndpoints(admin);

// Spec 004 — User lifecycle (deactivate/reactivate) under /api/users
var users = api.MapGroup("/users")
               .RequireAuthorization()
               .AddEndpointFilter<ImpersonationAuditFilter>();
UserLifecycleEndpoints.Map(users);

app.Run();

// Partial class allows endpoint extension methods to be in separate files (Minimal API pattern)
public partial class Program { }
