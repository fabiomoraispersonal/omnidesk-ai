using System.Security.Cryptography;
using System.Text;
using Hangfire;
using omniDesk.Api.Infrastructure.WhatsApp;
using StackExchange.Redis;

namespace omniDesk.Api.Features.WhatsApp.Webhook;

/// <summary>
/// Endpoints públicos do webhook Meta WhatsApp. Sem autenticação de usuário —
/// validação por verify_token (GET) e HMAC-SHA256 (POST).
/// Spec 008 / contracts/whatsapp-webhook.md.
/// </summary>
public static class WhatsAppWebhookEndpoints
{
    public static IEndpointRouteBuilder MapWhatsAppWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/public/whatsapp/webhook/{slug}", VerifyAsync)
            .WithName("WaWebhookVerify");

        app.MapPost("/api/public/whatsapp/webhook/{slug}", ReceiveAsync)
            .WithName("WaWebhookReceive");

        return app;
    }

    /// <summary>
    /// GET handshake da Meta. Devolve <c>hub.challenge</c> em texto plano se
    /// <c>hub.verify_token</c> bate com <c>whatsapp_config.webhook_verify_token</c>.
    /// </summary>
    private static async Task<IResult> VerifyAsync(
        string slug,
        HttpContext ctx,
        WaWebhookTenantResolver resolver,
        CancellationToken ct)
    {
        var mode      = ctx.Request.Query[MetaApi.Hub.Mode].ToString();
        var token     = ctx.Request.Query[MetaApi.Hub.VerifyToken].ToString();
        var challenge = ctx.Request.Query[MetaApi.Hub.Challenge].ToString();

        if (!string.Equals(mode, MetaApi.Hub.ModeSubscribe, StringComparison.Ordinal))
            return Results.StatusCode(403);

        var resolved = await resolver.ResolveAsync(slug, ct);
        if (resolved is null) return Results.NotFound();

        var providedBytes = Encoding.UTF8.GetBytes(token);
        var expectedBytes = Encoding.UTF8.GetBytes(resolved.WebhookVerifyToken);

        if (providedBytes.Length != expectedBytes.Length) return Results.StatusCode(403);
        if (!CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
            return Results.StatusCode(403);

        return Results.Text(challenge, "text/plain");
    }

    /// <summary>
    /// POST recepção de mensagens + status updates. Valida HMAC, dedup em Redis,
    /// enfileira <see cref="WaWebhookProcessorJob"/> em background, retorna 200 OK
    /// imediatamente (Meta timeout 20s; SLO interno ≤ 5s — FR-007).
    /// </summary>
    private static async Task<IResult> ReceiveAsync(
        string slug,
        HttpContext ctx,
        WaWebhookTenantResolver resolver,
        MetaWebhookSignatureValidator validator,
        IConnectionMultiplexer redis,
        IBackgroundJobClient jobs,
        ILogger<MetaWebhookSignatureValidator> logger,
        CancellationToken ct)
    {
        var rawBody = ctx.Items[RawBodyCaptureMiddleware.RawBodyKey] as byte[];
        if (rawBody is null || rawBody.Length == 0)
        {
            logger.LogWarning("WaWebhookMissingRawBody: tenant={Slug}", slug);
            return Results.StatusCode(403);
        }

        var resolved = await resolver.ResolveAsync(slug, ct);
        if (resolved is null) return Results.NotFound();

        // Canal desativado: 200 OK silently dropped (Meta exige 200; só descarta).
        if (!resolved.IsEnabled)
        {
            logger.LogDebug("WaWebhookChannelDisabled: tenant={Slug} dropping payload", slug);
            return Results.Ok();
        }

        // HMAC-SHA256 validation.
        var headerSig = ctx.Request.Headers[MetaApi.Headers.HubSignature256].ToString();
        var appSecretBytes = WaWebhookTenantResolver.GetAppSecretBytes(resolved);

        if (!validator.Validate(headerSig, rawBody, appSecretBytes))
        {
            logger.LogWarning("WaWebhookSignatureInvalid: tenant={Slug}", slug);
            return Results.StatusCode(403);
        }

        // Dedup por wa_message_id quando possível (parse parcial barato — falha tolerada).
        var dedupKey = TryExtractDedupKey(rawBody);
        if (!string.IsNullOrEmpty(dedupKey))
        {
            var firstSeen = await redis.GetDatabase().StringSetAsync(
                RedisKeys.WaDedup(slug, dedupKey),
                "1",
                TimeSpan.FromHours(24),
                When.NotExists);

            if (!firstSeen)
            {
                logger.LogInformation("WaWebhookDedupHit: tenant={Slug} key={Key}", slug, dedupKey);
                return Results.Ok();
            }
        }

        // Enfileira processamento async.
        jobs.Enqueue<WaWebhookProcessorJob>(j =>
            j.ProcessAsync(slug, resolved.TenantId, rawBody, CancellationToken.None));

        return Results.Ok();
    }

    /// <summary>
    /// Extrai o primeiro <c>wa_message_id</c> ou <c>statuses[].id</c> do payload bruto
    /// para dedup. Parse parcial barato — tolera erro (retorna null).
    /// </summary>
    private static string? TryExtractDedupKey(byte[] rawBody)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            if (!root.TryGetProperty("entry", out var entry) || entry.ValueKind != System.Text.Json.JsonValueKind.Array)
                return null;

            foreach (var e in entry.EnumerateArray())
            {
                if (!e.TryGetProperty("changes", out var changes)) continue;
                foreach (var c in changes.EnumerateArray())
                {
                    if (!c.TryGetProperty("value", out var value)) continue;

                    if (value.TryGetProperty("messages", out var messages)
                        && messages.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var m in messages.EnumerateArray())
                        {
                            if (m.TryGetProperty("id", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.String)
                                return id.GetString();
                        }
                    }

                    if (value.TryGetProperty("statuses", out var statuses)
                        && statuses.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var s in statuses.EnumerateArray())
                        {
                            if (s.TryGetProperty("id", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.String)
                                return $"st:{id.GetString()}";
                        }
                    }

                    if (value.TryGetProperty("message_template_id", out var tplId))
                    {
                        return $"tpl:{tplId.GetRawText()}";
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed payload — caller will still validate HMAC and pass to processor for logging.
            return null;
        }

        return null;
    }
}
