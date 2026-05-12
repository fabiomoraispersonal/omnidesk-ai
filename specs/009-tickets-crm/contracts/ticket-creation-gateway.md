# Contract — `ITicketCreationGateway` (internal)

Contrato interno entre o `AgentOrchestrator` (Spec 006) e a camada de Tickets (esta spec). Substitui o `StubTicketCreationGateway` que apenas inseria linhas mínimas em `tickets`.

**Localização**: `src/omniDesk.Api/Features/AgentRuntime/ITicketCreationGateway.cs` (contrato — evoluído nesta spec) + `src/omniDesk.Api/Features/Tickets/TicketCreationGateway.cs` (implementação real, esta spec).

---

## Interface

```csharp
public interface ITicketCreationGateway
{
    Task<TicketHandoffResult> CreateTicketFromAiHandoffAsync(
        TicketHandoffRequest request,
        CancellationToken ct);
}

public record TicketHandoffRequest(
    Guid ConversationId,                          // FK → conversations(id)
    Guid ThreadId,                                // FK → ai_threads(id) — Spec 006
    Guid DepartmentId,                            // Vindo da tool call transfer_to_human
    string Reason,                                // Texto livre da IA
    Guid? OriginatingAgentId,                     // Sub-agente que disparou
    TicketChannel Channel,                        // live_chat | whatsapp | manual
    ContactHints? ContactHints,                   // Email/phone do visitor identificado
    string? SubjectSuggestion,                    // Primeiras 100 chars da última mensagem da IA
    IReadOnlyList<ConversationMessage> History,   // Para snapshot ai_handoff_snapshots
    string ExternalConversationRef);              // ID estável (channel-specific)

public record ContactHints(
    string? Email,
    string? Phone,
    string? Name);

public record TicketHandoffResult(
    Guid TicketId,
    string Protocol,                              // TK-YYYYMMDD-XXXXX
    Guid DepartmentId,
    string DepartmentName,
    Guid? AttendantId,                            // null se ficou na fila
    string Status,                                // "new" | "in_progress" (wire value)
    Guid? ContactId);                             // dedupado ou criado
```

---

## Semântica

Quando `AgentOrchestrator` decide handoff (ou cliente usa palavra-chave), invoca:

```csharp
var result = await _gateway.CreateTicketFromAiHandoffAsync(new TicketHandoffRequest(
    ConversationId: conv.Id,
    ThreadId: thread.Id,
    DepartmentId: targetDept.Id,
    Reason: "Cliente solicitou atendente humano",
    OriginatingAgentId: agent.Id,
    Channel: TicketChannel.LiveChat,
    ContactHints: new ContactHints(Email: visitor.Email, Phone: visitor.Phone, Name: visitor.Name),
    SubjectSuggestion: lastAiMessage.Content.Truncate(100),
    History: messages,
    ExternalConversationRef: conv.ExternalRef
), ct);
```

A implementação real (`TicketCreationGateway`) executa, em ordem, dentro de uma transação SQL:

1. **Resolução do contato** via `ContactDeduplicationService` (R9):
   - Se `ContactHints.Email` ou `.Phone` está preenchido → dedup + create-or-update.
   - Senão → `contact_id = null` (ticket sem contato identificado).
2. **Geração do protocolo** via `TicketProtocolService` (R1).
3. **Cálculo de SLA inicial** com base no `Department.SlaResolutionMinutes` → `sla_resolution_deadline = now() + N`.
4. **INSERT** em `tickets` com:
   - `status = new`, `priority = normal`, `subject = SubjectSuggestion ?? fallback`, `tags = []`.
   - `channel`, `conversation_id`, `contact_id`, `department_id` preenchidos.
   - `protocol`, `sla_resolution_deadline`, `created_at = now()`.
5. **Atribuição automática** chamando `TicketAssignmentService.AssignAsync(...)` (Spec 005, adaptado):
   - Se atendente disponível: `attendant_id = X`, `assigned_at = now()`, `sla_first_response_deadline = now() + sla_first_response_minutes`, `status = in_progress`.
   - Senão: ticket fica `new` na fila.
6. **Snapshot de histórico** em `ai_handoff_snapshots` (preserva a Spec 006).
7. **Update da conversa**: `conversations.ticket_id = ticket.id`, `conversations.status = transferred_to_human` (Spec 007).
8. **Eventos Mongo**:
   - `ticket_created` (sempre).
   - `attendant_assigned` (se atribuiu).
9. **Eventos WebSocket** (via `TicketEventPublisher`):
   - `ticket.created` em `{slug}:crm:dept:{department_id}` e `{slug}:crm:supervisor`.
   - `ticket.assigned` (se atribuiu).
10. **Notificações** (delegado à Spec 010): in-app para o atendente designado.
11. **Retorna** `TicketHandoffResult` para o `AgentOrchestrator` continuar (que enviará mensagem ao cliente: "Transferi você para um atendente, em instantes você será atendido.").

---

## Atomicidade

- **Pré-condição**: `conversations.id` e `departments.id` existem.
- **Garantia**: ou todos os passos 1–10 completam, ou nenhum. SQL via `BEGIN/COMMIT`. Mongo/Redis/Notificações são side-effects pós-commit (best-effort) — falha nesses passos é logada mas não reverte o ticket.

---

## Erros

| Código | Quando |
|---|---|
| `DEPARTMENT_NOT_FOUND` | `DepartmentId` não existe no tenant. |
| `CONVERSATION_NOT_FOUND` | `ConversationId` não existe ou pertence a outro tenant. |
| `CONVERSATION_ALREADY_HAS_TICKET` | `conversation.ticket_id` já está preenchido com outro ticket ativo. |
| `CONTACT_DEDUP_TIMEOUT` | Redis lock timeout 3s — fallback é tentar criar sem dedup (R9). |
| `PROTOCOL_GENERATION_FAILED` | Falha ao criar/usar sequence (`SERIALIZABLE` deadlock) — retry com backoff. |

---

## Migração do Stub

A interface `ITicketCreationGateway` muda de assinatura (adiciona `Channel`, `ContactHints`, `SubjectSuggestion`). Migração:

1. Spec 009 entrega o contrato evoluído + implementação `TicketCreationGateway`.
2. `Program.cs` muda registro DI: `services.AddScoped<ITicketCreationGateway, TicketCreationGateway>();` (era `StubTicketCreationGateway`).
3. `StubTicketCreationGateway` é movido para `Infrastructure/AgentRuntime/_Obsolete/` (mantido 1 sprint para rollback rápido). Em V1.1 deletado.
4. Chamadores em Spec 006 (`AgentOrchestrator.HandleHandoffAsync` etc.) atualizados para incluir os novos parâmetros — informação já está disponível no contexto (visitor → ContactHints; channel da conversa).

---

## Observabilidade

- Cada chamada produz log Serilog estruturado:
  ```
  TicketCreationGateway: created ticket {Protocol} in dept {DepartmentName}
  via handoff from agent {OriginatingAgentId} thread {ThreadId},
  attendant {AttendantId|fila}, contact {ContactId|anônimo}, channel {Channel}.
  Duration: {Ms}ms.
  ```
- Métricas (V1.1): contador `ticket_handoff_created_total{tenant, department, channel, assigned}`.
