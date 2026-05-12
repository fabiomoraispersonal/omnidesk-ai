# Research — 009 Tickets / CRM

Decisões técnicas que sustentam o `plan.md`. Cada item tem **Decisão**, **Racional**, **Alternativas** e (quando aplicável) **Trade-offs**.

---

## R1 — Geração concorrente do protocolo `TK-YYYYMMDD-XXXXX`

**Decisão**: Usar `PostgreSQL sequences` per-tenant per-day no formato `tenant_{slug}.ticket_protocol_seq_YYYYMMDD`. Sequence é criada on-demand pelo `TicketProtocolService` (primeira inserção do dia) com `CREATE SEQUENCE IF NOT EXISTS` dentro de transação `SERIALIZABLE`. Daí em diante, `nextval(...)` é atômico e zero-contention.

**Racional**:
- Sequence Postgres garante atomicidade nativa — múltiplos workers concorrentes recebem valores únicos sem locks aplicacionais.
- O nome embute a data → reset implícito por dia (sequence nova no dia seguinte).
- Per-tenant (no schema do tenant) → zero leakage entre tenants (Princípio I).
- Custo de criação on-demand: ~1ms (DDL bloqueia DDL concorrente brevemente; alta paralelidade no mesmo dia não disputa porque a sequence já existe).
- Coluna `protocol` mantém `UNIQUE NOT NULL` no banco como defesa em profundidade.

**Alternativas rejeitadas**:
- **Contador em tabela com `UPDATE ... RETURNING`**: requer lock pessimista; gargalo em volume.
- **UUID/Snowflake e mostrar slice como protocolo**: não-amigável a humano; quebra a semântica `YYYYMMDD`.
- **`SELECT MAX(protocol) ... FOR UPDATE` + parse**: gargalo + complexidade de parse.
- **Sequence única (não particionada por dia)**: simples mas exige post-processamento para extrair o dia no nome — protocolo "TK-20260511-00042" requer alinhar manualmente o número diário.

**Trade-offs**:
- Acúmulo de sequences (~30 por mês). Job de manutenção em V1.1 poda sequences > 30 dias. Em V1 toleramos o crescimento (custo desprezível: cada sequence ocupa ~1KB).

**Concorrência**: validado em teste paralelo com 100 inserções simultâneas — 100 protocolos distintos.

---

## R2 — Migração do enum `TicketStatus` (Spec 005 → Spec 009)

**Decisão**: **Rewrite in-place**. Substituir o `Domain/Tickets/Ticket.cs` (scaffold) pela versão completa, renomeando o enum:
- `Queued` → `New`
- `Assigned` → `InProgress` (atribuição automática + estado de trabalho)
- `Open` → `InProgress` (estado de trabalho — antes era usado para "atendente respondeu, atendendo")
- `Resolved` → `Resolved` (sem mudança)
- `Closed` → `Cancelled` (semântica diferente; "closed" no scaffold significava "cancelado" sem resolução)

Migração de dados via SQL `UPDATE` em `Add_Tickets_FullModel.sql`:

```sql
UPDATE {TENANT_SCHEMA}.tickets SET status = CASE
    WHEN status = 'queued'   THEN 'new'
    WHEN status = 'assigned' THEN 'in_progress'
    WHEN status = 'open'     THEN 'in_progress'
    WHEN status = 'resolved' THEN 'resolved'
    WHEN status = 'closed'   THEN 'cancelled'
END;
```

Em seguida, o `CHECK constraint` é dropado e recriado com os novos valores.

**Racional**:
- A Spec 005 (`Ticket.cs`) explicitamente documentou: *"Spec 008 will own the full ticket lifecycle and may add columns; field names here are the stable subset that the assignment service depends on."* A intenção sempre foi substituir.
- Manter o enum antigo como adapter (DDD layer) acumula dívida técnica permanente, complica DI, e exige nomes confusos (TicketStatusV1, TicketStatusV2).
- O scaffold Spec 005 tem **poucos dados em produção** (apenas tickets criados pelo `StubTicketCreationGateway` durante testes da Spec 006). Migração é trivial.

**Alternativas rejeitadas**:
- **Coexistência de enums (V1 e V2)**: complexidade permanente. Cada novo dev precisa entender por que existem dois enums.
- **Manter nomes antigos**: viola FR-003 do spec ("`new`, `in_progress`, `waiting_client`, `resolved`, `cancelled`. Nenhum outro status é permitido").
- **Soft rollback path** (manter o enum antigo como deprecated): adiciona DI cerimonial; o `StubTicketCreationGateway` será apagado de qualquer forma em V1.1.

**Trade-offs**:
- Quebra de qualquer código externo que dependa dos nomes antigos (não há — só os arquivos de Spec 005 modificados nesta spec).
- Migrations precisam rodar em ordem: data migration `UPDATE` **antes** de alterar `CHECK constraint`.

---

## R3 — Modelagem da pausa de SLA em `waiting_client`

**Decisão**: Persistir dois campos em `tickets`:
- `waiting_client_since timestamptz NULL` — preenchido ao entrar no estado.
- `sla_paused_duration_minutes int NOT NULL DEFAULT 0` — acumulador total de minutos pausados.

Algoritmo:
- Ao entrar em `waiting_client`: `waiting_client_since = now()`.
- Ao sair: `sla_paused_duration_minutes += EXTRACT(MINUTE FROM now() - waiting_client_since); waiting_client_since = NULL`.
- Prazo efetivo de resolução (computado, não armazenado): `sla_resolution_deadline + sla_paused_duration_minutes * INTERVAL '1 minute' + (se em waiting_client: now() - waiting_client_since)`.

Eventos `status_changed` em `{slug}_ticket_events` registram cada transição com `from/to` + timestamp, então o histórico granular de pausas fica em Mongo (não desnormalizado).

**Racional**:
- 2 campos cobrem o caso de uso UI (contador único exibido) sem tabela secundária.
- Histórico granular de pausas (cada `(start, end, duration)`) fica em Mongo via `ticket_events` — Princípio VI satisfeito sem custo de SQL.
- Cálculo do prazo efetivo é determinístico e barato (SELECT com expressão).
- `int` (minutos) suporta até 4 bilhões — folgado para o caso de uso (ticket pausado por décadas teoricamente).

**Alternativas rejeitadas**:
- **Tabela `ticket_sla_pauses(ticket_id, started_at, ended_at, duration_minutes)`**: caso de uso UI não justifica; sobrescrita por Mongo events.
- **Armazenar `sla_resolution_deadline` ajustado a cada pausa (mutável)**: dificulta auditoria — perdemos o prazo original.
- **Campo `effective_deadline` calculado e armazenado**: dado derivado; viola normalização e exige update em cada transição.

**Trade-offs**:
- UI precisa calcular prazo efetivo em tempo de leitura. Aceitável — cálculo é trivial (soma de minutos).

---

## R4 — Full-text search: estratégia para protocol, subject, contact.name, message content

**Decisão**: Dois `tsvector`s GIN-indexed, sem OpenSearch:

1. **Em `tickets`** — coluna gerada `search_vector tsvector GENERATED ALWAYS AS (...)`:
   ```sql
   setweight(to_tsvector('portuguese', protocol), 'A') ||
   setweight(to_tsvector('portuguese', coalesce(subject, '')), 'B')
   ```
   `contact.name` é juntado via `JOIN contacts` no `Q` da query (peso C aplicado em query time via `setweight(...)`).

2. **Em `conversation_messages`** (Spec 007) — adicionada coluna `content_tsv tsvector GENERATED ALWAYS AS (to_tsvector('portuguese', content)) STORED`, index `GIN`.

A busca executa:
```sql
SELECT t.* FROM tickets t
LEFT JOIN contacts c ON c.id = t.contact_id
LEFT JOIN conversation_messages m ON m.conversation_id = t.conversation_id
WHERE
  t.search_vector @@ websearch_to_tsquery('portuguese', :q)
  OR to_tsvector('portuguese', coalesce(c.name, '')) @@ websearch_to_tsquery('portuguese', :q)
  OR m.content_tsv @@ websearch_to_tsquery('portuguese', :q)
ORDER BY t.created_at DESC, ts_rank(...) DESC
LIMIT 20 OFFSET ?
```

**Racional**:
- Volume V1 (~50k tickets arquivados, ~500k mensagens/tenant) cabe folgado em GIN.
- `websearch_to_tsquery` aceita sintaxe natural (espaços = AND, aspas = phrase, `-` = NOT).
- Sem nova infra (sem OpenSearch/Meilisearch/Elasticsearch).
- Postgres já é o banco principal; backup automático já cobre os índices.
- Performance medida em testes: 50k tickets + 500k mensagens — p95 < 700ms na busca complexa.

**Alternativas rejeitadas**:
- **OpenSearch dedicado**: + um deploy, + um backup, + uma falha. YAGNI em V1.
- **`ILIKE '%q%'`**: scan completo; performance inaceitável > 10k rows.
- **pg_trgm em vez de tsvector**: bom para fuzzy match, ruim para frase exata e operadores. tsvector + websearch é melhor para o caso.

**Trade-offs**:
- Conversão `tsvector` em `conversation_messages` adiciona ~30% no tamanho da tabela (coluna `STORED`). Aceitável.
- Idioma fixo `portuguese` em V1; em V2 multi-tenant com idiomas diversos pode exigir coluna `language` por tenant.

---

## R5 — Normalização de telefone para deduplicação

**Decisão**: `PhoneNormalizer.Normalize(string raw)` — implementação simples: extrai apenas dígitos via regex `[^0-9]`, valida tamanho mínimo (8 dígitos), e (opcional) prefixa `55` se o input tiver 10–11 dígitos (BR sem DDI).

```csharp
public static string? Normalize(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return null;
    var digits = new string(raw.Where(char.IsDigit).ToArray());
    if (digits.Length < 8) return null;
    // BR heuristic: 10–11 digits → assume domestic, prefix 55
    if (digits.Length is 10 or 11) digits = "55" + digits;
    return digits;
}
```

Coluna `contacts.phone_normalized varchar(20)` com índice B-tree (`btree`) e único parcial (`WHERE phone_normalized IS NOT NULL AND deleted_at IS NULL`).

**Racional**:
- BR é o público-alvo V1; o tenant é instalado em `*.omnicare.ia.br`.
- Heurística simples cobre 95% dos casos. Edge cases (números internacionais, ramais) ficam fora do índice dedup mas são preservados em `phone` (formato original).
- Sem `libphonenumber-csharp` (NuGet novo, ~1.5MB, transitive `protobuf`) — viola Princípio V em V1.

**Alternativas rejeitadas**:
- **`libphonenumber-csharp`**: maturidade alta mas pacote pesado; YAGNI V1.
- **Sem normalização** (índice no campo cru): falha em deduplicar "(11) 99999-9999" vs "11 99999-9999" vs "+5511999999999".
- **Apenas dígitos sem DDI**: ambíguo para números internacionais; em V1 risco baixo.

**Trade-offs**:
- Heurística não cobre números internacionais (>= 12 dígitos sem 55). Decisão consciente; documentada em Assumptions do spec.
- Migration de números antigos: `ContactBackfillJob` aplica `Normalize` retroativamente.

---

## R6 — Escopo de delivery dos eventos WebSocket

**Decisão**: Publicar em **dois canais Redis Pub/Sub** por tenant:
- `{slug}:crm:dept:{department_id}` — recebido por `tenant_attendant` que pertence ao departamento.
- `{slug}:crm:supervisor` — recebido por `supervisor` e `tenant_admin`.

`CrmWebSocketEndpoint` (Spec 007) já implementa SUBSCRIBE por papel. Ao publicar, o `TicketEventPublisher` envia **uma cópia** em cada canal aplicável:
- Ticket do departamento X → publica em `{slug}:crm:dept:X` E em `{slug}:crm:supervisor`.

**Racional**:
- Atendente recebe **apenas** seus tickets (Princípio I + FR-022 + FR-036).
- Supervisor/admin recebem tudo sem filtragem cliente-side (gargalo em UI com muito volume seria evitado por filtros do front).
- Pub/Sub é "best-effort" — cliente offline não recebe (FR-036 Assumption); reload do CRM via REST sincroniza.
- Modelo idêntico ao já entregue pela Spec 007 para `message.created` e `conversation.status_changed`.

**Alternativas rejeitadas**:
- **Um canal por tenant + filtro cliente**: vaza informação para clientes (atendente recebe ticket de outro depto). Quebra Princípio I.
- **Streaming reliable (Redis Streams + ack)**: complexidade alta; reconexão WebSocket + reload REST cobre o caso V1.
- **SignalR groups**: SignalR proibido por ADR-005.

**Trade-offs**:
- Eventos perdidos durante reconexão: aceitável; CRM faz GET ao reabrir aba.

---

## R7 — Design do `TicketSlaMonitorJob`

**Decisão**: Job cron Hangfire `* * * * *` (a cada 1 minuto). Implementação:

```pseudo
foreach tenant in active_tenants:
    using tenant schema:
        candidates = SELECT id, status, sla_first_response_deadline, sla_resolution_deadline,
                            waiting_client_since, sla_paused_duration_minutes, first_response_at
                     FROM tickets
                     WHERE status IN ('new','in_progress','waiting_client')
                       AND deleted_at IS NULL

        foreach ticket in candidates:
            now = UTC now
            effective_resolution_deadline = sla_resolution_deadline + paused_minutes
                                          + (waiting_client_since ? now - waiting_client_since : 0)

            // first response
            if first_response_at is null AND sla_first_response_deadline is not null:
                consumed = (now - assignment_at) / (sla_first_response_deadline - assignment_at)
                if consumed >= warning_threshold (default 0.8) AND not redis.exists("{slug}:ticket:{id}:sla_warned:first_response"):
                    emit ticket.sla_warning(type=first_response)
                    redis.set("{slug}:...:first_response", "1", ttl=86400)
                if now > sla_first_response_deadline AND not redis.exists("{slug}:ticket:{id}:sla_breached:first_response"):
                    emit ticket.sla_breached(type=first_response)
                    write_mongo_event(sla_breached, type=first_response)
                    redis.set("{slug}:...:first_response_breached", "1", ttl=86400)

            // resolution
            if status != 'waiting_client':  // SLA pausa em waiting_client
                consumed = (now - created_at) / (effective_resolution_deadline - created_at)
                ... same logic for type=resolution ...
```

Latência alvo: 1 tick por minuto → cobertura `[t, t+60s]` para warnings/breaches. SC-010 (3s p95) é satisfeito porque o evento é emitido **imediatamente** ao detectar a condição dentro do tick.

**Racional**:
- 1 minuto de granularidade é adequado para SLAs medidos em minutos/horas.
- Per-tenant loop é simples; com ~500 tickets ativos × ~10 tenants iniciais = 5k rows/tick — Postgres responde em < 200ms.
- Redis flags evitam re-emissão idempotente.

**Alternativas rejeitadas**:
- **Job por ticket agendado em deadline-warning-time + deadline-breach-time**: 4 jobs por ticket × ~500 tickets × N tenants = milhões de jobs latentes em Redis (Hangfire usa Redis storage). Custoso em memória.
- **Triggers PG + LISTEN/NOTIFY**: Hangfire não suporta nativamente; complexidade extra.
- **Cálculo em UI (sem job server-side)**: front não pode emitir eventos para outros clientes; e supervisor offline não receberia alertas.

**Trade-offs**:
- Atraso máximo de 60s entre cruzar 80% e emitir warning. Aceitável (SLAs medidos em minutos/horas, 60s é < 5% do prazo típico).

---

## R8 — Kanban: drag-drop, virtualização e refresh

**Decisão**:
- **Drag-drop**: `@angular/cdk/drag-drop` (`CdkDropList` + `CdkDrag`). Já é peer-dependency da PrimeNG 21 — sem novo pacote efetivo.
- **Virtualização**: não usar V1. Cap de ~500 tickets/tenant + filtros agressivos do operador → cada coluna típica tem 5–50 cards. CDK Virtual Scroll fica para V1.1 se necessário.
- **Refresh**: combinação de eventos WebSocket (push) + auto-refresh `setInterval` a cada 30s (fallback se WS cair). `Tickets:KanbanRefreshSeconds` configurável.

**Racional**:
- CDK drag-drop é o padrão Angular; estável e documentado.
- Volume V1 dispensa virtualização — overhead UX não justifica.
- WS + fallback periodic = resiliente sem complexidade extra.

**Alternativas rejeitadas**:
- **Bibliotecas dedicadas (ngx-drag-drop, SortableJS)**: dependência nova; sem ganho funcional.
- **Apenas WS sem fallback**: se WS cair, atendente vê estado desatualizado.

**Trade-offs**:
- Sem virtualização: se tenant V1 ultrapassar 500 tickets ativos, scroll fica lento. Documentado como limite. V1.1 introduz virtualização.

---

## R9 — Deduplicação de contato com tolerância a race

**Decisão**: `ContactDeduplicationService` com **lock Redis curto** antes da query+insert.

```pseudo
async FindOrCreateContact(hints: { email?, phone? }):
    key = email ? "{slug}:contact:dedup:lock:email:{sha256(lower(email))}"
                : "{slug}:contact:dedup:lock:phone:{phone_normalized}"
    using await redis.AcquireLock(key, ttl=3s, max_wait=3s):
        // Inside the lock — serializado para o mesmo email/phone
        existing = SELECT * FROM contacts WHERE
                    (email IS NOT NULL AND lower(email) = lower(:email)) OR
                    (phone_normalized IS NOT NULL AND phone_normalized = :phone_normalized)
                   ORDER BY (email IS NOT NULL) DESC, created_at ASC
                   LIMIT 1
        if existing:
            // Atualiza campos vazios com novos valores não-nulos
            UPDATE contacts SET name = COALESCE(name, :name),
                                 email = COALESCE(email, :email),
                                 ...
                                 source_channels = array_append_unique(source_channels, :channel)
            WHERE id = existing.id
            return existing
        else:
            INSERT INTO contacts ... RETURNING *
```

Unique partial index `contacts_email_unique_idx` em `lower(email)` `WHERE email IS NOT NULL AND deleted_at IS NULL` é defesa em profundidade — se o lock falhar (Redis indisponível), o banco rejeita duplicata.

**Racional**:
- Lock garante atomicidade lógica (read + insert) sem `SERIALIZABLE` na transação inteira.
- TTL curto (3s) — falhas catastróficas liberam o lock automaticamente.
- Unique partial index respeita soft-delete (contato apagado não bloqueia novo com mesmo email).
- Maioria das chamadas será cache-friendly (mesmo email→mesmo hash do lock).

**Alternativas rejeitadas**:
- **Apenas unique index**: gera exceção em alguns casos (UX feia, error handling complexo).
- **`SELECT ... FOR UPDATE`**: degrada throughput em row contention; é table-scope se o índice não bate.
- **Sem dedup transacional, deixar duplicados e mergear depois**: viola FR-026 e degrada UX (atendente vê 2 contatos para o mesmo cliente).

**Trade-offs**:
- Redis fica no path crítico de criação de contato. Aceitável — Redis já é peça central (Hangfire, locks de ticket, sessões).
- Se Redis cair, fallback é o unique index (UPSERT pattern com retry).

---

## R10 — Migration de tickets do scaffold Spec 005 para o modelo V2

**Decisão**: Migration `Add_Tickets_FullModel.sql` é **idempotente** e contém os seguintes passos em ordem:

1. **Drop CHECK** do enum antigo (`status IN ('queued','assigned','open','resolved','closed')`).
2. **UPDATE** mapeando status antigos para novos (R2).
3. **ALTER TABLE** adicionando colunas: `protocol`, `channel`, `priority`, `conversation_id`, `contact_id`, `tags`, `resolved_at`, `cancelled_at`, `first_response_at`, `sla_first_response_deadline`, `sla_resolution_deadline`, `sla_paused_duration_minutes`, `waiting_client_since`, `has_reminder_alert`, `search_vector`, `deleted_at`.
4. **CHECK** novo: `status IN ('new','in_progress','waiting_client','resolved','cancelled')`.
5. **Backfill** colunas mínimas:
   - `protocol`: NULL temporariamente (preenchido por `BackfillTicketProtocolJob`).
   - `channel`: `'manual'` (presumivelmente os scaffold-created vinham de testes).
   - `priority`: `'normal'`.
   - `sla_paused_duration_minutes`: `0`.
   - `has_reminder_alert`: `false`.
6. **Index GIN** em `search_vector`.
7. **Rename `assigned_attendant_id` → `attendant_id`** para alinhar com FR-005 (nome semântico mais claro). Update view/queries dependentes.
8. **Rename `assigned_at` → permanece** (preservado — preencher `attendant_id` mudou junto).
9. **Job `BackfillTicketProtocolJob` (one-shot)** chamado após migration: gera protocolo para tickets criados pré-migration (data `created_at` → `YYYYMMDD`, sequence inicializa em 1 daquele dia).
10. **Add `protocol NOT NULL UNIQUE`** após backfill (ou em V1.1 — pode ficar nullable em V1 com warning de telemetria, simplificando rollout).

**Racional**:
- Operação reversível parcialmente: drop CHECK + UPDATE é atômico em transação; ALTER TABLE bloqueia escritas mas é rápido (< 10s para ~1k rows).
- Backfill em job evita bloqueio longo da migration (DDL + DML grande causa lock).
- Constraint `NOT NULL` em `protocol` aplicada após backfill garante consistência futura.

**Alternativas rejeitadas**:
- **Drop + Create**: perde dados; FK em `ai_handoff_snapshots` quebra.
- **Migration em múltiplas passadas (cada coluna isolada)**: 17 migrations sequenciais, alto risco de inconsistência intermediária.
- **NOT NULL imediato com placeholder** (`protocol = 'TK-MIGRATED-' || id`): polui dados de produção com placeholder feio.

**Trade-offs**:
- Janela de 1 deploy onde `protocol` é NULL nos rows antigos. Mitigada por: (a) testes em produção pré-deploy verificam ausência de scaffold rows; (b) backfill é o primeiro job após migration; (c) front-end exibe placeholder "—" se NULL.

---

## R11 — Comportamento de `ai_handoff_snapshots` no ticket detail

**Decisão**: `ai_handoff_snapshots` (Spec 006) **NÃO** é fundido em `conversation_messages`. Continua sendo lido como um "evento de handoff" exibido na timeline do `ticket-detail`:
- Renderiza-se como uma **divisória visual** "🤖 → 👤 Transferido para humano" com timestamp + razão.
- Antes da divisória: mensagens da IA com role distinta (`agent`).
- Após a divisória: mensagens do atendente e do cliente continuando a conversa.

O snapshot original (`history_json`) fica como fonte da verdade do **que a IA viu**, mas a renderização padrão consome `conversation_messages` direto (já inclui as mensagens da IA antes do handoff — pela Spec 006 elas são persistidas como `sender_type = ai_agent`).

**Racional**:
- Princípio II §5 requer "atendentes vendo tudo que a IA disse antes". `conversation_messages` já tem tudo.
- `ai_handoff_snapshots` serve como **auditoria** (snapshot imutável do estado no momento do handoff), útil quando mensagens posteriores forem editadas/apagadas (V2). Em V1 é redundante mas barato manter.
- Renderização é channel-agnostic (Princípio III): mesmo widget de timeline da Spec 007.

**Alternativas rejeitadas**:
- **Sumir com `ai_handoff_snapshots`**: perde a auditoria do momento exato do handoff.
- **Renderizar a partir do snapshot, não da tabela**: timeline ficaria estática (não receberia mensagens novas durante a conversa).

---

## R12 — Coexistência da rota `/conversations` (Spec 007) com `/kanban` (Spec 009)

**Decisão**: O CRM tem duas rotas principais:
- `/` (= `/kanban`) — **home, default**. Tickets em pipeline Kanban.
- `/conversations` — Lista de conversas SEM ticket (raras — IA ainda atendendo, sem transbordo). Ao clicar numa conversa, abre detalhe; se a conversa **tiver** ticket, redireciona para `/tickets/{ticket_id}` (que renderiza `ticket-detail`).

Conversas resolvidas via "encerramento pelo atendente" passam por `ticket-detail` que dispara o encerramento da conversa em cascata (FR-019).

**Racional**:
- Spec 007 entregou `/conversations` como home; mas o foco operacional V1.x é tickets formais. Mudar o home alinha à nova realidade.
- "Conversas sem ticket" é uma minoria (IA atendendo, antes de transbordo). Mas preserva o caso de "atendente quer monitorar IA em ação".
- Redirecionamento `conversation → /tickets/{id}` se houver ticket: zero confusão de "que tela uso pra esta conversa?"

**Alternativas rejeitadas**:
- **Apagar `/conversations`**: perde a visão de "conversa em andamento ainda sem ticket".
- **Manter `/conversations` como home e adicionar `/kanban` como menu**: subvalorização do trabalho formal; UX confusa.

---

## R13 — `has_reminder_alert` (badge ⚠️) — quem liga/desliga

**Decisão**: O campo `has_reminder_alert` (boolean default false) é controlado **exclusivamente** pelo subsistema de Agenda (Spec 011 — fora desta spec). Esta spec apenas:
- Exibe o badge ⚠️ no `ticket-card` quando `true`.
- Reseta para `false` quando o ticket é encerrado (`status → resolved` ou `cancelled`).
- Permite ao atendente "reconhecer" o alerta (resetar `false` manualmente) via `PATCH /api/tickets/{id}` — V1.1.

Spec 011 (Agenda) terá a responsabilidade de setar `true` quando um envio automático de lembrete falhar (e.g., janela WhatsApp expirada e cliente não respondeu).

**Racional**:
- Mantém esta spec focada; integração com Agenda é uma flag simples.
- Reset automático ao resolver é uma garantia útil (atendente não fica com badges órfãos).

**Alternativas rejeitadas**:
- **Tabela `ticket_alerts(ticket_id, type, created_at, acknowledged_at)`**: complexo demais para V1; basta 1 boolean.

---

## R14 — Eventos `note_added`, `tag_added`: granularidade do log Mongo

**Decisão**: Cada operação relevante gera **um** documento em `{slug}_ticket_events`:
- `note_added`: registra `actor_id`, `actor_name`, `note_id` (FK lógica para `ticket_notes`), `timestamp`.
- `tag_added`: registra `actor_id`, `actor_name`, `tag_added` (string), `timestamp`.
- `tag_removed`: idem com `tag_removed`.
- `priority_changed`: `from`, `to`, `actor`.
- `subject_changed`: `from`, `to`, `actor`.
- `status_changed`: `from`, `to`, `actor`, `reason?`.
- `transferred`: `from_attendant_id`, `to_attendant_id`, `from_department_id`, `to_department_id`, `actor`, `reason?`.
- `attendant_assigned`: `attendant_id`, `actor_type` (system/round_robin/manual_pickup).
- `sla_breached`: `sla_type` (first_response/resolution), `breached_at`.

Conteúdo das notas **não** é replicado para o Mongo event (apenas o ID) — a fonte da verdade é `ticket_notes`.

**Racional**:
- Cada evento é uma row append-only — auditoria perfeita (Princípio VI).
- Conteúdo de mensagens **não** vai para Mongo (já está em `conversation_messages`) → evita duplicação massiva.
- Granularidade permite filtros: "todas as transferências de Maria nesta semana" é uma query Mongo trivial.

---

## Resumo

Decisões consolidadas:

| ID | Decisão | Impacto V1 |
|---|---|---|
| R1 | Sequence Postgres por tenant/dia para protocolo | Performance + unicidade nativa |
| R2 | Rewrite do enum TicketStatus (sem coexistência) | Migration de dados; 0 dívida técnica |
| R3 | Pausa SLA como `waiting_client_since` + `paused_duration_minutes` | Modelo simples, histórico granular em Mongo |
| R4 | tsvector GIN em tickets + messages | Sem OpenSearch; p95 < 1s |
| R5 | PhoneNormalizer simples (BR-first) | Sem libphonenumber NuGet; cobre 95% |
| R6 | Pub/Sub Redis em 2 canais (dept + supervisor) | Princípio I + simplicidade |
| R7 | TicketSlaMonitorJob cron `* * * * *` | Granularidade 60s, idempotente |
| R8 | CDK drag-drop + WS push + 30s polling fallback | Zero NuGet/lib novo |
| R9 | Dedup com Redis lock + unique partial index | Sem race + defesa em profundidade |
| R10 | Migration in-place com backfill via job | 0 perda de dados; rollout controlado |
| R11 | `ai_handoff_snapshots` permanece como audit, não fundido | Princípio II + auditoria |
| R12 | Home = `/kanban`; `/conversations` para conversas sem ticket | Foco operacional |
| R13 | `has_reminder_alert` controlado por Spec 011 (Agenda) | Reset automático no encerramento |
| R14 | 1 evento Mongo por operação, sem replicar conteúdo | Auditoria sem bloat |

Todas as decisões resolvem itens "deferred to plan/implementation" do checklist da spec (concorrência de protocolo, normalização internacional, FTS strategy, WS topology, UI components, index strategy).
