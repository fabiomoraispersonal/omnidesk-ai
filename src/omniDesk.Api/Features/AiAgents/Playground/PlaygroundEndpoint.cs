using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Features.AiAgents.Variables;
using omniDesk.Api.Features.Authorization.Policies;
using omniDesk.Api.Infrastructure.Authentication;
using omniDesk.Api.Infrastructure.OpenAi;
using omniDesk.Api.Infrastructure.Persistence;

namespace omniDesk.Api.Features.AiAgents.Playground;

public static class PlaygroundEndpoint
{
    public static RouteGroupBuilder MapPlayground(this RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/test", TestAsync).RequireAuthorization(Policies.CanUseAgentPlayground);
        group.MapDelete("/playground-sessions/{sessionId}", DeleteSessionAsync)
             .RequireAuthorization(Policies.CanUseAgentPlayground);
        return group;
    }

    private static async Task<IResult> TestAsync(
        Guid id,
        TestRequest req,
        AppDbContext db,
        IAssistantsApi assistantsApi,
        OpenAiKeyResolver keys,
        PlaygroundSessionStore store,
        PromptVariableSubstitutor substitutor,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return Results.BadRequest(new { success = false, error = "MESSAGE_REQUIRED" });
        if (req.Message.Length > 5000)
            return Results.BadRequest(new { success = false, error = "MESSAGE_TOO_LONG" });
        if (currentUser.TenantId is null)
            return Results.Unauthorized();

        var agent = await db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent is null) return Results.NotFound(new { success = false, error = "AGENT_NOT_FOUND" });

        var slug = currentUser.TenantSlug;
        var creds = await keys.ResolveAsync(currentUser.TenantId.Value, ct);

        // Get-or-create session.
        PlaygroundSession session;
        if (!string.IsNullOrEmpty(req.SessionId)
            && await store.GetAsync(slug, req.SessionId!) is { } existing)
        {
            session = existing;
        }
        else
        {
            var threadId = await assistantsApi.CreateThreadAsync(creds, ct);
            session = await store.CreateAsync(slug, id, threadId);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Append user message.
        await assistantsApi.AppendUserMessageAsync(session.OpenAiThreadId, req.Message, creds, ct);

        // Resolve assistant + run.
        var assistantId = await assistantsApi.EnsureAssistantAsync(agent, creds, ct);
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == currentUser.TenantId, ct);
        var department = agent.DepartmentId is { } d
            ? await db.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == d, ct)
            : null;
        var instructions = substitutor.Apply(agent.Prompt,
            new AgentVariablesContext(tenant.NomeFantasia ?? tenant.RazaoSocial, department?.Name, null));

        var run = await assistantsApi.CreateRunAsync(session.OpenAiThreadId, assistantId, instructions, creds, ct);
        run = await assistantsApi.PollRunAsync(session.OpenAiThreadId, run.Id, TimeSpan.FromSeconds(30), creds, ct);

        var observed = new List<object>();
        // Tool calls in playground are simulated — never produce side effects.
        while (run.Status == "requires_action" && run.ToolCalls.Count > 0)
        {
            var outputs = run.ToolCalls.Select(tc =>
            {
                observed.Add(new { tool = tc.ToolName, args = tc.ArgumentsJson });
                return new ToolOutput(tc.CallId,
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = true,
                        simulated = true,
                        message = "Tool simulada no playground — nenhum efeito colateral aplicado.",
                    }));
            }).ToList();
            run = await assistantsApi.SubmitToolOutputsAsync(session.OpenAiThreadId, run.Id, outputs, creds, ct);
            run = await assistantsApi.PollRunAsync(session.OpenAiThreadId, run.Id, TimeSpan.FromSeconds(30), creds, ct);
        }

        var reply = await assistantsApi.GetLatestAssistantMessageAsync(session.OpenAiThreadId, run.Id, creds, ct);
        sw.Stop();

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                session_id = session.SessionId,
                agent_id = agent.Id,
                agent_name = agent.Name,
                reply,
                tool_calls_observed = observed,
                elapsed_ms = sw.ElapsedMilliseconds,
                model = run.Model ?? agent.Model,
                tokens = new { input = run.InputTokens ?? 0, output = run.OutputTokens ?? 0 },
                expires_at = store.Now().Add(store.ConfiguredTtl),
            },
        });
    }

    private static async Task<IResult> DeleteSessionAsync(
        string sessionId,
        PlaygroundSessionStore store,
        ICurrentUser currentUser,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(currentUser.TenantSlug)) return Results.Unauthorized();
        await store.DeleteAsync(currentUser.TenantSlug, sessionId);
        return Results.NoContent();
    }

    public record TestRequest(string Message, string? SessionId);
}
