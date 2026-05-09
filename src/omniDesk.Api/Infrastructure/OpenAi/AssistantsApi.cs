using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using omniDesk.Api.Domain.AiAgents;

namespace omniDesk.Api.Infrastructure.OpenAi;

/// <summary>
/// Direct REST client over OpenAI Assistants v2 (https://platform.openai.com/docs/assistants).
/// Constituição V — sem SDK extra; constituição VII — testes via MockHttpMessageHandler (ADR-006-001).
/// </summary>
public class AssistantsApi : IAssistantsApi
{
    private const string BaseUri = "https://api.openai.com/";
    private const string AssistantsHeader = "OpenAI-Beta";
    private const string AssistantsHeaderValue = "assistants=v2";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AssistantsApi> _logger;

    public AssistantsApi(IHttpClientFactory httpFactory, ILogger<AssistantsApi> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<string> EnsureAssistantAsync(AiAgent agent, OpenAiCredentials cred, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(agent.OpenAiAssistantId))
        {
            // Verify still exists.
            using var http = CreateClient(cred);
            var resp = await http.GetAsync($"v1/assistants/{agent.OpenAiAssistantId}", ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Assistant {AssistantId} for agent {AgentId} disappeared on OpenAI; recreating.",
                    agent.OpenAiAssistantId, agent.Id);
            }
            else if (resp.IsSuccessStatusCode)
            {
                return agent.OpenAiAssistantId;
            }
            else
            {
                throw await ToHttpExceptionAsync(resp, ct);
            }
        }

        return await CreateAssistantAsync(agent, cred, ct);
    }

    private async Task<string> CreateAssistantAsync(AiAgent agent, OpenAiCredentials cred, CancellationToken ct)
    {
        using var http = CreateClient(cred);
        var body = new
        {
            name = agent.Name,
            instructions = agent.Prompt,
            model = agent.Model,
            tools = OpenAiToolDefinitions.All(),
        };
        using var resp = await http.PostAsJsonAsync("v1/assistants", body, ct);
        await EnsureSuccessAsync(resp, ct);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return doc.GetProperty("id").GetString()!;
    }

    public async Task UpdateAssistantAsync(string assistantId, AiAgent agent, OpenAiCredentials cred, CancellationToken ct)
    {
        using var http = CreateClient(cred);
        var body = new
        {
            name = agent.Name,
            instructions = agent.Prompt,
            model = agent.Model,
            tools = OpenAiToolDefinitions.All(),
        };
        using var resp = await http.PostAsJsonAsync($"v1/assistants/{assistantId}", body, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    public async Task<string> CreateThreadAsync(OpenAiCredentials cred, CancellationToken ct)
    {
        using var http = CreateClient(cred);
        using var resp = await http.PostAsync("v1/threads", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), ct);
        await EnsureSuccessAsync(resp, ct);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return doc.GetProperty("id").GetString()!;
    }

    public async Task DeleteThreadAsync(string threadId, OpenAiCredentials cred, CancellationToken ct)
    {
        using var http = CreateClient(cred);
        using var resp = await http.DeleteAsync($"v1/threads/{threadId}", ct);
        if (resp.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(resp, ct);
        }
    }

    public async Task AppendUserMessageAsync(string threadId, string content, OpenAiCredentials cred, CancellationToken ct)
    {
        using var http = CreateClient(cred);
        var body = new { role = "user", content };
        using var resp = await http.PostAsJsonAsync($"v1/threads/{threadId}/messages", body, ct);
        await EnsureSuccessAsync(resp, ct);
    }

    public async Task<AssistantRun> CreateRunAsync(string threadId, string assistantId, string? instructionsOverride, OpenAiCredentials cred, CancellationToken ct)
    {
        using var http = CreateClient(cred);
        var body = instructionsOverride is null
            ? (object)new { assistant_id = assistantId }
            : new { assistant_id = assistantId, instructions = instructionsOverride };
        using var resp = await http.PostAsJsonAsync($"v1/threads/{threadId}/runs", body, ct);
        await EnsureSuccessAsync(resp, ct);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ParseRun(doc);
    }

    public async Task<AssistantRun> PollRunAsync(string threadId, string runId, TimeSpan timeout, OpenAiCredentials cred, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var poll = TimeSpan.FromMilliseconds(500);
        while (!cts.IsCancellationRequested)
        {
            using var http = CreateClient(cred);
            using var resp = await http.GetAsync($"v1/threads/{threadId}/runs/{runId}", cts.Token);
            await EnsureSuccessAsync(resp, cts.Token);
            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cts.Token);
            var run = ParseRun(doc);
            if (run.Status is "completed" or "requires_action" or "failed" or "cancelled" or "expired")
                return run;
            await Task.Delay(poll, cts.Token);
        }
        throw new TimeoutException($"Run {runId} did not finish within {timeout}.");
    }

    public async Task<AssistantRun> SubmitToolOutputsAsync(string threadId, string runId, IReadOnlyList<ToolOutput> outputs, OpenAiCredentials cred, CancellationToken ct)
    {
        using var http = CreateClient(cred);
        var body = new
        {
            tool_outputs = outputs.Select(o => new { tool_call_id = o.CallId, output = o.OutputJson }).ToArray(),
        };
        using var resp = await http.PostAsJsonAsync($"v1/threads/{threadId}/runs/{runId}/submit_tool_outputs", body, ct);
        await EnsureSuccessAsync(resp, ct);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ParseRun(doc);
    }

    public async Task<string?> GetLatestAssistantMessageAsync(string threadId, string runId, OpenAiCredentials cred, CancellationToken ct)
    {
        using var http = CreateClient(cred);
        using var resp = await http.GetAsync($"v1/threads/{threadId}/messages?order=desc&limit=10&run_id={runId}", ct);
        await EnsureSuccessAsync(resp, ct);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!doc.TryGetProperty("data", out var data)) return null;
        foreach (var msg in data.EnumerateArray())
        {
            if (msg.GetProperty("role").GetString() == "assistant"
                && msg.TryGetProperty("content", out var content))
            {
                foreach (var part in content.EnumerateArray())
                {
                    if (part.GetProperty("type").GetString() == "text")
                        return part.GetProperty("text").GetProperty("value").GetString();
                }
            }
        }
        return null;
    }

    private HttpClient CreateClient(OpenAiCredentials cred)
    {
        var http = _httpFactory.CreateClient("openai-assistants");
        http.BaseAddress = new Uri(BaseUri);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cred.ApiKey);
        http.DefaultRequestHeaders.Remove(AssistantsHeader);
        http.DefaultRequestHeaders.Add(AssistantsHeader, AssistantsHeaderValue);
        if (!string.IsNullOrEmpty(cred.Organization))
            http.DefaultRequestHeaders.Add("OpenAI-Organization", cred.Organization);
        if (!string.IsNullOrEmpty(cred.Project))
            http.DefaultRequestHeaders.Add("OpenAI-Project", cred.Project);
        return http;
    }

    private static AssistantRun ParseRun(JsonElement doc)
    {
        var id = doc.GetProperty("id").GetString()!;
        var status = doc.GetProperty("status").GetString()!;
        int? inT = null, outT = null;
        if (doc.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) inT = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ot)) outT = ot.GetInt32();
        }
        var model = doc.TryGetProperty("model", out var m) ? m.GetString() : null;
        var calls = new List<ToolCall>();
        if (doc.TryGetProperty("required_action", out var ra) && ra.ValueKind == JsonValueKind.Object
            && ra.TryGetProperty("submit_tool_outputs", out var sto)
            && sto.TryGetProperty("tool_calls", out var tcs))
        {
            foreach (var tc in tcs.EnumerateArray())
            {
                calls.Add(new ToolCall(
                    tc.GetProperty("id").GetString()!,
                    tc.GetProperty("function").GetProperty("name").GetString()!,
                    tc.GetProperty("function").GetProperty("arguments").GetString() ?? "{}"));
            }
        }
        OpenAiErrorDetails? err = null;
        if (doc.TryGetProperty("last_error", out var le) && le.ValueKind == JsonValueKind.Object)
        {
            int? errStatus = le.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : null;
            err = new OpenAiErrorDetails(
                errStatus,
                le.TryGetProperty("code", out var c) ? c.GetString() : null,
                le.TryGetProperty("message", out var msg) ? (msg.GetString() ?? string.Empty) : string.Empty);
        }
        return new AssistantRun(id, status, inT, outT, model, calls, err);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        throw await ToHttpExceptionAsync(resp, ct);
    }

    private static async Task<OpenAiHttpException> ToHttpExceptionAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var body = await resp.Content.ReadAsStringAsync(ct);
        return new OpenAiHttpException((int)resp.StatusCode, body);
    }
}

public class OpenAiHttpException : Exception
{
    public int StatusCode { get; }
    public string Body { get; }

    public OpenAiHttpException(int status, string body)
        : base($"OpenAI HTTP {status}: {body}")
    {
        StatusCode = status;
        Body = body;
    }
}
