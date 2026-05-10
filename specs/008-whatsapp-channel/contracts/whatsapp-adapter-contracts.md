# Contract — WhatsApp Channel Adapter

**Audience**: implementação interna `Features/WhatsApp/Adapters/*`. Cumpre os contratos `IIncomingChannelAdapter` e `IOutgoingChannelAdapter` definidos pela Spec 006 (`AgentOrchestrator`) e estendidos pela Spec 007 (`LiveChat*Adapter` referência).

**Princípio**: §III Channel Agnosticism — adapter traduz eventos do canal em modelos internos canal-agnósticos. Zero modificação no `AgentOrchestrator`, `IncomingMessageWorker`, `OutgoingMessageWorker`.

---

## 1. `WhatsAppIncomingAdapter`

### Responsabilidade

Converter um payload Meta (`messages[]` ou `statuses[]`) em uma sequência de operações idempotentes contra o domain.

### Interface

```csharp
public interface IIncomingChannelAdapter  // contrato Spec 006
{
    Task ProcessIncomingAsync(string tenantSlug, IncomingChannelEvent evt, CancellationToken ct);
}

public sealed class WhatsAppIncomingAdapter : IIncomingChannelAdapter
{
    public WhatsAppIncomingAdapter(
        ITenantContextSetter tenantContextSetter,
        IVisitorRepository visitorRepo,
        IConversationRepository conversationRepo,
        IMessageRepository messageRepo,
        IWaMessageStatusesRepository statusRepo,
        IIncomingMessageQueue incomingQueue,         // Spec 006 fila Hangfire
        IBackgroundJobClient hangfire,               // para WaMediaDownloadJob
        ICrmWebSocketBroadcaster wsBroadcaster,      // Spec 007 /ws/crm
        TimeProvider clock,
        ILogger<WhatsAppIncomingAdapter> logger);

    public async Task ProcessIncomingAsync(string tenantSlug, IncomingChannelEvent evt, CancellationToken ct)
    {
        _tenantContextSetter.Set(tenantSlug);

        switch (evt)
        {
            case WhatsAppMessageEvent msg:
                await HandleMessageAsync(msg, ct);
                break;
            case WhatsAppStatusEvent st:
                await HandleStatusAsync(st, ct);
                break;
            case WhatsAppTemplateStatusEvent ts:
                await HandleTemplateStatusAsync(ts, ct);
                break;
        }
    }
}
```

### `HandleMessageAsync` — fluxo

```
1. Find or create visitor by metadata.wa_phone == msg.From
   - Visitor.Name = msg.ContactName ?? msg.From
2. Find or create open conversation by:
   - WHERE channel='whatsapp' AND wa_contact_phone = msg.From AND status = 'open'
   - Se não existe: criar com status='open', channel='whatsapp', visitor_id, wa_contact_phone
3. Update conversation.wa_session_expires_at = clock.UtcNow + 24h
4. Persist message:
   - sender_type = 'visitor'
   - content_type por tipo (text/image/file)
   - content = msg.Text (se texto)
   - metadata = {
       wa_message_id: msg.Id,
       wa_attachment_status: msg.HasMedia ? 'pending' : null,
       wa_attachment_meta_id: msg.MediaId,
       wa_attachment_filename: msg.Filename
     }
5. Para mídia: hangfire.Enqueue<WaMediaDownloadJob>(j => j.RunAsync(msg.Id, msg.MediaId, ...))
6. Enfileira IncomingMessage (modelo agnóstico Spec 006) em incomingQueue
7. Broadcast WS event 'chat.new_conversation' (se nova) ou 'chat.message_received'
   (reuso do CRM events da Spec 007)
```

### Idempotência

Garantida pela dedup Redis no controller (`{slug}:wa:dedup:{wa_message_id}` 24h) — adapter não precisa re-checar.

### `HandleStatusAsync`

```
1. Persist em MongoDB {slug}_wa_message_statuses (find or insert by wa_message_id+status)
2. Resolve message_id local via index unique em messages.metadata->>'wa_message_id'
3. Broadcast WS 'wa.message_status' { conversation_id, message_id, status, error_code?, error_message?, timestamp }
```

### `HandleTemplateStatusAsync`

```
1. Find template by meta_template_id ?? name (a primeira aprovação preenche meta_template_id)
2. If event=APPROVED: status='approved', approved_at=now(), meta_template_id=event.MessageTemplateId
3. If event=REJECTED: status='rejected', rejected_at=now(), rejection_reason=event.Reason
4. Broadcast WS event (opcional V1.1) 'wa.template_status_changed'
```

---

## 2. `WhatsAppOutgoingAdapter`

### Responsabilidade

Consumir `OutgoingMessage` da fila `{slug}:outgoing_messages` (Spec 006) e enviar via Meta Graph API.

### Interface

```csharp
public sealed class WhatsAppOutgoingAdapter : IOutgoingChannelAdapter
{
    public WhatsAppOutgoingAdapter(
        IWhatsAppConfigRepository configRepo,
        IMessageRepository messageRepo,
        IConversationRepository conversationRepo,
        IWhatsAppTemplateRepository templateRepo,
        ISessionWindowGuard windowGuard,
        IWaOutgoingGuard outgoingGuard,
        WhatsAppMetaClient metaClient,
        AesGcmEncryptionService crypto,
        IBackgroundJobClient hangfire,
        ILogger<WhatsAppOutgoingAdapter> logger);

    public bool CanHandle(string channel) => channel == "whatsapp";

    public async Task SendAsync(OutgoingMessage msg, CancellationToken ct);
}
```

### Fluxo

```
1. Carregar conversation (get wa_contact_phone, wa_session_expires_at).
2. WaOutgoingGuard.Validate(msg, conversation) — 422 se IA tentando template.
3. SessionWindowGuard.Validate(conversation, msg.MessageType):
    - Se text e expired → throw WaWindowExpiredException → mensagem fica `failed`,
      atendente é notificado via WS para enviar template.
    - Se template → ok independentemente da janela.
4. Carregar whatsapp_config + decifrar access_token.
5. Build payload Graph API:
    - text: { type: text, text: { body: msg.Content } }
    - template: { type: template, template: { name, language, components.parameters } }
6. metaClient.SendAsync(...) com Polly retry.
7. Em sucesso:
    - Update messages.metadata.wa_message_id = response.messages[0].id
    - Persist em MongoDB {slug}_wa_message_statuses (status=sent)
    - Broadcast WS 'wa.message_status' status=sent
8. Em falha 401 (code 190):
    - Mark message failed
    - Hangfire.Enqueue<WaTokenRevokedDetectorJob>(slug, message_id)
    - rethrow para Hangfire retry NÃO acontecer (configurar AutomaticRetry(Attempts = 0) no método)
9. Em falha 5xx: deixar Polly retentar (3x); após esgotar → mensagem failed, broadcast WS.
```

### Falhas conhecidas

| Erro | Tratamento |
|---|---|
| `ChannelNotEnabled` (config.is_enabled=false) | Silently drop (mensagem fica em `pending` no DB; reativação manual reenvia) |
| `WindowExpired` (text fora janela) | `failed` + WS notify atendente |
| `TemplateNotApproved` | 422 antes de chegar aqui (validação no endpoint /send) |
| `TokenRevoked` (190) | Detector job + canal off |
| `TimeoutException` Polly esgotou | `failed` + log warn |

---

## 3. `LiveChatConversationGateway` ↔ `WhatsApp`

A Spec 007 implementou `LiveChatConversationGateway` (impl real de `IConversationGateway` da Spec 006). Este gateway é canal-agnóstico — `AgentOrchestrator` chama métodos genéricos:

- `GetActiveConversationAsync(visitor_id, channel)` — retorna conv aberta no canal.
- `CreateConversationAsync(visitor_id, channel, metadata)` — cria nova.
- `MarkAsResolvedAsync(conversation_id, ended_by)` — encerra.

**Não há `WhatsAppConversationGateway`** — `LiveChatConversationGateway` opera sobre tabela `conversations` (canal-agnóstica), filtrando por `channel`. Esta spec **renomeia mentalmente** o gateway para `ConversationGateway` mas mantém o tipo concreto da Spec 007 (refactor de nome é V1.1).

---

## 4. Registro DI

Em `Program.cs`:

```csharp
builder.Services.AddScoped<IIncomingChannelAdapter, WhatsAppIncomingAdapter>(); // (multiple registration ok — resolve por canal)
builder.Services.AddScoped<IOutgoingChannelAdapter, WhatsAppOutgoingAdapter>();
builder.Services.AddSingleton<AesGcmEncryptionService>();
builder.Services.AddSingleton<MetaWebhookSignatureValidator>();
builder.Services.AddHttpClient<WhatsAppMetaClient>("WhatsAppGraph", client =>
{
    client.BaseAddress = new Uri(configuration["WhatsApp:GraphApiBaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(10);
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetTimeoutPolicy());
```

`OutgoingMessageDispatcher` (Spec 006) seleciona adapter por `channel`:

```csharp
public class OutgoingMessageDispatcher
{
    private readonly IEnumerable<IOutgoingChannelAdapter> _adapters;

    public Task DispatchAsync(OutgoingMessage msg, CancellationToken ct)
    {
        var adapter = _adapters.FirstOrDefault(a => a.CanHandle(msg.Channel))
            ?? throw new InvalidOperationException($"No adapter for channel {msg.Channel}");
        return adapter.SendAsync(msg, ct);
    }
}
```

---

## 5. Testes de contrato

`tests/Features/WhatsApp/Adapters/WhatsAppIncomingAdapterTests.cs`:

| Cenário | Assert |
|---|---|
| Mensagem texto de visitante novo | Visitor criado com `metadata.wa_phone`; conversation criada `channel=whatsapp`; message persistida; `wa_session_expires_at` ≈ now+24h; IncomingMessage enfileirado |
| Mensagem texto em conversa existente | Visitor reusado; conversation reusada; nova message; `wa_session_expires_at` atualizado |
| Mensagem image | message com `metadata.wa_attachment_status='pending'`; WaMediaDownloadJob enfileirado |
| Status delivered | MongoDB updated; WS `wa.message_status` broadcast |
| Template approval webhook | template.status='approved', approved_at preenchido |
| Template rejection webhook | template.status='rejected', rejection_reason preenchido |

`tests/Features/WhatsApp/Adapters/WhatsAppOutgoingAdapterTests.cs`:

| Cenário | Assert |
|---|---|
| Texto dentro janela | Meta API chamado com payload text; wa_message_id persistido |
| Texto fora janela (sender=attendant) | `WaWindowExpiredException`; message status=failed |
| Template (sender=attendant) | Meta API chamado com payload template; wa_message_id persistido |
| Template (sender=ai_agent) | `WaOutgoingGuardException`; bloqueado antes da Meta |
| 401 Meta | Message failed; `WaTokenRevokedDetectorJob` enfileirado; sem retry |
| 503 Meta | Polly retenta 3x; depois marca failed |
