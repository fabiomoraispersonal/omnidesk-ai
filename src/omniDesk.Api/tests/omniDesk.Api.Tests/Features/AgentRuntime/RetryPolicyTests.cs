using Microsoft.Extensions.Configuration;
using omniDesk.Api.Features.AgentRuntime;
using omniDesk.Api.Infrastructure.OpenAi;
using Xunit;

namespace omniDesk.Api.Tests.Features.AgentRuntime;

public class RetryPolicyTests
{
    private readonly RetryPolicy _policy = new(new ConfigurationBuilder().Build());

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(429)]
    public void Decide_5xxAndRateLimit_Retry(int status)
    {
        var ex = new OpenAiHttpException(status, "{}");
        Assert.Equal(RetryDecision.Retry, _policy.Decide(ex));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void Decide_AuthFailures_NoRetry(int status)
    {
        var ex = new OpenAiHttpException(status, "{}");
        Assert.Equal(RetryDecision.NoRetry, _policy.Decide(ex));
    }

    [Fact]
    public void Decide_TimeoutException_Retry()
    {
        Assert.Equal(RetryDecision.Retry, _policy.Decide(new TimeoutException()));
    }

    [Fact]
    public void Decide_TaskCanceled_Retry()
    {
        Assert.Equal(RetryDecision.Retry, _policy.Decide(new TaskCanceledException()));
    }

    [Fact]
    public void Decide_BadRequest_NoRetry()
    {
        Assert.Equal(RetryDecision.NoRetry, _policy.Decide(new OpenAiHttpException(400, "{}")));
    }

    [Theory]
    [InlineData(500, "http_5xx")]
    [InlineData(429, "rate_limit")]
    [InlineData(401, "http_4xx")]
    public void ClassifyError_TagsHttp(int status, string expected)
    {
        Assert.Equal(expected, RetryPolicy.ClassifyError(new OpenAiHttpException(status, "{}")));
    }

    [Fact]
    public void DefaultMaxRetries_Is_1()
    {
        Assert.Equal(1, _policy.MaxRetries);
    }

    [Fact]
    public void DefaultBackoff_Is_3Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), _policy.Backoff);
    }
}
