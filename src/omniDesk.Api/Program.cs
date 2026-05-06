using omniDesk.Api.Infrastructure.Auth;
using omniDesk.Api.Infrastructure.Security;
using Serilog;
using omniDesk.Api.Features.Admin;
using omniDesk.Api.Features.Auth;
using omniDesk.Api.Features.Me;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

builder.Services.AddAuthInfrastructure(builder.Configuration);
builder.Services.AddAuthRateLimiting();

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
app.UseRateLimiter();

var api = app.MapGroup("/api");

// Auth endpoints — registered in US1–US6 implementation phases
var auth = api.MapGroup("/auth");
app.MapAuthEndpoints(auth);

var me = api.MapGroup("/me")
            .RequireAuthorization()
            .AddEndpointFilter<ImpersonationAuditFilter>();
app.MapMeEndpoints(me);

var admin = api.MapGroup("/admin")
               .RequireAuthorization()
               .AddEndpointFilter<ImpersonationAuditFilter>();
app.MapAdminEndpoints(admin);

app.Run();

// Partial class allows endpoint extension methods to be in separate files (Minimal API pattern)
public partial class Program { }
