using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace OmniDesk.Api.Infrastructure.Security;

public record TurnstileResult(bool Success, string? FailureReason = null)
{
    public static TurnstileResult Ok() => new(true);
    public static TurnstileResult Fail(string reason) => new(false, reason);
}

internal record TurnstileApiResponse(
    [property: JsonPropertyName("success")]      bool Success,
    [property: JsonPropertyName("challenge_ts")] string? ChallengeTimestamp,
    [property: JsonPropertyName("hostname")]     string? Hostname,
    [property: JsonPropertyName("error-codes")]  string[]? ErrorCodes
);

public interface ITurnstileService
{
    Task<TurnstileResult> VerifyAsync(string token, string? remoteIp = null,
        CancellationToken ct = default);
}

public sealed class TurnstileService(HttpClient http, IConfiguration config) : ITurnstileService
{
    private const string Endpoint = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    public async Task<TurnstileResult> VerifyAsync(string token, string? remoteIp = null,
        CancellationToken ct = default)
    {
        var secret = config["TURNSTILE_SECRET_KEY"]
            ?? throw new InvalidOperationException("TURNSTILE_SECRET_KEY is not configured.");

        var form = new Dictionary<string, string>
        {
            ["secret"]   = secret,
            ["response"] = token,
        };

        if (!string.IsNullOrEmpty(remoteIp))
            form["remoteip"] = remoteIp;

        try
        {
            using var resp = await http.PostAsync(
                Endpoint, new FormUrlEncodedContent(form), ct);

            if (!resp.IsSuccessStatusCode)
                return TurnstileResult.Fail($"siteverify returned {(int)resp.StatusCode}");

            var body = await resp.Content.ReadFromJsonAsync<TurnstileApiResponse>(ct);
            if (body is null)
                return TurnstileResult.Fail("empty response from siteverify");

            return body.Success
                ? TurnstileResult.Ok()
                : TurnstileResult.Fail(string.Join(", ", body.ErrorCodes ?? []));
        }
        catch (Exception ex)
        {
            return TurnstileResult.Fail(ex.Message);
        }
    }
}
