using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using omniDesk.Api.Features.AiSuggestions;
using omniDesk.Api.Infrastructure.Persistence;
using omniDesk.Api.Tests.Helpers;
using Xunit;

namespace omniDesk.Api.Tests.Features.AiSuggestions;

[Trait("Category", "Integration")]
public class SuggestReplyServiceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public SuggestReplyServiceTests(TestWebApplicationFactory factory) => _factory = factory;

    private sealed class StubAgentRuntime : IAgentRuntime
    {
        public SubAgentContext? SubAgent { get; init; }
        public IReadOnlyList<ConversationMessage> Messages { get; init; } = Array.Empty<ConversationMessage>();
        public Task<SubAgentContext?> GetSubAgentForDepartmentAsync(Guid departmentId, CancellationToken ct = default)
            => Task.FromResult(SubAgent);
        public Task<IReadOnlyList<ConversationMessage>> GetRecentMessagesAsync(Guid conversationId, int maxCount, CancellationToken ct = default)
            => Task.FromResult(Messages.Take(maxCount).ToArray() as IReadOnlyList<ConversationMessage>);
        public Task<string?> GetClientNameAsync(Guid conversationId, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class StubOpenAi(Func<IReadOnlyList<(string role, string content)>, AiCallResult> behaviour)
        : IOpenAiSuggestionClient
    {
        public IReadOnlyList<(string role, string content)>? LastPrompt { get; private set; }
        public Task<AiCallResult> CompleteAsync(IReadOnlyList<(string role, string content)> messages, TimeSpan timeout, CancellationToken ct)
        {
            LastPrompt = messages;
            return Task.FromResult(behaviour(messages));
        }
    }

    [Fact]
    public async Task BuildsPromptWithSubAgentAndRecentMessages()
    {
        var subAgent = new SubAgentContext(Guid.NewGuid(), "Suporte", "Você é o agente de Suporte.");
        var messages = new[]
        {
            new ConversationMessage("user", "olá", DateTimeOffset.UtcNow.AddMinutes(-3)),
            new ConversationMessage("assistant", "como posso ajudar?", DateTimeOffset.UtcNow.AddMinutes(-2)),
        };
        var prompt = SuggestReplyService.BuildPrompt(subAgent, messages);
        Assert.Equal("system", prompt[0].role);
        Assert.Contains("Suporte", prompt[0].content);
        Assert.Contains(prompt, p => p.role == "user" && p.content == "olá");
        Assert.Equal("user", prompt[^1].role);
    }

    [Fact]
    public async Task TruncatesSuggestionTextTo1000Chars()
    {
        var longText = new string('A', 1500);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<AiSuggestionLogger>();
        var openai = new StubOpenAi(_ => new AiCallResult(
            new OpenAiSuggestion(longText, "gpt-4o", 100, 50), AiProviderError.None));
        var service = new SuggestReplyService(
            new StubAgentRuntime(), openai, logger, db, NullLogger<SuggestReplyService>.Instance);

        var outcome = await service.SuggestAsync("slug",
            new SuggestionRequestContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20),
            default);

        Assert.NotNull(outcome.Response);
        Assert.Equal(SuggestReplyService.MaxSuggestionLength, outcome.Response!.Text.Length);
    }

    [Fact]
    public async Task TimeoutFromProvider_MapsToFailure()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<AiSuggestionLogger>();
        var openai = new StubOpenAi(_ => new AiCallResult(null, AiProviderError.Timeout));
        var service = new SuggestReplyService(
            new StubAgentRuntime(), openai, logger, db, NullLogger<SuggestReplyService>.Instance);

        var outcome = await service.SuggestAsync("slug",
            new SuggestionRequestContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20),
            default);

        Assert.Null(outcome.Response);
        Assert.Equal(SuggestionFailure.Timeout, outcome.Failure);
    }

    [Fact]
    public async Task RateLimitFromProvider_MapsToRateLimitFailure()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<AiSuggestionLogger>();
        var openai = new StubOpenAi(_ => new AiCallResult(null, AiProviderError.RateLimit));
        var service = new SuggestReplyService(
            new StubAgentRuntime(), openai, logger, db, NullLogger<SuggestReplyService>.Instance);

        var outcome = await service.SuggestAsync("slug",
            new SuggestionRequestContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20),
            default);

        Assert.Equal(SuggestionFailure.RateLimit, outcome.Failure);
    }
}
