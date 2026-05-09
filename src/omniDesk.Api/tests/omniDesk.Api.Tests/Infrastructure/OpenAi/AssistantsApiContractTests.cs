using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Infrastructure.OpenAi;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Infrastructure.OpenAi;

/// <summary>
/// Contract tests for the OpenAI Assistants v2 wrapper, using StubHttpMessageHandler.
/// Justified by ADR-006-001 — OpenAI does not offer a sandbox/replay for Assistants v2.
/// Each test verifies the wrapper sends the right request shape and parses the response correctly.
/// </summary>
public class AssistantsApiContractTests
{
    private static OpenAiCredentials Cred() => new("sk-test-key", "org_x", "proj_y", "test");

    private static (AssistantsApi api, StubHttpMessageHandler stub) Build()
    {
        var stub = new StubHttpMessageHandler();
        var factory = new SingleClientHttpFactory(stub);
        var api = new AssistantsApi(factory, NullLogger<AssistantsApi>.Instance);
        return (api, stub);
    }

    private static AiAgent BuildAgent(string? assistantId = null) => new()
    {
        Id = Guid.NewGuid(),
        Type = AgentType.Orchestrator,
        Name = "Aria",
        Prompt = "You are Aria.",
        Model = "gpt-4o",
        OpenAiAssistantId = assistantId,
    };

    [Fact]
    public async Task EnsureAssistant_CreatesWhenMissing()
    {
        var (api, stub) = Build();
        stub.Map(HttpMethod.Post, "/v1/assistants", body: new { id = "asst_test_1" });

        var assistantId = await api.EnsureAssistantAsync(BuildAgent(), Cred(), CancellationToken.None);

        Assert.Equal("asst_test_1", assistantId);
        var req = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/v1/assistants", req.Path);
        Assert.Contains("\"model\":\"gpt-4o\"", req.Body);
        Assert.True(req.Headers.ContainsKey("OpenAI-Beta"));
        Assert.Contains("assistants=v2", req.Headers["OpenAI-Beta"]);
    }

    [Fact]
    public async Task EnsureAssistant_RecreatesWhen404()
    {
        var (api, stub) = Build();
        stub.Map(HttpMethod.Get, "/v1/assistants/asst_old", HttpStatusCode.NotFound);
        stub.Map(HttpMethod.Post, "/v1/assistants", body: new { id = "asst_new" });

        var id = await api.EnsureAssistantAsync(BuildAgent("asst_old"), Cred(), CancellationToken.None);

        Assert.Equal("asst_new", id);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task EnsureAssistant_ReusesExisting()
    {
        var (api, stub) = Build();
        stub.Map(HttpMethod.Get, "/v1/assistants/asst_1", body: new { id = "asst_1" });

        var id = await api.EnsureAssistantAsync(BuildAgent("asst_1"), Cred(), CancellationToken.None);

        Assert.Equal("asst_1", id);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task UpdateAssistant_PostsToAssistantId()
    {
        var (api, stub) = Build();
        stub.Map(HttpMethod.Post, "/v1/assistants/asst_42", body: new { id = "asst_42" });

        await api.UpdateAssistantAsync("asst_42", BuildAgent("asst_42"), Cred(), CancellationToken.None);

        var req = Assert.Single(stub.Requests);
        Assert.Equal("/v1/assistants/asst_42", req.Path);
    }

    [Fact]
    public async Task CreateThread_ReturnsId()
    {
        var (api, stub) = Build();
        stub.Map(HttpMethod.Post, "/v1/threads", body: new { id = "thread_1" });

        var id = await api.CreateThreadAsync(Cred(), CancellationToken.None);

        Assert.Equal("thread_1", id);
    }

    [Fact]
    public async Task DeleteThread_IgnoresNotFound()
    {
        var (api, stub) = Build();
        stub.Map(HttpMethod.Delete, "/v1/threads/thread_x", HttpStatusCode.NotFound);

        await api.DeleteThreadAsync("thread_x", Cred(), CancellationToken.None);

        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task AppendUserMessage_PostsRoleUser()
    {
        var (api, stub) = Build();
        stub.Map(HttpMethod.Post, "/v1/threads/thread_1/messages", body: new { id = "msg_1" });

        await api.AppendUserMessageAsync("thread_1", "Olá", Cred(), CancellationToken.None);

        var req = Assert.Single(stub.Requests);
        Assert.Contains("\"role\":\"user\"", req.Body);
        Assert.Contains("\"content\":\"Ol", req.Body);
    }

    [Fact]
    public async Task CreateRun_ParsesRunBody()
    {
        var (api, stub) = Build();
        stub.Map(HttpMethod.Post, "/v1/threads/thread_1/runs", body: new
        {
            id = "run_1",
            status = "queued",
            model = "gpt-4o",
        });

        var run = await api.CreateRunAsync("thread_1", "asst_1", instructionsOverride: "Override", Cred(), CancellationToken.None);

        Assert.Equal("run_1", run.Id);
        Assert.Equal("queued", run.Status);
        Assert.Equal("gpt-4o", run.Model);
        var req = Assert.Single(stub.Requests);
        Assert.Contains("\"assistant_id\":\"asst_1\"", req.Body);
        Assert.Contains("\"instructions\":\"Override\"", req.Body);
    }

    [Fact]
    public async Task PollRun_StopsOnTerminal()
    {
        var (api, stub) = Build();
        stub.MapSequential(HttpMethod.Get, "/v1/threads/thread_1/runs/run_1",
            (HttpStatusCode.OK, new { id = "run_1", status = "queued" }),
            (HttpStatusCode.OK, new { id = "run_1", status = "in_progress" }),
            (HttpStatusCode.OK, new { id = "run_1", status = "completed",
                usage = new { prompt_tokens = 200, completion_tokens = 50 },
                model = "gpt-4o" }));

        var run = await api.PollRunAsync("thread_1", "run_1", TimeSpan.FromSeconds(10), Cred(), CancellationToken.None);

        Assert.Equal("completed", run.Status);
        Assert.Equal(200, run.InputTokens);
        Assert.Equal(50, run.OutputTokens);
    }

    [Fact]
    public async Task PollRun_ParsesRequiresActionToolCalls()
    {
        var (api, stub) = Build();
        stub.Map(HttpMethod.Get, "/v1/threads/thread_1/runs/run_1", body: new
        {
            id = "run_1",
            status = "requires_action",
            required_action = new
            {
                submit_tool_outputs = new
                {
                    tool_calls = new[]
                    {
                        new
                        {
                            id = "call_1",
                            function = new { name = "transfer_to_human", arguments = "{\"reason\":\"loop\"}" },
                        },
                    },
                },
            },
        });

        var run = await api.PollRunAsync("thread_1", "run_1", TimeSpan.FromSeconds(2), Cred(), CancellationToken.None);

        Assert.Equal("requires_action", run.Status);
        var call = Assert.Single(run.ToolCalls);
        Assert.Equal("call_1", call.CallId);
        Assert.Equal("transfer_to_human", call.ToolName);
        Assert.Contains("\"reason\":\"loop\"", call.ArgumentsJson);
    }

    [Fact]
    public async Task SubmitToolOutputs_PostsCallIdsAndReturnsRun()
    {
        var (api, stub) = Build();
        stub.Map(HttpMethod.Post, "/v1/threads/thread_1/runs/run_1/submit_tool_outputs",
            body: new { id = "run_1", status = "in_progress" });

        var run = await api.SubmitToolOutputsAsync("thread_1", "run_1",
            new[] { new ToolOutput("call_1", "{\"success\":true}") }, Cred(), CancellationToken.None);

        Assert.Equal("in_progress", run.Status);
        var req = Assert.Single(stub.Requests);
        Assert.Contains("\"tool_call_id\":\"call_1\"", req.Body);
    }

    [Fact]
    public async Task GetLatestAssistantMessage_ExtractsTextContent()
    {
        var (api, stub) = Build();
        stub.Map(HttpMethod.Get, "/v1/threads/thread_1/messages", body: new
        {
            data = new[]
            {
                new
                {
                    role = "assistant",
                    content = new[]
                    {
                        new { type = "text", text = new { value = "Olá, posso ajudar?" } },
                    },
                },
            },
        });

        var text = await api.GetLatestAssistantMessageAsync("thread_1", "run_1", Cred(), CancellationToken.None);

        Assert.Equal("Olá, posso ajudar?", text);
    }

    [Fact]
    public async Task NonSuccessResponse_ThrowsOpenAiHttpException()
    {
        var (api, stub) = Build();
        stub.Map(HttpMethod.Post, "/v1/assistants", HttpStatusCode.Unauthorized,
            new { error = new { message = "Invalid key" } });

        var ex = await Assert.ThrowsAsync<OpenAiHttpException>(() =>
            api.EnsureAssistantAsync(BuildAgent(), Cred(), CancellationToken.None));
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task ServerError_ThrowsWith5xxStatus()
    {
        var (api, stub) = Build();
        stub.Map(HttpMethod.Post, "/v1/threads", HttpStatusCode.ServiceUnavailable, null);

        var ex = await Assert.ThrowsAsync<OpenAiHttpException>(() =>
            api.CreateThreadAsync(Cred(), CancellationToken.None));
        Assert.Equal(503, ex.StatusCode);
    }

    [Fact]
    public async Task Headers_IncludeOrgAndProject()
    {
        var (api, stub) = Build();
        stub.Map(HttpMethod.Post, "/v1/threads", body: new { id = "thread_1" });

        await api.CreateThreadAsync(Cred(), CancellationToken.None);

        var req = Assert.Single(stub.Requests);
        Assert.Equal("org_x", req.Headers.GetValueOrDefault("OpenAI-Organization"));
        Assert.Equal("proj_y", req.Headers.GetValueOrDefault("OpenAI-Project"));
        Assert.Equal("Bearer sk-test-key", req.Headers.GetValueOrDefault("Authorization"));
    }
}

internal sealed class SingleClientHttpFactory : IHttpClientFactory
{
    private readonly StubHttpMessageHandler _handler;
    public SingleClientHttpFactory(StubHttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
