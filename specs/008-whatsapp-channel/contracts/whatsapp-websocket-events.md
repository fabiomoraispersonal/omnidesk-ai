# Contract — WhatsApp WebSocket Events (CRM)

**Audience**: backend → CRM Angular via `/ws/crm` (Spec 007). Esta spec **não cria** novo endpoint WS — apenas adiciona 3 eventos novos no canal CRM.

**Canal Pub/Sub**: `{slug}:crm:dept:{department_id}` (já existente Spec 007). Eventos broadcast em fan-out — todos os atendentes do departamento dono da conversa recebem.

**Constantes**: `Hubs/Events/WhatsAppCrmEvents.cs`:

```csharp
public static class WhatsAppCrmEvents
{
    public const string WaMessageStatus    = "wa.message_status";
    public const string WaSessionExpiring  = "wa.session_expiring";
    public const string WaSessionExpired   = "wa.session_expired";
}
```

---

## 1. `wa.message_status`

**Quando**: status update Meta processado por `WaWebhookProcessorJob.HandleStatusAsync`, OU envio outbound completou (status `sent`), OU mídia foi baixada (status `attachment_ready`).

**Payload**:

```json
{
  "type": "wa.message_status",
  "payload": {
    "conversation_id": "uuid",
    "message_id": "uuid",
    "wa_message_id": "wamid.HBgL...",
    "status": "sent",
    "timestamp": "2026-05-10T14:32:11Z",
    "error_code": null,
    "error_message": null,
    "attachment_ready": false
  }
}
```

**Variantes do `status`**:
- `sent` — emitido pelo OutgoingAdapter após sucesso na Graph API.
- `delivered` — webhook Meta status=delivered.
- `read` — webhook Meta status=read.
- `failed` — webhook Meta status=failed OU Polly esgotou retries.

**`attachment_ready: true`** acompanha `status` (qualquer) quando uma mensagem com mídia teve `messages.attachment_url` preenchido pelo `WaMediaDownloadJob`.

**Frontend**: `crm-websocket.service.ts` mantém um Map `messageId → WaStatus` em signal; o componente `conversation-detail.component.ts` consume e renderiza ícone:
- `sent` → `✓`
- `delivered` → `✓✓`
- `read` → `✓✓` azul
- `failed` → `✗` com tooltip = `error_message`

---

## 2. `wa.session_expiring`

**Quando**: `WaSessionExpiringNotifierJob` (cron */5 min) detecta `wa_session_expires_at` entre `now()` e `now() + 1h` para conversa `open`.

**Idempotência**: Redis flag `{slug}:wa:expiring_emitted:{conversation_id}` TTL 1h impede repetição.

**Payload**:

```json
{
  "type": "wa.session_expiring",
  "payload": {
    "conversation_id": "uuid",
    "expires_at": "2026-05-10T15:32:11Z",
    "minutes_remaining": 47
  }
}
```

**Frontend**: `conversation-detail.component.ts` mostra banner amarelo:
> ⚠️ A janela de 24h da Meta expira em 47 min. Após isso, será necessário usar template.

Banner persiste até `wa.session_expired` ou nova mensagem do cliente reabrir a janela.

---

## 3. `wa.session_expired`

**Quando**: `WaSessionExpiringNotifierJob` detecta `wa_session_expires_at < now()` para conversa `open` que ainda não recebeu este evento.

**Idempotência**: Redis flag `{slug}:wa:expired_emitted:{conversation_id}` TTL 24h.

**Payload**:

```json
{
  "type": "wa.session_expired",
  "payload": {
    "conversation_id": "uuid",
    "expired_at": "2026-05-10T16:32:11Z"
  }
}
```

**Frontend**:
- Banner muda de amarelo para vermelho:
  > 🚫 A janela de 24h expirou. Selecione um template para enviar.
- Input de texto livre fica disabled.
- Botão "Selecionar template" aparece — abre `template-picker-dialog.component.ts`.

---

## 4. Reabertura da janela

Quando o cliente envia nova mensagem **após** janela expirada, `WhatsAppIncomingAdapter.HandleMessageAsync` faz:

```
1. wa_session_expires_at = now() + 24h.
2. Limpa Redis flags expiring_emitted e expired_emitted.
3. Broadcast `chat.message_received` (já existente Spec 007).
4. (Opcional V1.1) emit `wa.session_resumed` com novo expires_at.
```

V1: o frontend infere reabertura observando `chat.message_received` em conversa cujo banner mostrava "expirada" → re-fetch da conversation para atualizar `wa_session_expires_at` e remove banner. Simples e suficiente.

---

## 5. Eventos NÃO incluídos nesta spec

**Já existentes Spec 007** (reuso):
- `chat.new_conversation` — quando WhatsApp abre nova conv (broadcast em routing por departamento).
- `chat.message_received` — toda nova mensagem do cliente (texto ou mídia).
- `chat.visitor_typing` — Meta não envia signal de typing do cliente; **apenas LiveChat** dispara este; WhatsApp não dispara em V1.
- `chat.browser_notify` — push notification visual (reuso).

**Não emitidos em V1**:
- `wa.template_status_changed` — UX considera `polling refresh manual` aceitável; WS opcional V1.1.
- `wa.session_resumed` — frontend infere via `chat.message_received` (item 4 acima).

---

## 6. Frontend — assinatura

`crm-websocket.service.ts` ganha 3 handlers novos:

```typescript
// crm-websocket.service.ts (extensão Spec 007)
private readonly waMessageStatus$ = new Subject<WaMessageStatusEvent>();
private readonly waSessionExpiring$ = new Subject<WaSessionExpiringEvent>();
private readonly waSessionExpired$ = new Subject<WaSessionExpiredEvent>();

// Switch novo no message handler
case WhatsAppCrmEvents.WaMessageStatus:
    this.waMessageStatus$.next(event.payload);
    break;
case WhatsAppCrmEvents.WaSessionExpiring:
    this.waSessionExpiring$.next(event.payload);
    break;
case WhatsAppCrmEvents.WaSessionExpired:
    this.waSessionExpired$.next(event.payload);
    break;
```

`conversation-detail.component.ts` consome via signals:

```typescript
readonly waStatusByMessageId = signal<Map<string, WaStatus>>(new Map());
readonly sessionWindow = signal<{ status: 'active' | 'expiring' | 'expired'; expiresAt?: Date }>({ status: 'active' });

constructor() {
    effect(() => {
        this.crmWs.waMessageStatus$.subscribe(e => {
            const next = new Map(this.waStatusByMessageId());
            next.set(e.message_id, { status: e.status, errorMessage: e.error_message, attachmentReady: e.attachment_ready });
            this.waStatusByMessageId.set(next);
        });
        this.crmWs.waSessionExpiring$.subscribe(e => this.sessionWindow.set({ status: 'expiring', expiresAt: new Date(e.expires_at) }));
        this.crmWs.waSessionExpired$.subscribe(_ => this.sessionWindow.set({ status: 'expired' }));
    });
}
```

---

## 7. Testes de contrato

`tests/Features/WhatsApp/Jobs/WaSessionExpiringNotifierJobTests.cs`:

| Cenário | Assert |
|---|---|
| Conv com `wa_session_expires_at` em now+30min | `wa.session_expiring` emit; flag Redis set |
| Conv já flagged em Redis | Sem reemit |
| Conv com `wa_session_expires_at` em now-5min | `wa.session_expired` emit; flag set |
| Conv com `channel=live_chat` | Ignorada (filtro index parcial) |
| Conv com `status=resolved` | Ignorada |

`tests/Hubs/WaCrmEventsBroadcastTests.cs`:

| Cenário | Assert |
|---|---|
| Status delivered chega via webhook | Cliente WS no canal `{slug}:crm:dept:1` recebe `wa.message_status` |
| OutgoingAdapter envia text → 200 Meta | Cliente WS recebe `wa.message_status` status=sent |
