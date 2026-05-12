namespace omniDesk.Api.Features.WhatsApp.Webhook;

/// <summary>
/// Captura o raw body do request e armazena em <c>HttpContext.Items["RawBody"]</c>
/// para que o handler do webhook possa validar a assinatura HMAC-SHA256
/// (<c>X-Hub-Signature-256</c>) sobre os bytes brutos antes do model binding.
///
/// Aplicado **apenas** em rotas <c>/api/public/whatsapp/webhook/*</c> via
/// <see cref="UseWhen"/> em <c>Program.cs</c>.
///
/// Spec 008 / contracts/whatsapp-webhook.md / research R4.
/// </summary>
public sealed class RawBodyCaptureMiddleware
{
    public const string RawBodyKey = "RawBody";

    private readonly RequestDelegate _next;

    public RawBodyCaptureMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // Apenas POST tem body relevante para HMAC; GET (verify) não precisa.
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await _next(context);
            return;
        }

        context.Request.EnableBuffering();

        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms);
        var raw = ms.ToArray();
        context.Items[RawBodyKey] = raw;

        // Rebobina para que model binding possa ler novamente.
        context.Request.Body.Position = 0;

        await _next(context);
    }
}
