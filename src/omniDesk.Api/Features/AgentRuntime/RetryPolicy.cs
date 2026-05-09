using omniDesk.Api.Infrastructure.OpenAi;

namespace omniDesk.Api.Features.AgentRuntime;

public enum RetryDecision
{
    Retry,
    NoRetry,
}

public class RetryPolicy
{
    private readonly IConfiguration _config;

    public RetryPolicy(IConfiguration config) => _config = config;

    public int MaxRetries => _config.GetValue<int>("Ai:RunMaxRetries", 1);
    public TimeSpan Backoff => TimeSpan.FromSeconds(_config.GetValue<int>("Ai:RunRetryBackoffSeconds", 3));
    public TimeSpan RunTimeout => TimeSpan.FromSeconds(_config.GetValue<int>("Ai:RunTimeoutSeconds", 30));

    public RetryDecision Decide(Exception ex)
    {
        return ex switch
        {
            OpenAiHttpException http => DecideHttp(http.StatusCode),
            TimeoutException => RetryDecision.Retry,
            HttpRequestException => RetryDecision.Retry,
            TaskCanceledException => RetryDecision.Retry,
            _ => RetryDecision.NoRetry,
        };
    }

    public static RetryDecision DecideHttp(int status) => status switch
    {
        401 or 403 => RetryDecision.NoRetry,
        429 => RetryDecision.Retry,
        >= 500 => RetryDecision.Retry,
        _ => RetryDecision.NoRetry,
    };

    public static string ClassifyError(Exception ex) => ex switch
    {
        OpenAiHttpException http when http.StatusCode == 401 || http.StatusCode == 403 => "http_4xx",
        OpenAiHttpException http when http.StatusCode == 429 => "rate_limit",
        OpenAiHttpException http when http.StatusCode >= 500 => "http_5xx",
        OpenAiHttpException => "http_4xx",
        TimeoutException => "timeout",
        TaskCanceledException => "timeout",
        HttpRequestException => "http_5xx",
        _ => "unknown",
    };
}
