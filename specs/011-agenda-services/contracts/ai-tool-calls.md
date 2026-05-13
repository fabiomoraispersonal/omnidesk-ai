# Contract: AI Tool Calls — Agenda

**Spec**: 011-agenda-services
**Depends on**: Spec 006 (Agentes IA) — `IToolRegistry`, `AgentOrchestrator`.

Two new tool calls registered with the OpenAI Assistants API runtime per tenant. Both share the same backend services used by the REST endpoints (single source of truth — research §R1).

---

## Tool 1: `check_availability`

### OpenAI tool definition

```json
{
  "type": "function",
  "function": {
    "name": "check_availability",
    "description": "Consulta horários disponíveis de um profissional para um serviço específico em uma data. Use antes de propor horários ao cliente. Retorna lista vazia se o profissional estiver inativo, não oferecer o serviço, ou não tiver slots livres no dia.",
    "parameters": {
      "type": "object",
      "properties": {
        "professional_id": {
          "type": "string",
          "description": "UUID do profissional"
        },
        "service_id": {
          "type": "string",
          "description": "UUID do serviço — define a duração do slot"
        },
        "date": {
          "type": "string",
          "description": "Data no formato YYYY-MM-DD (interpretada no fuso horário do tenant)"
        }
      },
      "required": ["professional_id", "service_id", "date"]
    }
  }
}
```

### Response (returned to the model as tool output)

```json
{
  "professional_id": "p1...",
  "service_id": "s1...",
  "date": "2026-06-10",
  "duration_minutes": 45,
  "slots": [
    { "start_at": "2026-06-10T09:00:00-03:00", "end_at": "2026-06-10T09:45:00-03:00" },
    { "start_at": "2026-06-10T09:45:00-03:00", "end_at": "2026-06-10T10:30:00-03:00" }
  ]
}
```

Empty list responses follow the same conditions as `GET /api/availability` (see `availability-api.md`).

### Error responses

```json
{ "error": "PROFESSIONAL_NOT_FOUND" }
{ "error": "SERVICE_NOT_FOUND" }
{ "error": "INVALID_DATE_FORMAT" }
```

(Returned as tool output JSON; the model is instructed to handle gracefully and offer to clarify with the client.)

---

## Tool 2: `create_appointment`

### OpenAI tool definition

```json
{
  "type": "function",
  "function": {
    "name": "create_appointment",
    "description": "Cria um agendamento confirmado ou pendente de confirmação (depende do tipo de cliente e da configuração do serviço). Use APENAS após ter consultado disponibilidade e o cliente ter escolhido um horário específico. NÃO use para reagendar — para reagendar, transfira para humano.",
    "parameters": {
      "type": "object",
      "properties": {
        "professional_id": { "type": "string", "description": "UUID do profissional" },
        "service_id":      { "type": "string", "description": "UUID do serviço" },
        "start_at":        { "type": "string", "description": "ISO 8601 com timezone, ex.: 2026-06-10T09:00:00-03:00" },
        "client_name":     { "type": "string", "description": "Nome do cliente (usado para criar contato se ainda não existir)" },
        "client_phone":    { "type": "string", "description": "Telefone E.164, ex.: +5511999998888" },
        "client_type":     {
          "type": "string",
          "enum": ["new_client", "returning_client"],
          "description": "Tipo do cliente — APENAS para coerência narrativa. O backend recalcula autoritativamente."
        }
      },
      "required": ["professional_id", "service_id", "start_at", "client_name", "client_phone"]
    }
  }
}
```

### Response

```json
{
  "appointment_id": "a1...",
  "status": "confirmed",
  "client_type": "new_client",
  "start_at": "2026-06-10T09:00:00-03:00",
  "end_at": "2026-06-10T09:45:00-03:00",
  "requires_confirmation": false,
  "message_to_client": "Agendamento criado para 10/06/2026 às 09:00 com Dra. Ana Lima."
}
```

`message_to_client` is a server-rendered suggestion; the model decides whether to use it verbatim or paraphrase.

### Error responses

```json
{ "error": "APPOINTMENT_SLOT_CONFLICT", "message": "Slot já reservado — consulte disponibilidade novamente." }
{ "error": "APPOINTMENT_OUTSIDE_AVAILABILITY", "message": "Horário fora da disponibilidade do profissional." }
{ "error": "PROFESSIONAL_DOES_NOT_OFFER_SERVICE", "message": "Este profissional não oferece este serviço." }
{ "error": "PROFESSIONAL_NOT_FOUND" }
{ "error": "SERVICE_NOT_FOUND" }
```

When the model receives these errors, it MUST:

1. Re-check availability for the same date (or offer adjacent dates).
2. Inform the client clearly.
3. NOT retry the same `(professional_id, service_id, start_at)` triple — the slot is taken.

---

## Backend authoritativeness (FR-020)

When `create_appointment` is invoked:

1. Backend **discards** the `client_type` parameter sent by the model.
2. Backend resolves contact via E.164 phone:
   - If contact exists: use existing `contact_id`.
   - If not: create a new contact with `name = client_name`, `phone = client_phone`, `created_via = ai`.
3. Backend computes `client_type` autoritatively via `ClientTypeResolver.ResolveAsync(contactId, ct)` (research §R5).
4. Backend computes `status` per FR-021 (new_client OR requires_confirmation → pending; else confirmed).
5. Backend sets `created_by = "ai"`, `conversation_id` from `IAgentContext.ConversationId`, `ticket_id` if conversation has open ticket.

The model's narrative may say "vou marcar como cliente novo" but if the backend determines otherwise, the response payload reflects the authoritative `client_type`. The model is instructed to defer to the response.

---

## Registration

```csharp
// Infrastructure/AgentRuntime/ToolRegistry.cs (extended)

public sealed class ToolRegistry : IToolRegistry
{
    public IEnumerable<ITool> GetEnabledToolsForTenant(string tenantSlug, AgentSettings settings)
    {
        // ...existing tools (handoff_to_agent, transfer_to_human, etc.)...

        if (settings.AgendaEnabled)  // tenant opt-in flag (Spec 006 ai_settings)
        {
            yield return _serviceProvider.GetRequiredService<CheckAvailabilityTool>();
            yield return _serviceProvider.GetRequiredService<CreateAppointmentTool>();
        }
    }
}
```

Spec 006 stores `agenda_enabled` boolean per tenant in `ai_settings` (already extensible; this spec just consumes it).

---

## Audit & observability

Every tool invocation appends to `{slug}_agent_activity_logs`:

```json
{
  "action": "tool_call",
  "tool_name": "check_availability" | "create_appointment",
  "arguments": { /* full args */ },
  "result": "success" | "error",
  "error_code": "...",  // if applicable
  "appointment_id": "...",  // for create_appointment success
  "duration_ms": 320,
  "conversation_id": "..."
}
```

Metrics emitted:

- `ai_tool_calls_total{tool, outcome}` (Counter)
- `ai_tool_duration_seconds{tool}` (Histogram)

---

## Implementation notes

- `Features/Agenda/Tools/CheckAvailabilityTool.cs` — implements `ITool`. `ExecuteAsync(args, context, ct)` parses args, calls `IAvailabilityCalculator`, returns JSON.
- `Features/Agenda/Tools/CreateAppointmentTool.cs` — calls `IContactRepository` (find-or-create by phone), then `CreateAppointmentCommand`. `client_type` discarded; `created_by = "ai"` set explicitly.
- Both tools resolve `IAgentContext` (Spec 006) for `conversation_id`, `tenant_slug`, `tenant_timezone`.
- Validation: same FluentValidation rules as REST POST, plus E.164 phone format for `client_phone`.
