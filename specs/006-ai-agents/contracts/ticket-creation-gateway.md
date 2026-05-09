# Contract — `ITicketCreationGateway` (interno, ponte para Spec 008)

Interface interna que o `ToolCallDispatcher` usa para criar tickets quando um agente aciona `transfer_to_human`. **Não** é um endpoint HTTP.

A Spec 006 entrega impl stub que insere registro mínimo em `tenant_{slug}.tickets` (a tabela já existe — `Add_Tickets_Scaffold.sql` da Spec 005). A Spec 008 substitui pela impl completa.

---

## C# Interface

```csharp
namespace omniDesk.Api.Features.AgentRuntime;

public interface ITicketCreationGateway
{
    Task<TicketHandoffResult> CreateTicketFromAiHandoffAsync(
        TicketHandoffRequest request,
        CancellationToken ct);
}

public record TicketHandoffRequest(
    string TenantSlug,
    Guid ThreadId,                     // AiThread.Id
    Guid DepartmentId,                 // resolvido pelo dispatcher
    string Reason,                     // do tool call
    Guid? OriginatingAgentId,          // null se vindo de api_error sem agente
    IReadOnlyList<ConversationMessage> History,
    string ExternalConversationRef);   // livechat:... ou whatsapp:...

public record TicketHandoffResult(
    Guid TicketId,
    string TicketNumber,               // ex.: "TKT-1042"
    string DepartmentName,
    string Status);                    // "queued" no V1
```

---

## Comportamento esperado da impl (contrato)

1. **Cria ticket** em `tenant_{slug}.tickets` com:
   - `id`: novo UUID.
   - `subject`: derivado do `Reason` (truncado em 255).
   - `department_id`: do request.
   - `status`: `'queued'`.
   - `assigned_attendant_id`: null (será atribuído pelo round-robin da Spec 005 quando essa entidade estiver provisionada com tickets reais).
   - `sla_started_at`: `now()`.
2. **Anexa histórico**: na Spec 006 stub, persiste o `History` em uma tabela transitional `tenant_{slug}.ai_handoff_snapshots` (jsonb). Na Spec 008 isso vira `messages` real do ticket.
3. **Retorna** `TicketHandoffResult` com id, número (sequence `ticket_number_seq` já criada na Spec 005) e nome do depto.

**Falhas**:
- Departamento inexistente/inativo → throw `DepartmentNotFoundException` (caller decide; o `ToolCallDispatcher` aciona fallback documentado em `cross-spec-pendencies.md`).
- Banco indisponível → exception bubble; a mensagem fica na fila Redis com retry padrão Hangfire.

---

## Eventos de notificação

A criação do ticket **dispara** evento Redis pub/sub `{slug}:ws:dept:{department_id}` payload:

```json
{
  "type": "ticket_created_from_ai",
  "ticket_id": "uuid",
  "ticket_number": "TKT-1042",
  "originating_agent_name": "Agente Comercial",
  "department_id": "uuid",
  "timestamp": "..."
}
```

Consumido pela Spec 005 (presença/distribuição) e pelas notificações da Spec 010 (futura). Spec 006 apenas **publica**; consumidores são responsabilidade das outras specs.

---

## Migration adicional para esta spec

`Add_Ai_Handoff_Snapshots.sql` (transitional, removida pela Spec 008):

```sql
CREATE TABLE IF NOT EXISTS {TENANT_SCHEMA}.ai_handoff_snapshots (
    id              uuid        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    ticket_id       uuid        NOT NULL,        -- FK lógica para tickets
    thread_id       uuid        NOT NULL REFERENCES {TENANT_SCHEMA}.ai_threads(id) ON DELETE RESTRICT,
    history_json    jsonb       NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_ai_handoff_snapshots_ticket
    ON {TENANT_SCHEMA}.ai_handoff_snapshots (ticket_id);
```

---

## Testes de contrato

- `StubTicketCreationGatewayTests.cs`:
  - Cria ticket com `status='queued'`, `subject` truncado, `sla_started_at` preenchido.
  - Anexa snapshot em `ai_handoff_snapshots`.
  - Publica evento Redis (verifica via `IDatabase.Multiplexer.GetSubscriber()`).
- `ITicketCreationGatewayBehaviorTests.cs` — testes vão sobreviver à substituição da Spec 008.
