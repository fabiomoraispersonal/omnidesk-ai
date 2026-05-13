using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Features.Agenda.Tools;
using omniDesk.Api.Infrastructure.OpenAi;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.AgentRuntime;

public class ToolCallDispatcher
{
    private readonly AppDbContext _db;
    private readonly IConversationGateway _conversation;
    private readonly ITicketCreationGateway _ticketGateway;
    private readonly AgentResolver _resolver;
    private readonly CheckAvailabilityTool _checkAvailability;
    private readonly CreateAppointmentTool _createAppointment;
    private readonly ILogger<ToolCallDispatcher> _logger;

    public ToolCallDispatcher(
        AppDbContext db,
        IConversationGateway conversation,
        ITicketCreationGateway ticketGateway,
        AgentResolver resolver,
        CheckAvailabilityTool checkAvailability,
        CreateAppointmentTool createAppointment,
        ILogger<ToolCallDispatcher> logger)
    {
        _db = db;
        _conversation = conversation;
        _ticketGateway = ticketGateway;
        _resolver = resolver;
        _checkAvailability = checkAvailability;
        _createAppointment = createAppointment;
        _logger = logger;
    }

    public async Task<ToolDispatchResult> DispatchAsync(
        ToolCall call,
        ToolDispatchContext context,
        CancellationToken ct)
    {
        return call.ToolName switch
        {
            ToolNames.HandoffToAgent => await HandleHandoffAsync(call, context, ct),
            ToolNames.TransferToHuman => await HandleTransferToHumanAsync(call, context, ct),
            ToolNames.CheckAvailability => new ToolDispatchResult(
                new ToolOutput(call.CallId, await _checkAvailability.ExecuteAsync(call.ArgumentsJson, context, ct)),
                ToolDispatchOutcome.SubmitErrorContinue),
            ToolNames.CreateAppointment => new ToolDispatchResult(
                new ToolOutput(call.CallId, await _createAppointment.ExecuteAsync(call.ArgumentsJson, context, ct)),
                ToolDispatchOutcome.SubmitErrorContinue),
            _ => Unknown(call),
        };
    }

    private async Task<ToolDispatchResult> HandleHandoffAsync(ToolCall call, ToolDispatchContext ctx, CancellationToken ct)
    {
        var args = JsonDocument.Parse(call.ArgumentsJson).RootElement;
        var requestedId = args.TryGetProperty("agent_id", out var idEl) ? idEl.GetString() ?? "" : "";
        var reason = args.TryGetProperty("reason", out var rEl) ? rEl.GetString() ?? "" : "";

        AiAgent? target = null;
        if (string.Equals(requestedId, ToolNames.OrchestratorShortcut, StringComparison.OrdinalIgnoreCase))
        {
            target = await _resolver.GetOrchestratorAsync(ct);
        }
        else if (Guid.TryParse(requestedId, out var guid))
        {
            target = await _resolver.GetByIdAsync(guid, ct);
        }

        if (target is null || !target.IsActive)
        {
            return ErrorOutput(call, "AGENT_NOT_ACTIVE", $"Agente '{requestedId}' não está ativo no tenant.");
        }

        // Loop detection — 3 handoffs to the same target in this run.
        if (ctx.HandoffHistory.Count(id => id == target.Id) >= 3)
        {
            return ErrorOutput(call, "HANDOFF_LOOP_DETECTED", "Loop de handoff detectado. Acionando transbordo automático.");
        }

        await _conversation.SetCurrentAgentAsync(ctx.ThreadId, target.Type == AgentType.Orchestrator ? null : target.Id, ct);
        ctx.HandoffHistory.Add(target.Id);

        return new ToolDispatchResult(
            new ToolOutput(call.CallId, JsonSerializer.Serialize(new { success = true, next_agent_name = target.Name })),
            ToolDispatchOutcome.HandoffToAgent,
            HandoffTargetAgentId: target.Id,
            NewAssistantId: target.OpenAiAssistantId);
    }

    private async Task<ToolDispatchResult> HandleTransferToHumanAsync(ToolCall call, ToolDispatchContext ctx, CancellationToken ct)
    {
        var args = JsonDocument.Parse(call.ArgumentsJson).RootElement;
        Guid? departmentId = null;
        if (args.TryGetProperty("department_id", out var dEl)
            && dEl.ValueKind == JsonValueKind.String
            && Guid.TryParse(dEl.GetString(), out var dGuid))
        {
            departmentId = dGuid;
        }
        var reason = args.TryGetProperty("reason", out var rEl) ? rEl.GetString() ?? "" : "";

        // Resolve destination department: explicit > sub-agent's department > tenant default.
        if (departmentId is null && ctx.CurrentAgent is not null)
        {
            departmentId = ctx.CurrentAgent.DepartmentId;
        }
        if (departmentId is null)
        {
            departmentId = await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == ctx.TenantId)
                .Select(t => t.DefaultDepartmentId)
                .FirstOrDefaultAsync(ct);
        }
        if (departmentId is null)
        {
            _logger.LogError("transfer_to_human invoked but no destination department resolvable for tenant {TenantId}.", ctx.TenantId);
            return ErrorOutput(call, "DEPARTMENT_UNRESOLVED",
                "Nenhum departamento disponível para transbordo. Configure default_department_id no tenant.");
        }

        // Validate department is active.
        var deptActive = await _db.Departments.AsNoTracking()
            .AnyAsync(d => d.Id == departmentId && d.IsActive, ct);
        if (!deptActive)
        {
            return ErrorOutput(call, "DEPARTMENT_NOT_ACTIVE",
                $"Departamento {departmentId} inativo ou inexistente.");
        }

        var history = await _conversation.GetRecentMessagesAsync(ctx.ThreadId, 100, ct);
        var ticket = await _ticketGateway.CreateTicketFromAiHandoffAsync(
            new TicketHandoffRequest(
                ConversationId: ctx.ThreadId,
                ThreadId: ctx.ThreadId,
                DepartmentId: departmentId.Value,
                Reason: reason,
                OriginatingAgentId: ctx.CurrentAgent?.Id,
                Channel: omniDesk.Api.Domain.Tickets.TicketChannel.LiveChat,
                ContactHints: null,
                SubjectSuggestion: null,
                History: history,
                ExternalConversationRef: ctx.ExternalConversationRef),
            ct);

        await _conversation.MarkHandedOffAsync(ctx.ThreadId, ct);

        var output = new
        {
            success = true,
            ticket_id = ticket.TicketId,
            ticket_number = ticket.Protocol,
            department_name = ticket.DepartmentName,
            instruction_for_agent =
                $"Envie ao cliente: 'Vou transferir você para nossa equipe de {ticket.DepartmentName}. Aguarde um momento.'",
        };

        return new ToolDispatchResult(
            new ToolOutput(call.CallId, JsonSerializer.Serialize(output)),
            ToolDispatchOutcome.TransferredToHuman,
            HandoffTargetDepartmentId: departmentId,
            HandoffTicketId: ticket.TicketId,
            HandoffDepartmentName: ticket.DepartmentName);
    }

    private static ToolDispatchResult Unavailable(ToolCall call, string message)
        => ErrorOutput(call, "TOOL_NOT_AVAILABLE", message);

    private static ToolDispatchResult Unknown(ToolCall call)
        => ErrorOutput(call, "UNKNOWN_TOOL", $"Tool '{call.ToolName}' desconhecida.");

    private static ToolDispatchResult ErrorOutput(ToolCall call, string code, string message)
        => new(
            new ToolOutput(call.CallId, JsonSerializer.Serialize(new { success = false, error = code, message })),
            ToolDispatchOutcome.SubmitErrorContinue);
}

public class ToolDispatchContext
{
    public required Guid TenantId { get; init; }
    public required string TenantSlug { get; init; }
    public required Guid ThreadId { get; init; }
    public required string ExternalConversationRef { get; init; }
    public AiAgent? CurrentAgent { get; set; }
    public List<Guid> HandoffHistory { get; } = new();
}

public enum ToolDispatchOutcome
{
    SubmitErrorContinue,
    HandoffToAgent,
    TransferredToHuman,
}

public record ToolDispatchResult(
    ToolOutput Output,
    ToolDispatchOutcome Outcome,
    Guid? HandoffTargetAgentId = null,
    Guid? HandoffTargetDepartmentId = null,
    string? NewAssistantId = null,
    Guid? HandoffTicketId = null,
    string? HandoffDepartmentName = null);
