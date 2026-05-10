using FluentValidation;
using Hangfire;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Minio;
using MongoDB.Driver;
using omniDesk.Api.Domain.Authorization;
using omniDesk.Api.Features.Authorization.Impersonation;
using omniDesk.Api.Features.Authorization.Authz;
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
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Features.AiAgents;
using omniDesk.Api.Features.AiAgents.Playground;
using omniDesk.Api.Features.AiAgents.Variables;
using omniDesk.Api.Features.AiSettings;
using omniDesk.Api.Features.AiSuggestions;
using omniDesk.Api.Features.Attendants;
using omniDesk.Api.Infrastructure.ActivityLogs;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.AiAgents;
using omniDesk.Api.Infrastructure.OpenAi;
using omniDesk.Api.Infrastructure.Queues;
using omniDesk.Api.Features.Auth;
using omniDesk.Api.Features.CannedResponses;
using omniDesk.Api.Features.Departments;
using omniDesk.Api.Features.Me;
using omniDesk.Api.Features.Distribution;
using omniDesk.Api.Infrastructure.Distribution;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Infrastructure.Presence;
using omniDesk.Api.Infrastructure.WebSockets;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.LiveChat.Adapters;
using omniDesk.Api.Features.LiveChat.Config;
using omniDesk.Api.Features.LiveChat.Inbox;
using omniDesk.Api.Features.LiveChat.Public;
using omniDesk.Api.Features.LiveChat.Uploads;
using omniDesk.Api.Infrastructure.LiveChat;

var builder = WebApplication.CreateBuilder(args);

// Spec 004 (FR-031) — enricher needs IHttpContextAccessor; resolve via DI.
builder.Services.AddSingleton<ImpersonationAuditEnricher>();

builder.Host.UseSerilog((ctx, services, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.With(services.GetRequiredService<ImpersonationAuditEnricher>())
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

// Spec 005 — Departamentos e Atendentes (presença, lock, round-robin, WebSocket)
builder.Services.AddSingleton<PresenceCache>();
builder.Services.AddSingleton<PresenceLogger>();
builder.Services.AddSingleton<TicketLock>();
builder.Services.AddSingleton<RoundRobinCursorRedis>();
builder.Services.AddSingleton<DepartmentEventBus>();
builder.Services.AddSingleton<AttendantHubHandler>();
builder.Services.AddScoped<EligibleAttendantsQuery>();
builder.Services.AddScoped<TicketAssignmentService>();
builder.Services.AddScoped<omniDesk.Api.Features.Distribution.Commands.TransferTicketCommandHandler>();
builder.Services.AddScoped<omniDesk.Api.Features.Attendants.UpdateAttendantStatusService>();
builder.Services.AddValidatorsFromAssemblyContaining<omniDesk.Api.Features.Departments.Validators.CreateDepartmentValidator>();

// Spec 005 / US8 — Sugestão IA
builder.Services.AddHttpClient();
// Spec 006 substitui o FallbackAgentRuntime pela impl real (cross-spec §005-A).
builder.Services.AddScoped<IAgentRuntime, AgentRuntime>();
builder.Services.AddScoped<IOpenAiSuggestionClient, OpenAiSuggestionClient>();
builder.Services.AddSingleton<AiSuggestionLogger>();
builder.Services.AddScoped<SuggestReplyService>();
// Sliding-window rate limit for suggestion calls (FR-040 / contracts/ai-suggestion-api.md).
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("ai-suggestion", httpContext =>
    {
        var key = httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return System.Threading.RateLimiting.RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
            new System.Threading.RateLimiting.SlidingWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            });
    });
});

// Spec 006 — Agentes de IA (orchestrator, sub-agentes, transbordo, playground)
builder.Services.AddDataProtection();
builder.Services.AddScoped<TenantContextHolder>();
builder.Services.AddScoped<ITenantSlugAccessor>(sp => sp.GetRequiredService<TenantContextHolder>());
builder.Services.AddScoped<IAssistantsApi, AssistantsApi>();
// Spec 006 — DEV-only fault injector for QS-7 (Resilience). Production has no IFaultInjector
// registered, so AssistantsApi sees null and skips the check.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<IFaultInjector, RedisFaultInjector>();
}
builder.Services.AddScoped<OpenAiKeyResolver>();
builder.Services.AddScoped<AgentActivityLogger>();
builder.Services.AddScoped<RetryPolicy>();
builder.Services.AddScoped<PromptVariableSubstitutor>();
builder.Services.AddScoped<HandoffKeywordDetector>();
builder.Services.AddScoped<ContextBuilder>();
builder.Services.AddScoped<AgentResolver>();
builder.Services.AddScoped<ToolCallDispatcher>();
builder.Services.AddScoped<AgentOrchestrator>();
// Spec 007 — replaces ChannelStubGateway. ChannelStubGateway remains in code as a
// fallback for tests that don't involve real conversations (per contracts/conversation-gateway-impl.md).
builder.Services.AddScoped<omniDesk.Api.Features.LiveChat.Adapters.LiveChatOutgoingAdapter>();
builder.Services.AddScoped<omniDesk.Api.Features.LiveChat.Adapters.LiveChatIncomingAdapter>();
builder.Services.AddScoped<IConversationGateway, omniDesk.Api.Features.LiveChat.Adapters.LiveChatConversationGateway>();
builder.Services.AddScoped<ITicketCreationGateway, StubTicketCreationGateway>();
builder.Services.AddScoped<IncomingMessagePublisher>();
builder.Services.AddScoped<OutgoingMessagePublisher>();
builder.Services.AddScoped<IncomingMessageWorker>();
builder.Services.AddScoped<OutgoingMessageWorker>();
builder.Services.AddScoped<PlaygroundSessionStore>();
builder.Services.AddScoped<PlaygroundCleanupJob>();
builder.Services.AddValidatorsFromAssemblyContaining<omniDesk.Api.Features.AiAgents.Validators.CreateAiAgentValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<omniDesk.Api.Features.AiSettings.Validators.UpdateAiSettingsValidator>();

// Spec 007 — Live Chat (Widget): repositories, public auth scheme, request filters, WS broker
builder.Services.AddScoped<IWidgetConfigRepository, WidgetConfigRepository>();
builder.Services.AddScoped<IVisitorRepository, VisitorRepository>();
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();
builder.Services.AddScoped<OriginValidator>();
builder.Services.AddScoped<PublicRateLimiter>();
builder.Services.AddScoped<omniDesk.Api.Features.LiveChat.Public.Commands.StartConversationCommand>();
builder.Services.AddValidatorsFromAssemblyContaining<omniDesk.Api.Features.LiveChat.Public.Validators.StartConversationValidator>();
builder.Services.AddSingleton<omniDesk.Api.Hubs.WidgetConnectionRegistry>();
builder.Services.AddSingleton<omniDesk.Api.Hubs.WebSocketBroker>();
builder.Services.AddScoped<omniDesk.Api.Hubs.Handlers.MessageSendHandler>();
builder.Services.AddScoped<omniDesk.Api.Hubs.Handlers.VisitorTypingHandler>();
builder.Services.AddScoped<omniDesk.Api.Hubs.Handlers.MessagesReadHandler>();
builder.Services.AddScoped<omniDesk.Api.Hubs.Handlers.MessagesReplayHandler>();
builder.Services.AddScoped<omniDesk.Api.Hubs.WidgetWebSocketEndpoint>();
builder.Services.AddScoped<omniDesk.Api.Features.LiveChat.Jobs.AbandonmentSweepJob>();
builder.Services.AddScoped<omniDesk.Api.Features.LiveChat.Jobs.InactivitySweepJob>();
builder.Services.AddScoped<omniDesk.Api.Features.LiveChat.Uploads.MimeTypeDetector>();
builder.Services.AddScoped<omniDesk.Api.Features.LiveChat.Uploads.MinioUploader>();
builder.Services.AddValidatorsFromAssemblyContaining<omniDesk.Api.Features.LiveChat.Uploads.Validators.UploadValidator>();
builder.Services.AddScoped<omniDesk.Api.Features.LiveChat.Config.Commands.UpdateWidgetConfigCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.LiveChat.Config.Commands.ToggleWidgetCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.LiveChat.Jobs.WidgetDisableEnforcementJob>();
builder.Services.AddValidatorsFromAssemblyContaining<omniDesk.Api.Features.LiveChat.Config.Validators.UpdateWidgetConfigValidator>();
builder.Services.AddScoped<omniDesk.Api.Features.LiveChat.Inbox.Commands.SendAttendantMessageCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.LiveChat.Inbox.Commands.ResolveConversationCommand>();
builder.Services.AddScoped<omniDesk.Api.Hubs.CrmWebSocketEndpoint>();
builder.Services
    .AddAuthentication()
    .AddScheme<WidgetTokenAuthenticationOptions, WidgetTokenAuthHandler>(
        WidgetTokenAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(WidgetTokenAuthHandler.SchemeName, policy =>
    {
        policy.AddAuthenticationSchemes(WidgetTokenAuthHandler.SchemeName);
        policy.RequireAuthenticatedUser();
    });

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration["Cors:AllowedOrigins"]
            ?? builder.Configuration["CORS_ALLOWED_ORIGINS"]
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

// Spec 005 / US5 — Presence timeout (FR-008/FR-009): online→away aos 15 min, away→offline aos 30 min.
RecurringJob.AddOrUpdate<omniDesk.Api.Features.Distribution.PresenceTimeoutJob>(
    "presence-timeout-job",
    job => job.RunAsync(CancellationToken.None),
    "*/1 * * * *");

// Spec 007 / US5 — Lifecycle sweeps (FR-022/FR-023). Runs hourly per research §R9.
RecurringJob.AddOrUpdate<omniDesk.Api.Features.LiveChat.Jobs.AbandonmentSweepJob>(
    "live-chat-abandonment-sweep",
    job => job.RunAsync(CancellationToken.None),
    "0 * * * *");
RecurringJob.AddOrUpdate<omniDesk.Api.Features.LiveChat.Jobs.InactivitySweepJob>(
    "live-chat-inactivity-sweep",
    job => job.RunAsync(CancellationToken.None),
    "0 * * * *");

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

// Spec 005 — Departments
var departments = api.MapGroup("/departments")
                     .RequireAuthorization()
                     .AddEndpointFilter<ImpersonationAuditFilter>();
DepartmentsEndpoints.Map(departments);

// Spec 005 — Attendants
var attendants = api.MapGroup("/attendants")
                    .RequireAuthorization()
                    .AddEndpointFilter<ImpersonationAuditFilter>();
AttendantsEndpoints.Map(attendants);

// Spec 005 — Tickets (manual pickup, transfer) and internal assignment
var tickets = api.MapGroup("/tickets")
                 .RequireAuthorization()
                 .AddEndpointFilter<ImpersonationAuditFilter>();
PickupTicketEndpoint.Map(tickets);
TransferTicketEndpoint.Map(tickets);

var internalTickets = api.MapGroup("/internal/tickets")
                         .RequireAuthorization()
                         .AddEndpointFilter<ImpersonationAuditFilter>();
AssignTicketEndpoint.Map(internalTickets);

// Spec 005 — Canned responses
var canned = api.MapGroup("/canned-responses")
                .RequireAuthorization()
                .AddEndpointFilter<ImpersonationAuditFilter>();
CannedResponsesEndpoints.Map(canned);

// Spec 006 — Agentes de IA (CRUD + playground)
var agents = api.MapGroup("/agents")
                .RequireAuthorization()
                .AddEndpointFilter<ImpersonationAuditFilter>();
AiAgentsEndpoints.Map(agents);
agents.MapPlayground();

// Spec 006 — AI Settings (Configurações Avançadas)
var aiSettings = api.MapGroup("/ai-settings")
                    .RequireAuthorization()
                    .AddEndpointFilter<ImpersonationAuditFilter>();
AiSettingsEndpoints.Map(aiSettings);

// Spec 006 — Internal endpoints (Development only) — atalho para QS-2/QS-7
if (app.Environment.IsDevelopment())
{
    var internalAi = api.MapGroup("/internal");
    InternalTestEndpoint.Map(internalAi);
    internalAi.MapFaultInjector();
}

// Spec 005 / US8 — Sugestão IA
var conversations = api.MapGroup("/conversations")
                       .RequireAuthorization()
                       .AddEndpointFilter<ImpersonationAuditFilter>();
SuggestReplyEndpoint.Map(conversations);

// Spec 007 US3 — attendant inbox surface (list, history, send, resolve).
conversations.MapInboxConversationEndpoints();

// Spec 007 — Public widget surface (auth via WidgetToken scheme)
var widgetPublic = api.MapGroup("/public/widget");
widgetPublic.MapWidgetPublicEndpoints();
widgetPublic.MapWidgetUpload();

// Spec 007 — CRM admin config surface (JWT auth).
var widgetConfig = api.MapGroup("/widget/config")
    .RequireAuthorization()
    .AddEndpointFilter<ImpersonationAuditFilter>();
widgetConfig.MapWidgetConfigEndpoints();

// Spec 007 — Internal endpoint reserved for the Spec 006 orchestrator.
var widgetInternal = api.MapGroup("/internal/livechat")
    .RequireAuthorization();
widgetInternal.MapInternalEndConversation();

// Spec 005 — WebSocket native handler (research §R4)
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.Map("/ws", async (HttpContext ctx, AttendantHubHandler hub, CancellationToken ct) =>
{
    await hub.HandleAsync(ctx, ct);
}).RequireAuthorization();

// Spec 007 — visitor widget WebSocket. Auth via WidgetToken scheme (?token= query).
app.Map("/ws/widget/{conversation_id:guid}",
    async (HttpContext ctx, Guid conversation_id, omniDesk.Api.Hubs.WidgetWebSocketEndpoint endpoint, CancellationToken ct) =>
    {
        await endpoint.HandleAsync(ctx, conversation_id, ct);
    })
    .RequireAuthorization(omniDesk.Api.Features.LiveChat.Public.WidgetTokenAuthHandler.SchemeName);

// Spec 007 US3 — attendant CRM WebSocket. Standard JWT auth.
app.Map("/ws/crm",
    async (HttpContext ctx, omniDesk.Api.Hubs.CrmWebSocketEndpoint endpoint, CancellationToken ct) =>
    {
        await endpoint.HandleAsync(ctx, ct);
    })
    .RequireAuthorization();

app.Run();

// Partial class allows endpoint extension methods to be in separate files (Minimal API pattern)
public partial class Program { }
