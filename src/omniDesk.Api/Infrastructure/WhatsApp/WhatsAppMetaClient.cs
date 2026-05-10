using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace omniDesk.Api.Infrastructure.WhatsApp;

/// <summary>
/// Typed client da Meta Cloud Graph API (v19.0). Encapsula envio de mensagens,
/// submissão/consulta de templates, e download de mídia. Retry exponencial
/// inline (zero NuGet novo) — apenas 5xx e timeout. 4xx incluindo 401/403
/// nunca retentam (research R8).
///
/// Spec 008 / contracts/whatsapp-meta-graph.md.
/// </summary>
public sealed class WhatsAppMetaClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
    };

    private readonly HttpClient _http;
    private readonly ILogger<WhatsAppMetaClient> _logger;

    public WhatsAppMetaClient(HttpClient http, ILogger<WhatsAppMetaClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    // ---------- Send ----------

    public Task<MetaSendResponse> SendTextAsync(
        string phoneNumberId, string accessToken, string toE164, string body, CancellationToken ct)
    {
        var path = string.Format(MetaApi.Paths.Messages, phoneNumberId);
        var payload = new
        {
            messaging_product = "whatsapp",
            recipient_type    = "individual",
            to                = toE164,
            type              = "text",
            text              = new { preview_url = false, body }
        };
        return PostAndParseAsync<MetaSendResponse>(path, accessToken, payload, ParseSend, ct);
    }

    public Task<MetaSendResponse> SendTemplateAsync(
        string phoneNumberId, string accessToken, string toE164,
        TemplateSendPayload tpl, CancellationToken ct)
    {
        var path = string.Format(MetaApi.Paths.Messages, phoneNumberId);
        var payload = new
        {
            messaging_product = "whatsapp",
            to                = toE164,
            type              = "template",
            template = new
            {
                name     = tpl.TemplateName,
                language = new { code = tpl.Language },
                components = new[]
                {
                    new
                    {
                        type       = "body",
                        parameters = tpl.Parameters.Select(p => new { type = p.Type, text = p.Text }).ToArray()
                    }
                }
            }
        };
        return PostAndParseAsync<MetaSendResponse>(path, accessToken, payload, ParseSend, ct);
    }

    // ---------- Templates ----------

    public Task<MetaTemplateSubmissionResponse> SubmitTemplateAsync(
        string wabaId, string accessToken, TemplateSubmissionPayload payload, CancellationToken ct)
    {
        var path = string.Format(MetaApi.Paths.MessageTemplates, wabaId);
        var body = new
        {
            name       = payload.Name,
            category   = payload.Category,
            language   = payload.Language,
            components = payload.Components.Select(c => new
            {
                type    = c.Type,
                text    = c.Text,
                example = c.Example is null ? null : new { body_text = c.Example.BodyText }
            }).ToArray()
        };

        return PostAndParseAsync<MetaTemplateSubmissionResponse>(
            path,
            accessToken,
            body,
            json =>
            {
                var resp = JsonSerializer.Deserialize<SubmitTemplateApiResponse>(json, JsonOpts)
                    ?? throw new InvalidOperationException("Empty Meta submit-template response");
                return new MetaTemplateSubmissionResponse(resp.Id, resp.Status);
            },
            ct);
    }

    public async Task<MetaTemplateStatusInfo?> GetTemplateStatusAsync(
        string wabaId, string accessToken, string templateName, CancellationToken ct)
    {
        var path = string.Format(MetaApi.Paths.MessageTemplates, wabaId) + $"?name={Uri.EscapeDataString(templateName)}";

        return await GetAndParseAsync<MetaTemplateStatusInfo?>(
            path,
            accessToken,
            json =>
            {
                var resp = JsonSerializer.Deserialize<GetTemplateStatusApiResponse>(json, JsonOpts);
                var item = resp?.Data?.FirstOrDefault(d => d.Name == templateName);
                return item is null ? null : new MetaTemplateStatusInfo(item.Name, item.Language, item.Status, item.Id);
            },
            ct);
    }

    // ---------- Media ----------

    public Task<MetaMediaInfo> GetMediaInfoAsync(string mediaId, string accessToken, CancellationToken ct)
    {
        var path = string.Format(MetaApi.Paths.Media, mediaId);
        return GetAndParseAsync<MetaMediaInfo>(
            path,
            accessToken,
            json =>
            {
                var info = JsonSerializer.Deserialize<MediaInfoApiResponse>(json, JsonOpts)
                    ?? throw new InvalidOperationException("Empty Meta media info response");
                return new MetaMediaInfo(info.Url, info.MimeType, info.Sha256, info.FileSize, info.Id);
            },
            ct);
    }

    public async Task<byte[]> DownloadMediaBytesAsync(string url, string accessToken, CancellationToken ct)
    {
        return await ExecuteWithRetryAsync<byte[]>(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
            if (!resp.IsSuccessStatusCode)
                await ThrowMetaErrorAsync(resp, ct);
            return await resp.Content.ReadAsByteArrayAsync(ct);
        }, "DownloadMediaBytes", ct);
    }

    // ---------- Token validation ----------

    public async Task<bool> ValidateAccessTokenAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, MetaApi.Paths.Me);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Meta /me probe falhou — assumindo token inválido.");
            return false;
        }
    }

    // ---------- Internals ----------

    private async Task<T> PostAndParseAsync<T>(
        string path, string accessToken, object body, Func<string, T> parse, CancellationToken ct)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(body, options: JsonOpts)
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var resp = await _http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                ThrowMetaError(raw, (int)resp.StatusCode);
            return parse(raw);
        }, $"POST {path}", ct);
    }

    private async Task<T> GetAndParseAsync<T>(
        string path, string accessToken, Func<string, T> parse, CancellationToken ct)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var resp = await _http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                ThrowMetaError(raw, (int)resp.StatusCode);
            return parse(raw);
        }, $"GET {path}", ct);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action, string operation, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (MetaApiException) // 4xx — não retenta
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && attempt < RetryDelays.Length)
            {
                _logger.LogWarning(ex,
                    "Meta API {Operation} falhou (tentativa {Attempt}); retentando em {Delay}.",
                    operation, attempt + 1, RetryDelays[attempt]);
                await Task.Delay(RetryDelays[attempt], ct);
            }
        }
    }

    private static async Task ThrowMetaErrorAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var raw = await resp.Content.ReadAsStringAsync(ct);
        ThrowMetaError(raw, (int)resp.StatusCode);
    }

    private static void ThrowMetaError(string body, int statusCode)
    {
        try
        {
            var env = JsonSerializer.Deserialize<MetaErrorEnvelope>(body, JsonOpts);
            if (env?.Error is { } e)
                throw new MetaApiException(e.Code, e.Message, e.FbTraceId, statusCode);
        }
        catch (JsonException) { /* fallthrough */ }

        throw new MetaApiException(0, $"Unexpected Meta response: {body}", null, statusCode);
    }

    private static MetaSendResponse ParseSend(string json)
    {
        var resp = JsonSerializer.Deserialize<SendApiResponse>(json, JsonOpts)
            ?? throw new InvalidOperationException("Empty Meta send response");
        var first = resp.Messages?.FirstOrDefault()
            ?? throw new InvalidOperationException("Meta send response had no messages[]");
        return new MetaSendResponse(first.Id);
    }

    /// <summary>Status codes que disparam retry quando lançados via HttpRequestException.</summary>
    public static readonly IReadOnlySet<HttpStatusCode> RetryableStatuses = new HashSet<HttpStatusCode>
    {
        HttpStatusCode.RequestTimeout,           // 408
        HttpStatusCode.InternalServerError,      // 500
        HttpStatusCode.BadGateway,               // 502
        HttpStatusCode.ServiceUnavailable,       // 503
        HttpStatusCode.GatewayTimeout,           // 504
    };
}
