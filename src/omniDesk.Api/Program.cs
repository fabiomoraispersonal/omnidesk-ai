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
using omniDesk.Api.Features.Tickets;
using omniDesk.Api.Features.Tickets.Notes;
using omniDesk.Api.Features.Pipelines;
using omniDesk.Api.Features.Contacts;
using omniDesk.Api.Domain.LiveChat;
using omniDesk.Api.Features.LiveChat.Adapters;
using omniDesk.Api.Features.LiveChat.Config;
using omniDesk.Api.Features.WhatsApp.Config;
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
    // Spec 009 US8 T167 — ticket_notes content is internal; never log the note body.
    .Destructure.ByTransforming<omniDesk.Api.Domain.Tickets.TicketNote>(n => new
    {
        n.Id,
        n.TicketId,
        n.AttendantId,
        n.CreatedAt,
        // content omitted — internal note body must not appear in logs
    })
    // Spec 008 FR-034 — segredos WhatsApp NUNCA em texto plano em logs.
    .Destructure.ByTransforming<omniDesk.Api.Domain.WhatsApp.WhatsAppConfig>(c => new
    {
        c.TenantId,
        c.IsEnabled,
        c.PhoneNumber,
        c.DisplayName,
        c.WabaId,
        c.PhoneNumberId,
        AccessTokenCiphertext = c.HasAccessToken ? "***" : null,
        AppSecretCiphertext   = c.HasAppSecret   ? "***" : null,
        WebhookVerifyTokenSet = !string.IsNullOrEmpty(c.WebhookVerifyToken),
        c.BusinessHoursEnabled,
        c.UpdatedAt,
    })
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
builder.Services.AddScoped<omniDesk.Api.Features.Distribution.AttendantAvailabilityHandler>();
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
builder.Services.AddScoped<ITicketCreationGateway, omniDesk.Api.Features.Tickets.TicketCreationGateway>();
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

// Spec 008 — WhatsApp: repositories
builder.Services.AddScoped<omniDesk.Api.Domain.WhatsApp.IWhatsAppConfigRepository, omniDesk.Api.Infrastructure.WhatsApp.WhatsAppConfigRepository>();
builder.Services.AddScoped<omniDesk.Api.Domain.WhatsApp.IWhatsAppTemplateRepository, omniDesk.Api.Infrastructure.WhatsApp.WhatsAppTemplateRepository>();
builder.Services.AddScoped<omniDesk.Api.Infrastructure.WhatsApp.IWaMessageStatusesRepository, omniDesk.Api.Infrastructure.WhatsApp.WaMessageStatusesRepository>();
builder.Services.AddSingleton<omniDesk.Api.Features.WhatsApp.Webhook.MetaWebhookSignatureValidator>();
builder.Services.AddHttpClient<omniDesk.Api.Infrastructure.WhatsApp.WhatsAppMetaClient>(client =>
{
    var baseUrl = builder.Configuration["WhatsApp:GraphApiBaseUrl"] ?? "https://graph.facebook.com/v19.0";
    if (!baseUrl.EndsWith('/')) baseUrl += "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(omniDesk.Api.Infrastructure.WhatsApp.MetaApi.Defaults.SendTimeoutSeconds);
});

// Spec 008 US1 — webhook + adapters + job
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Webhook.WaWebhookTenantResolver>();
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Webhook.WaWebhookProcessorJob>();
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Adapters.WhatsAppIncomingAdapter>();
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Adapters.WhatsAppOutgoingAdapter>();
builder.Services.AddSingleton(TimeProvider.System);

// Spec 008 US2 — CRM config (queries + commands + validator)
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Config.Queries.GetWhatsAppConfigQuery>();
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Config.Commands.UpdateWhatsAppConfigCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Config.Commands.ToggleWhatsAppChannelCommand>();
builder.Services.AddValidatorsFromAssemblyContaining<omniDesk.Api.Features.WhatsApp.Config.Validators.UpdateWhatsAppConfigValidator>();

// Spec 008 US3 — Send + guards + token revoked detector
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Send.SessionWindowGuard>();
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Send.WaOutgoingGuard>();
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Send.Commands.SendWhatsAppMessageCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Jobs.WaTokenRevokedDetectorJob>();

// Spec 008 US4 — Session window sweep job
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Jobs.WaSessionExpiringNotifierJob>();

// Spec 008 US5 — Templates CRUD + submit + webhook status handler + poller
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Templates.Queries.ListTemplatesQuery>();
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Templates.Commands.CreateTemplateCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Templates.Commands.UpdateTemplateCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Templates.Commands.SubmitTemplateCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Templates.Commands.DeleteTemplateCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Webhook.WaTemplateStatusHandler>();
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Jobs.WaTemplateStatusPollerJob>();
builder.Services.AddValidatorsFromAssemblyContaining<omniDesk.Api.Features.WhatsApp.Templates.Validators.CreateTemplateValidator>();

// Spec 008 US6 — Media download job (Meta GET media → MimeTypeDetector → MinIO).
builder.Services.AddScoped<omniDesk.Api.Features.WhatsApp.Jobs.WaMediaDownloadJob>();

// Spec 009 — Tickets/CRM services
builder.Services.AddScoped<omniDesk.Api.Features.Pipelines.PipelineProvisioningService>();
builder.Services.AddScoped<omniDesk.Api.Infrastructure.Tickets.TicketProtocolService>();
builder.Services.AddScoped<omniDesk.Api.Infrastructure.WebSockets.TicketEventPublisher>();
builder.Services.AddScoped<omniDesk.Api.Features.Contacts.ContactDeduplicationService>();
builder.Services.AddScoped<omniDesk.Api.Domain.Tickets.ITicketEventStore, omniDesk.Api.Infrastructure.Tickets.MongoTicketEventStore>();
// Backfill jobs (manual trigger only — no cron; run via Hangfire dashboard post-deploy)
builder.Services.AddScoped<omniDesk.Api.Infrastructure.Jobs.BackfillTicketProtocolJob>();
builder.Services.AddScoped<omniDesk.Api.Features.Contacts.ContactBackfillJob>();
// Spec 009 US3 — SLA monitoring jobs
builder.Services.AddScoped<omniDesk.Api.Infrastructure.Jobs.TicketSlaMonitorJob>();
builder.Services.AddScoped<omniDesk.Api.Infrastructure.Jobs.WaitingClientResumerJob>();
// Spec 009 US2 — queries and commands
builder.Services.AddScoped<omniDesk.Api.Features.Tickets.Queries.SearchTicketsQuery>();
builder.Services.AddScoped<omniDesk.Api.Features.Tickets.Queries.ListTicketsQuery>();
builder.Services.AddScoped<omniDesk.Api.Features.Tickets.Queries.GetTicketDetailQuery>();
builder.Services.AddScoped<omniDesk.Api.Features.Tickets.Commands.ChangeTicketStatusCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.Tickets.Commands.ResolveTicketCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.Tickets.Commands.CancelTicketCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.Tickets.Commands.UpdateTicketCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.Tickets.Notes.AddTicketNoteCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.Tickets.Commands.TransferTicketCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.Tickets.Commands.CreateManualTicketCommand>();
// Spec 010 — Notifications (real impl replaces Spec 009's NoOp stub).
builder.Services.AddMemoryCache();
builder.Services.AddScoped<omniDesk.Api.Infrastructure.Notifications.NotificationRepository>();
builder.Services.AddScoped<omniDesk.Api.Infrastructure.Notifications.PushSubscriptionRepository>();
builder.Services.AddScoped<omniDesk.Api.Infrastructure.Notifications.AttendantPreferencesRepository>();
builder.Services.AddScoped<omniDesk.Api.Infrastructure.Notifications.TenantSettingsRepository>();
builder.Services.AddScoped<omniDesk.Api.Infrastructure.WebSockets.NotificationEventPublisher>();
builder.Services.AddScoped<omniDesk.Api.Features.Notifications.SupervisorLookupService>();
builder.Services.AddScoped<omniDesk.Api.Features.Notifications.INotificationService,
    omniDesk.Api.Features.Notifications.NotificationService>();
// Spec 009 US9 — Pipeline config
builder.Services.AddScoped<omniDesk.Api.Features.Pipelines.Queries.GetPipelineWithColumnsQuery>();
builder.Services.AddScoped<omniDesk.Api.Features.Pipelines.Queries.ListPipelinesQuery>();
builder.Services.AddScoped<omniDesk.Api.Features.Pipelines.Commands.UpdatePipelineColumnsCommand>();
// Spec 009 US6 — Contact profile
builder.Services.AddScoped<omniDesk.Api.Features.Contacts.Queries.ListContactsQuery>();
builder.Services.AddScoped<omniDesk.Api.Features.Contacts.Queries.GetContactQuery>();
builder.Services.AddScoped<omniDesk.Api.Features.Contacts.Queries.ListContactTicketsQuery>();
builder.Services.AddScoped<omniDesk.Api.Features.Contacts.Queries.ListContactConversationsQuery>();
builder.Services.AddScoped<omniDesk.Api.Features.Contacts.Commands.CreateContactCommand>();
builder.Services.AddScoped<omniDesk.Api.Features.Contacts.Commands.UpdateContactCommand>();

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

// Spec 008 — captura raw body APENAS na rota de webhook WhatsApp para HMAC-SHA256
// (deve rodar antes de Authentication/Authorization para que o handler tenha o byte[]).
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api/public/whatsapp/webhook", StringComparison.OrdinalIgnoreCase),
    branch => branch.UseMiddleware<omniDesk.Api.Features.WhatsApp.Webhook.RawBodyCaptureMiddleware>());

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

// Spec 008 US4 — emite wa.session_expiring / wa.session_expired via WS (cron */5min).
RecurringJob.AddOrUpdate<omniDesk.Api.Features.WhatsApp.Jobs.WaSessionExpiringNotifierJob>(
    "wa-session-expiring-notifier",
    job => job.RunAsync(CancellationToken.None),
    "*/5 * * * *");

// Spec 008 US5 — fallback poller para template status (cron @hourly).
RecurringJob.AddOrUpdate<omniDesk.Api.Features.WhatsApp.Jobs.WaTemplateStatusPollerJob>(
    "wa-template-status-poller",
    job => job.RunAsync(CancellationToken.None),
    "0 * * * *");

// Spec 009 US3 — SLA monitor: runs every minute across all active tenants.
RecurringJob.AddOrUpdate<omniDesk.Api.Infrastructure.Jobs.TicketSlaMonitorJob>(
    "sla-monitor",
    job => job.RunAsync(CancellationToken.None),
    Cron.Minutely());

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

// Spec 009 US2 — CRM ticket management (list, detail, update, status, resolve, cancel, notes)
tickets.MapTicketEndpoints();

// Spec 009 US9 — Pipeline config
var pipelines = api.MapGroup("/pipelines")
                   .RequireAuthorization()
                   .AddEndpointFilter<ImpersonationAuditFilter>();
pipelines.MapPipelineEndpoints();

// Spec 009 US6 — Contact profile
var contacts = api.MapGroup("/contacts")
                  .RequireAuthorization()
                  .AddEndpointFilter<ImpersonationAuditFilter>();
contacts.MapContactEndpoints();

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

// Spec 008 — Public WhatsApp webhook (HMAC validation; no user auth).
omniDesk.Api.Features.WhatsApp.Webhook.WhatsAppWebhookEndpoints.MapWhatsAppWebhookEndpoints(app);

// Spec 008 US2 — CRM config (JWT auth + RBAC policies).
var whatsappConfig = api.MapGroup("/whatsapp/config")
    .RequireAuthorization()
    .AddEndpointFilter<ImpersonationAuditFilter>();
whatsappConfig.MapWhatsAppConfigEndpoints();

// Spec 008 US3 — Atendente envia mensagem (texto livre dentro da janela 24h).
var whatsappSend = api.MapGroup("/whatsapp/send")
    .RequireAuthorization()
    .AddEndpointFilter<ImpersonationAuditFilter>();
omniDesk.Api.Features.WhatsApp.Send.WhatsAppSendEndpoint.MapWhatsAppSendEndpoint(whatsappSend);

// Spec 008 US5 — Templates CRUD (JWT auth; CRUD requer CanManageTemplates).
var whatsappTemplates = api.MapGroup("/whatsapp/templates")
    .RequireAuthorization()
    .AddEndpointFilter<ImpersonationAuditFilter>();
omniDesk.Api.Features.WhatsApp.Templates.WhatsAppTemplatesEndpoints.MapWhatsAppTemplatesEndpoints(whatsappTemplates);

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
