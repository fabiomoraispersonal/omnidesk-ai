using System.Text.Json;
using Hangfire;
using omniDesk.Api.Features.WhatsApp.Adapters;
using omniDesk.Api.Infrastructure.AgentRuntime;
using omniDesk.Api.Infrastructure.WhatsApp;

namespace omniDesk.Api.Features.WhatsApp.Webhook;

/// <summary>
/// Hangfire job que processa o payload bruto do webhook WhatsApp em background.
/// Mantido async para o controller responder 200 OK em ≤ 5s (FR-007 / SC-001 — Meta
/// timeout 20s). Spec 008 research R2.
/// </summary>
public sealed class WaWebhookProcessorJob
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly TenantContextHolder _tenantContext;
    private readonly WhatsAppIncomingAdapter _incoming;
    private readonly IWaMessageStatusesRepository _statusRepo;
    private readonly WaTemplateStatusHandler _templateStatusHandler;
    private readonly ILogger<WaWebhookProcessorJob> _logger;

    public WaWebhookProcessorJob(
        TenantContextHolder tenantContext,
        WhatsAppIncomingAdapter incoming,
        IWaMessageStatusesRepository statusRepo,
        WaTemplateStatusHandler templateStatusHandler,
        ILogger<WaWebhookProcessorJob> logger)
    {
        _tenantContext = tenantContext;
        _incoming = incoming;
        _statusRepo = statusRepo;
        _templateStatusHandler = templateStatusHandler;
        _logger = logger;
    }

    [Queue("wa-webhook")]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ProcessAsync(string tenantSlug, Guid tenantId, byte[] rawPayload, CancellationToken ct)
    {
        _tenantContext.Set(tenantSlug, tenantId);

        WaWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WaWebhookPayload>(rawPayload, JsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "WaWebhookPayloadInvalid: tenant={Slug} bytes={Length}", tenantSlug, rawPayload.Length);
            return; // Meta exige 200 OK; já retornado pelo controller. Aqui apenas dropamos.
        }

        if (payload?.Entry is null) return;

        foreach (var entry in payload.Entry)
        {
            if (entry.Changes is null) continue;
            foreach (var change in entry.Changes)
            {
                switch (change.Field)
                {
                    case MetaApi.WebhookFields.Messages:
                        await _incoming.HandleMessagesChangeAsync(tenantId, tenantSlug, change, ct);

                        // Persist status updates em MongoDB (FR-019).
                        if (change.Value.Statuses is { } statuses)
                        {
                            foreach (var st in statuses)
                            {
                                await _statusRepo.InsertAsync(tenantSlug, new WaMessageStatusEntry(
                                    MessageId:      Guid.Empty, // resolvido pelo adapter para WS broadcast
                                    WaMessageId:    st.Id,
                                    ConversationId: Guid.Empty,
                                    Status:         WaMessageStatusFromString(st.Status),
                                    ErrorCode:      st.Errors?.FirstOrDefault()?.Code.ToString(),
                                    ErrorMessage:   st.Errors?.FirstOrDefault()?.Message,
                                    RecipientId:    st.RecipientId,
                                    Timestamp:      ParseUnix(st.Timestamp) ?? DateTimeOffset.UtcNow), ct);
                            }
                        }
                        break;

                    case MetaApi.WebhookFields.MessageTemplateStatusUpdate:
                        await _templateStatusHandler.HandleAsync(tenantId, tenantSlug, change.Value, ct);
                        break;

                    default:
                        _logger.LogInformation(
                            "WaWebhookFieldIgnored: tenant={Slug} field={Field}",
                            tenantSlug, change.Field);
                        break;
                }
            }
        }
    }

    private static omniDesk.Api.Domain.WhatsApp.WaMessageStatus WaMessageStatusFromString(string s) =>
        s switch
        {
            "sent"      => omniDesk.Api.Domain.WhatsApp.WaMessageStatus.Sent,
            "delivered" => omniDesk.Api.Domain.WhatsApp.WaMessageStatus.Delivered,
            "read"      => omniDesk.Api.Domain.WhatsApp.WaMessageStatus.Read,
            "failed"    => omniDesk.Api.Domain.WhatsApp.WaMessageStatus.Failed,
            _           => omniDesk.Api.Domain.WhatsApp.WaMessageStatus.Failed,
        };

    private static DateTimeOffset? ParseUnix(string? s) =>
        string.IsNullOrEmpty(s) ? null
            : long.TryParse(s, out var t) ? DateTimeOffset.FromUnixTimeSeconds(t) : null;
}
