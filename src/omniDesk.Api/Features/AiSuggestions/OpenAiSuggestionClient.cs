using System.Net.Http.Json;
using System.Text.Json;

namespace omniDesk.Api.Features.AiSuggestions;

/// <summary>
/// Minimal direct REST client for OpenAI chat completions. We avoid pulling in the full
/// `OpenAI` SDK at this layer — Spec 002 owns the SDK integration; for the suggestion path
/// we only need a thin call. When `OPENAI_API_KEY` is unset, the client returns a deterministic
/// stub useful for dev/test.
/// </summary>
public class OpenAiSuggestionClient : IOpenAiSuggestionClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string? _apiKey;
    private readonly string _model;

    public OpenAiSuggestionClient(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? config["OpenAI:ApiKey"];
        _model = config["OpenAI:Model"] ?? "gpt-4o";
    }

    public async Task<AiCallResult> CompleteAsync(
        IReadOnlyList<(string role, string content)> messages,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            // Dev fallback — never used in production because the configuration check fails earlier.
            await Task.Yield();
            return new AiCallResult(
                new OpenAiSuggestion("Sugestão de exemplo (sem OPENAI_API_KEY).", _model, 0, 0),
                AiProviderError.None);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var http = _httpFactory.CreateClient("openai");
        http.BaseAddress = new Uri("https://api.openai.com/");
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        var payload = new
        {
            model = _model,
            messages = messages.Select(m => new { role = m.role, content = m.content }).ToArray(),
            max_tokens = 256,
            temperature = 0.4,
        };

        try
        {
            var resp = await http.PostAsJsonAsync("v1/chat/completions", payload, cts.Token);
            if ((int)resp.StatusCode == 429) return new AiCallResult(null, AiProviderError.RateLimit);
            if ((int)resp.StatusCode >= 500) return new AiCallResult(null, AiProviderError.ServerError);
            if (!resp.IsSuccessStatusCode) return new AiCallResult(null, AiProviderError.ServerError);

            using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            var root = doc.RootElement;
            var text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
            var usage = root.TryGetProperty("usage", out var u) ? u : default;
            var input = usage.ValueKind == JsonValueKind.Undefined ? 0 : usage.GetProperty("prompt_tokens").GetInt32();
            var output = usage.ValueKind == JsonValueKind.Undefined ? 0 : usage.GetProperty("completion_tokens").GetInt32();
            return new AiCallResult(new OpenAiSuggestion(text, _model, input, output), AiProviderError.None);
        }
        catch (TaskCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new AiCallResult(null, AiProviderError.Timeout);
        }
        catch (HttpRequestException)
        {
            return new AiCallResult(null, AiProviderError.ServerError);
        }
    }
}
