using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Domain.AiThreads;
using omniDesk.Api.Features.AiAgents.Variables;
using omniDesk.Api.Infrastructure.ActivityLogs;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.OpenAi;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.AgentRuntime;

/// <summary>
/// Heart of Spec 006 — orchestrates a single client turn end-to-end.
/// Linear flow per research §R3 (no state machine framework).
/// </summary>
public class AgentOrchestrator
{
    private readonly AppDbContext _db;
    private readonly IConversationGateway _conversation;
    private readonly ITicketCreationGateway _ticketGateway;
    private readonly IAssistantsApi _assistantsApi;
    private readonly OpenAiKeyResolver _keyResolver;
    private readonly AgentResolver _agentResolver;
    private readonly ContextBuilder _contextBuilder;
    private readonly HandoffKeywordDetector _handoffKeyword;
    private readonly ToolCallDispatcher _dispatcher;
    private readonly RetryPolicy _retry;
    private readonly AgentActivityLogger _activityLogger;
    private readonly TenantContextHolder _tenantContext;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        AppDbContext db,
        IConversationGateway conversation,
        ITicketCreationGateway ticketGateway,
        IAssistantsApi assistantsApi,
        OpenAiKeyResolver keyResolver,
        AgentResolver agentResolver,
        ContextBuilder contextBuilder,
        HandoffKeywordDetector handoffKeyword,
        ToolCallDispatcher dispatcher,
        RetryPolicy retry,
        AgentActivityLogger activityLogger,
        TenantContextHolder tenantContext,
        ILogger<AgentOrchestrator> logger)
    {
        _db = db;
        _conversation = conversation;
        _ticketGateway = ticketGateway;
        _assistantsApi = assistantsApi;
        _keyResolver = keyResolver;
        _agentResolver = agentResolver;
        _contextBuilder = contextBuilder;
        _handoffKeyword = handoffKeyword;
        _dispatcher = dispatcher;
        _retry = retry;
        _activityLogger = activityLogger;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task ProcessAsync(IncomingMessage message, CancellationToken ct)
    {
        _tenantContext.Set(message.TenantSlug, message.TenantId);

        // 1. Resolve credentials (per-execution, supports tenant key vs global).
        var credentials = await _keyResolver.ResolveAsync(message.TenantId, ct);
        if (string.IsNullOrEmpty(credentials.ApiKey))
        {
            _logger.LogError("No OpenAI key available for tenant {Tenant}; aborting run.", message.TenantSlug);
            return;
        }

        // 2. Get-or-create thread (idempotent on external_conversation_ref).
        var thread = await _conversation.GetOrCreateThreadAsync(
            message.ExternalConversationRef,
            () => _assistantsApi.CreateThreadAsync(credentials, ct),
            ct);

        // 3. If handed off to human, send auto-reply only and return — IA does not process anymore (FR-015).
        if (thread.HandedOffToHumanAt is not null)
        {
            await _conversation.EnqueueOutgoingAsync(thread.Id,
                new OutgoingMessage(
                    "Sua mensagem foi recebida. Um atendente responderá em breve.",
                    "system",
                    null),
                ct);
            return;
        }

        // 4. Resolve current agent (orchestrator if null/inactive).
        var threadEntity = await _db.AiThreads.FirstAsync(t => t.Id == thread.Id, ct);
        var currentAgent = await _agentResolver.ResolveCurrentAgentAsync(threadEntity, ct);
        if (currentAgent is null)
        {
            _logger.LogError("No active orchestrator found for tenant {Tenant}.", message.TenantSlug);
            return;
        }

        // 5. Detect explicit handoff keyword and inject guidance for the IA (FR-013).
        var systemHint = _handoffKeyword.ShouldForceHumanHandoff(message.Content)
            ? _handoffKeyword.BuildSystemHint(message.Content)
            : null;

        // 6. Append user message to OpenAI thread.
        var contentToSend = systemHint is null
            ? message.Content
            : message.Content + "\n\n" + systemHint;
        await _assistantsApi.AppendUserMessageAsync(thread.OpenAiThreadId, contentToSend, credentials, ct);

        // 7. Run loop with retry policy. Wraps the OpenAI conversation including any tool dispatch.
        await RunLoopAsync(message, thread, threadEntity, currentAgent, credentials, ct);
    }

    private async Task RunLoopAsync(
        IncomingMessage message,
        AiThreadDto thread,
        AiThread threadEntity,
        AiAgent currentAgent,
        OpenAiCredentials credentials,
        CancellationToken ct)
    {
        var dispatchContext = new ToolDispatchContext
        {
            TenantId = message.TenantId,
            TenantSlug = message.TenantSlug,
            ThreadId = thread.Id,
            ExternalConversationRef = message.ExternalConversationRef,
            CurrentAgent = currentAgent,
        };

        var attempt = 0;
        while (true)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var assistantId = await _assistantsApi.EnsureAssistantAsync(currentAgent, credentials, ct);
                if (assistantId != currentAgent.OpenAiAssistantId)
                {
                    var tracked = await _db.AiAgents.FirstAsync(a => a.Id == currentAgent.Id, ct);
                    tracked.OpenAiAssistantId = assistantId;
                    await _db.SaveChangesAsync(ct);
                }

                // Build per-run instructions including resolved variables.
                var department = currentAgent.DepartmentId is { } deptId
                    ? await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deptId, ct)
                    : null;
                var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == message.TenantId, ct);
                var subAgents = currentAgent.Type == AgentType.Orchestrator
                    ? await _agentResolver.ListActiveSubAgentsAsync(ct)
                    : Array.Empty<AiAgent>();

                var instructions = await _contextBuilder.BuildInstructionsAsync(
                    currentAgent,
                    new AgentVariablesContext(
                        tenant.NomeFantasia ?? tenant.RazaoSocial,
                        department?.Name,
                        AttendantName: null),
                    subAgents,
                    ct);

                var run = await _assistantsApi.CreateRunAsync(thread.OpenAiThreadId, assistantId, instructions, credentials, ct);
                run = await _assistantsApi.PollRunAsync(thread.OpenAiThreadId, run.Id, _retry.RunTimeout, credentials, ct);

                while (run.Status == "requires_action" && run.ToolCalls.Count > 0)
                {
                    var outputs = new List<ToolOutput>();
                    ToolDispatchResult? lastResult = null;
                    foreach (var call in run.ToolCalls)
                    {
                        var result = await _dispatcher.DispatchAsync(call, dispatchContext, ct);
                        outputs.Add(result.Output);
                        lastResult = result;
                        await LogActionAsync(message, thread, currentAgent, run, result, sw.ElapsedMilliseconds, ct);
                    }
                    run = await _assistantsApi.SubmitToolOutputsAsync(thread.OpenAiThreadId, run.Id, outputs, credentials, ct);

                    if (lastResult?.Outcome == ToolDispatchOutcome.TransferredToHuman)
                    {
                        // IA stops here — do not continue this turn.
                        return;
                    }
                    if (lastResult?.Outcome == ToolDispatchOutcome.HandoffToAgent
                        && lastResult.HandoffTargetAgentId is { } targetId)
                    {
                        var nextAgent = await _agentResolver.GetByIdAsync(targetId, ct);
                        if (nextAgent is not null)
                        {
                            currentAgent = nextAgent;
                            dispatchContext.CurrentAgent = nextAgent;
                            // Open a new run with the destination assistant.
                            run = await _assistantsApi.CreateRunAsync(thread.OpenAiThreadId,
                                await _assistantsApi.EnsureAssistantAsync(nextAgent, credentials, ct),
                                null, credentials, ct);
                            run = await _assistantsApi.PollRunAsync(thread.OpenAiThreadId, run.Id, _retry.RunTimeout, credentials, ct);
                            continue;
                        }
                    }

                    run = await _assistantsApi.PollRunAsync(thread.OpenAiThreadId, run.Id, _retry.RunTimeout, credentials, ct);
                }

                if (run.Status == "completed")
                {
                    var reply = await _assistantsApi.GetLatestAssistantMessageAsync(thread.OpenAiThreadId, run.Id, credentials, ct);
                    sw.Stop();
                    if (!string.IsNullOrWhiteSpace(reply))
                    {
                        await _conversation.EnqueueOutgoingAsync(thread.Id,
                            new OutgoingMessage(reply!, "agent", currentAgent.Id), ct);
                    }
                    await _activityLogger.LogAsync(BuildLog(message, thread, currentAgent, run,
                        AgentActivityActions.Respond, sw.ElapsedMilliseconds), ct);
                    return;
                }

                // Failed/cancelled/expired → treat as error.
                throw new OpenAiHttpException(500, $"Run terminated with status '{run.Status}': {run.Error?.Message ?? "(no detail)"}");
            }
            catch (Exception ex) when (attempt < _retry.MaxRetries && _retry.Decide(ex) == RetryDecision.Retry)
            {
                attempt++;
                await _activityLogger.LogAsync(BuildErrorLog(message, thread, currentAgent, ex), ct);
                _logger.LogWarning(ex, "Agent run failed (attempt {Attempt}); retrying after {Backoff}.",
                    attempt, _retry.Backoff);
                await Task.Delay(_retry.Backoff, ct);
            }
            catch (Exception ex)
            {
                await _activityLogger.LogAsync(BuildErrorLog(message, thread, currentAgent, ex), ct);
                await ApplyApiFailureFallbackAsync(message, thread, currentAgent, dispatchContext, ct);
                return;
            }
        }
    }

    /// <summary>
    /// US6 — On unrecoverable OpenAI failure, route to default department and emit
    /// the system instability message before stopping (FR-020).
    /// </summary>
    private async Task ApplyApiFailureFallbackAsync(
        IncomingMessage message,
        AiThreadDto thread,
        AiAgent currentAgent,
        ToolDispatchContext dispatchContext,
        CancellationToken ct)
    {
        var departmentId = currentAgent.DepartmentId
            ?? await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == message.TenantId)
                .Select(t => t.DefaultDepartmentId)
                .FirstOrDefaultAsync(ct);

        if (departmentId is null)
        {
            _logger.LogError("Unrecoverable OpenAI failure with no default department; dropping message {MessageId}.", message.MessageId);
            await _conversation.EnqueueOutgoingAsync(thread.Id,
                new OutgoingMessage(
                    "Estamos com uma instabilidade técnica no momento. Por favor, retorne em alguns minutos.",
                    "system", null),
                ct);
            return;
        }

        var historyMessages = await _conversation.GetRecentMessagesAsync(thread.Id, 100, ct);
        await _ticketGateway.CreateTicketFromAiHandoffAsync(
            new TicketHandoffRequest(
                thread.Id, departmentId.Value, "Falha técnica no agente de IA",
                currentAgent.Id, historyMessages, message.ExternalConversationRef),
            ct);

        await _conversation.MarkHandedOffAsync(thread.Id, ct);

        await _conversation.EnqueueOutgoingAsync(thread.Id,
            new OutgoingMessage(
                "Estamos com uma instabilidade técnica no momento. Vou transferir você para um de nossos atendentes.",
                "system", null),
            ct);

        var log = BuildLog(message, thread, currentAgent, run: null,
            AgentActivityActions.TransferToHuman, 0);
        log.HandoffTargetDepartmentId = departmentId;
        await _activityLogger.LogAsync(log, ct);
    }

    private async Task LogActionAsync(IncomingMessage message, AiThreadDto thread, AiAgent agent,
        AssistantRun run, ToolDispatchResult result, long elapsedMs, CancellationToken ct)
    {
        var log = BuildLog(message, thread, agent, run, result.Outcome switch
        {
            ToolDispatchOutcome.HandoffToAgent => AgentActivityActions.HandoffToAgent,
            ToolDispatchOutcome.TransferredToHuman => AgentActivityActions.TransferToHuman,
            _ => AgentActivityActions.Respond,
        }, elapsedMs);
        log.HandoffTargetAgentId = result.HandoffTargetAgentId;
        log.HandoffTargetDepartmentId = result.HandoffTargetDepartmentId;
        await _activityLogger.LogAsync(log, ct);
    }

    private static AgentActivityLog BuildLog(IncomingMessage message, AiThreadDto thread, AiAgent agent,
        AssistantRun? run, string action, long elapsedMs) => new()
        {
            TenantSlug = message.TenantSlug,
            ConversationId = thread.Id,
            AgentId = agent.Id,
            AgentName = agent.Name,
            AgentType = AgentTypes.ToWire(agent.Type),
            Action = action,
            InputTokens = run?.InputTokens ?? 0,
            OutputTokens = run?.OutputTokens ?? 0,
            Model = run?.Model ?? agent.Model,
            LatencyMs = elapsedMs,
            OpenAiRunId = run?.Id,
            OpenAiThreadId = thread.OpenAiThreadId,
            Timestamp = DateTimeOffset.UtcNow,
        };

    private static AgentActivityLog BuildErrorLog(IncomingMessage message, AiThreadDto thread, AiAgent agent, Exception ex)
        => new()
        {
            TenantSlug = message.TenantSlug,
            ConversationId = thread.Id,
            AgentId = agent.Id,
            AgentName = agent.Name,
            AgentType = AgentTypes.ToWire(agent.Type),
            Action = AgentActivityActions.ApiError,
            Model = agent.Model,
            OpenAiThreadId = thread.OpenAiThreadId,
            Error = new AgentActivityError
            {
                Type = RetryPolicy.ClassifyError(ex),
                Status = ex is OpenAiHttpException http ? http.StatusCode : null,
                Message = ex.Message,
            },
            Timestamp = DateTimeOffset.UtcNow,
        };

}
