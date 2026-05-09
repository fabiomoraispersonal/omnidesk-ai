using Microsoft.Extensions.Logging.Abstractions;
using omniDesk.Api.Domain.AiAgents;
using omniDesk.Api.Infrastructure.OpenAi;
using Xunit;

namespace omniDesk.Api.Tests.Smoke;

/// <summary>
/// Live smoke against OpenAI Assistants v2 — runs only when OPENAI_API_KEY is set
/// AND when the test runner explicitly enables openai-live (default .runsettings
/// excludes it). Justification: ADR-006-001.
///
/// Run locally:  dotnet test --filter "openai-live=true"
/// Suggested model: gpt-4o-mini for cost.
/// </summary>
[Trait("openai-live", "true")]
public class OpenAiLiveSmoke
{
    [Fact]
    public async Task EndToEnd_CreateAssistant_RunSimplePrompt()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // Skip via Assert.Skip not available in xUnit 2.x; emit assertion failure with context.
            Assert.Fail("OPENAI_API_KEY not set — skip by leaving filter excluding openai-live.");
            return;
        }

        var http = new HttpClientFactory();
        var api = new AssistantsApi(http, NullLogger<AssistantsApi>.Instance);
        var creds = new OpenAiCredentials(apiKey,
            Environment.GetEnvironmentVariable("OPENAI_ORGANIZATION"),
            Environment.GetEnvironmentVariable("OPENAI_PROJECT"),
            "live-smoke");
        var agent = new AiAgent
        {
            Id = Guid.NewGuid(),
            Type = AgentType.Orchestrator,
            Name = "SmokeBot",
            Prompt = "You are a smoke test bot. Reply with exactly the word 'pong'.",
            Model = "gpt-4o-mini",
        };

        var assistantId = await api.EnsureAssistantAsync(agent, creds, CancellationToken.None);
        Assert.StartsWith("asst_", assistantId);

        try
        {
            var threadId = await api.CreateThreadAsync(creds, CancellationToken.None);
            await api.AppendUserMessageAsync(threadId, "ping", creds, CancellationToken.None);

            var run = await api.CreateRunAsync(threadId, assistantId,
                instructionsOverride: null, creds, CancellationToken.None);
            run = await api.PollRunAsync(threadId, run.Id, TimeSpan.FromSeconds(60),
                creds, CancellationToken.None);

            Assert.Equal("completed", run.Status);
            var reply = await api.GetLatestAssistantMessageAsync(threadId, run.Id, creds, CancellationToken.None);
            Assert.False(string.IsNullOrEmpty(reply));

            await api.DeleteThreadAsync(threadId, creds, CancellationToken.None);
        }
        finally
        {
            // Best-effort assistant cleanup.
            try
            {
                using var http2 = new HttpClient();
                http2.BaseAddress = new Uri("https://api.openai.com/");
                http2.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                http2.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");
                await http2.DeleteAsync($"v1/assistants/{assistantId}");
            }
            catch
            {
                // Cleanup is best-effort; live test is opt-in and rare.
            }
        }
    }

    private sealed class HttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
