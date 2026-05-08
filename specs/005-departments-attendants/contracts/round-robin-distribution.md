# Contract: Round-robin Distribution + Lock

Algoritmo de atribuição automática de tickets aos atendentes elegíveis, com lock atômico para evitar atribuição duplicada.

## Entrada

`AssignTicketRequest`:

```csharp
public record AssignTicketRequest(
    Guid TicketId,
    Guid DepartmentId,
    AssignmentReason Reason);

public enum AssignmentReason { AiHandoff, AttendantReleased, ManualPickup, Transfer }
```

## Saída

`AssignmentResult`:

```csharp
public record AssignmentResult(
    AssignmentOutcome Outcome,
    Guid? AssignedAttendantId,
    QueueReason? QueueReason);

public enum AssignmentOutcome { Assigned, Queued }
public enum QueueReason {
    NoAttendantsOnline,
    AllAtCapacity,
    OutsideBusinessHoursNoOneOnline
}
```

## Algoritmo

```text
Function AssignTicket(ticketId, departmentId):

  1. Lock global: acquire = SET {slug}:ticket_lock:{ticketId} {pid} NX EX 10
     If acquire == nil: return Outcome.Queued, no_change (someone else is handling)

  Try:

    2. eligible = Query(
        attendants
          JOIN attendant_departments ON ...
          JOIN attendant_status_redis ON ...
        WHERE department_id = departmentId
          AND attendants.is_active = true
          AND attendant_status_redis.status = 'online'
          AND attendants.active_ticket_count < attendants.max_simultaneous_chats
        ORDER BY attendants.id ASC)  // ordering deterministic for round-robin

    3. If eligible is empty:
        evaluator = BusinessHoursEvaluator(department.business_hours)
        reason = match {
            evaluator.IsAvailable(now) AND no_one_online: NoAttendantsOnline
            !evaluator.IsAvailable(now) AND no_one_online: OutsideBusinessHoursNoOneOnline
            online_but_at_capacity: AllAtCapacity
        }
        Return (Outcome.Queued, reason)

    4. cursor = INCR {slug}:rr:{departmentId}     // atomic
       EXPIRE {slug}:rr:{departmentId} 3600
       chosenIdx = (cursor - 1) % len(eligible)
       chosen = eligible[chosenIdx]

    5. UPDATE tickets SET assigned_attendant_id = chosen.id, assigned_at = now() WHERE id = ticketId
       AtomicIncrement chosen.active_ticket_count

    6. Publish ticket.assigned event:
        - channel: {slug}:ws:attendant:{chosen.id}
        - channel: {slug}:ws:dept:{departmentId}

    7. Return (Outcome.Assigned, chosen.id)

  Finally:
    DEL {slug}:ticket_lock:{ticketId}     // release lock
```

## Notas críticas

### Lock atômico (FR-016, SC-002)

`SET key val NX EX 10` é a única primitiva. Não há retry — se o lock falha, outra requisição **já está atribuindo** o ticket; aceitamos como "ok" e retornamos `Queued` (a requisição que tem o lock vai gerar a atribuição correta). Para o caso de "Assumir manualmente" o caller decide: mostra mensagem "ticket sendo atribuído por outro atendente" se quiser.

### Cursor mod len (R1)

`INCR` é atômico — duas requisições simultâneas pegam valores consecutivos (`N` e `N+1`). Mesmo que a lista mude entre as duas, ambas escolhem positions distintas com alta probabilidade. Para listas pequenas (2–5 atendentes) o `mod` mantém distribuição justa em rajadas.

### TTL do cursor

`EXPIRE 3600` (1 h). Após inatividade longa (sem tickets), o cursor é descartado e a próxima rajada começa de 0 — comportamento aceito (premissa A10).

### Atomicidade do active_ticket_count

`active_ticket_count` é mantido em coluna desnormalizada em `attendants` para evitar SUM em hot path. Atualização via `UPDATE attendants SET active_ticket_count = active_ticket_count + 1` (SQL atômico). Decremento ocorre quando o ticket é fechado/transferido.

### Quando o status do atendente muda durante atribuição?

Cenário: dois tickets entram quase ao mesmo tempo. O atendente A é elegível em t0 mas fica `away` em t0+1ms. A requisição para o segundo ticket pode ler a lista incluindo A; ao chegar no UPDATE, o lock detecta `chosen.active_ticket_count + 1 > max`? Não — esse caso é checado no passo 2 (filtro). Se o atendente A já estava no limite quando o passo 2 rodou, A não está em `eligible`. Cenários de corrida fina (ms entre filtro e UPDATE) são tratados pelo lock — apenas uma das duas requisições obtém o lock e atualiza; a outra aceita como concluída.

## Atribuição manual ("Assumir")

Mesma lógica de lock, mas pula round-robin:

```text
Function ManualPickup(ticketId, attendantId):
  acquire = SET {slug}:ticket_lock:{ticketId} {attendantId} NX EX 10
  If !acquire: return AlreadyTaken
  Try:
    current = SELECT assigned_attendant_id FROM tickets WHERE id = ticketId
    If current != null AND current != attendantId:
      // Confirmação visual no frontend; backend aceita override
      // (UI exibe "Este ticket está com [X]. Deseja assumir?")
      Return RequiresConfirmation
    Update + emit events (transferred if had owner; assigned if was queued)
  Finally:
    DEL lock
```

## Cobertura por testes (Spec 005 — Phase 2)

| FR/SC | Teste |
|---|---|
| FR-013, FR-014, SC-003 | `RoundRobinCursorTests` — 100 tickets/N atendentes → diff ≤ 1 |
| FR-015 | `TicketAssignmentService.QueuesWhenNoEligible` |
| FR-016, SC-002 | `TicketLockTests.RejectsConcurrentAcquisitions` (50 pares) |
| FR-018 | `TicketAssignmentService.SkipsAtCapacity` |
| FR-027–030 | `BusinessHoursEvaluatorTests` (4 cenários da matriz de transbordo) |
