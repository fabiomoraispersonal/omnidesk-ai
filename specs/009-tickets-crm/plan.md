# Implementation Plan: Tickets / CRM (Pipeline Kanban)

**Branch**: `009-tickets-crm` | **Data**: 2026-05-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/009-tickets-crm/spec.md`

## Summary

Spec **Tickets/CRM** consolida o módulo central do CRM. Substitui o scaffold mínimo de `tickets` criado pela Spec 005 (que existia apenas para suportar distribuição/round-robin) pela **versão completa** do ciclo de vida: protocolo `TK-YYYYMMDD-XXXXX`, 5 status (`new`/`in_progress`/`waiting_client`/`resolved`/`cancelled`), SLA com **pausa** em `waiting_client`, transferência entre atendentes/departamentos com recálculo de SLA, deduplicação automática de contatos, perfil de contato com histórico paginado, filtros + busca full-text, anotações internas, e pipeline Kanban configurável por departamento.

Esta spec **fecha o ciclo operacional V1**: cliente chega por Live Chat (007) ou WhatsApp (008) → IA atende (006) → IA transfere para humano → ticket nasce e segue ciclo de vida formal no CRM (009).

Reaproveita o que Spec 005–008 entregaram:

- **Pipeline conversacional** Spec 006 (`IncomingMessageWorker`/`OutgoingMessageWorker`) — sem mudança.
- **Distribuição round-robin** Spec 005 (`TicketAssignmentService`, `PickupTicketEndpoint`, `TransferTicketEndpoint`, `SlaCalculator`) — adaptada para os novos status (rename + extensão de regras).
- **Handoff IA → ticket** Spec 006 (`ITicketCreationGateway`) — `StubTicketCreationGateway` é substituído pela implementação real (`TicketCreationGateway`) que preenche `protocol`, `channel`, `subject` auto, `contact_id` (com dedup), SLA, e dispara round-robin.
- **WebSocket `/ws/crm`** Spec 007 — ganha 6 eventos novos (`ticket.created`, `ticket.assigned`, `ticket.status_changed`, `ticket.transferred`, `ticket.sla_warning`, `ticket.sla_breached`).
- **`ai_handoff_snapshots`** Spec 006 — passa a ser **acessível pelo atendente** (já compõe histórico do ticket).
- **`visitors`** Spec 007 — ganha campo `contact_id` (nullable) para vincular visitor → contato deduplicado.
- **`conversations`** Spec 007 — ganha campo `ticket_id` (nullable, FK lógica → já existia comentado na migration); um ticket fecha a conversa correspondente ao resolver.

Backend novo entrega:

- **4 tabelas tenant-scoped** novas: `contacts`, `ticket_notes`, `pipelines`, `pipeline_columns`.
- **Expansão de `tickets`** (protocol, channel, priority, conversation_id, contact_id, tags, subject expandido, resolved_at, cancelled_at, first_response_at, sla_first_response_deadline, sla_resolution_deadline, sla_paused_duration_minutes, waiting_client_since, has_reminder_alert) + **rename de status enum** (`queued|assigned|open|resolved|closed` → `new|in_progress|waiting_client|resolved|cancelled`).
- **1 coluna nova em `visitors`**: `contact_id` (FK → contacts, nullable).
- **1 coluna nova em `conversations`**: `ticket_id` (FK → tickets, nullable, idx).
- **1 collection MongoDB** nova: `{slug}_ticket_events` (auditoria imutável conforme Princípio VI).
- **1 sequence Postgres** por tenant por dia para protocolos: `ticket_protocol_seq_YYYYMMDD` (criada on-demand pela primeira inserção do dia).
- **8 endpoints Tickets** + 6 endpoints Contacts + 3 endpoints Pipelines + 2 endpoints Notes/Events (REST).
- **2 jobs Hangfire**: `TicketSlaMonitorJob` (cron `* * * * *` — checa SLA a cada minuto, emite warning/breach), `WaitingClientResumerJob` (disparado por evento de mensagem do cliente quando ticket em `waiting_client`).
- **Round-robin re-trigger**: quando atendente fica online (Spec 005 já emite evento), `AttendantAvailabilityHandler` enfileira ticket mais antigo da fila `new` do depto.

Frontend CRM (Angular 21) entrega:

- `features/tickets-kanban/` — pipeline Kanban com 3 colunas (drag-drop entre colunas muda status), filtros (depto/atendente/canal/prioridade/tag/período), busca full-text, badges SLA verde/amarelo/vermelho, badge ⚠️ para `has_reminder_alert`.
- `features/ticket-detail/` — 2 painéis (histórico + resposta à esquerda, dados + ações à direita), anotações internas colapsáveis, edição inline (subject/priority/tags/status), botões Transferir/Encerrar/Cancelar.
- `features/contacts/` — perfil de contato com dados editáveis + abas "Tickets" e "Conversas" paginadas + dedup automática transparente.
- `features/pipeline-config/` — config: renomear/reordenar/colorir as 3 colunas (sem add/remove).
- Extensões em `features/live-chat-inbox/` (Spec 007) — a tela de conversa **continua a existir**, mas tickets atribuídos abrem na nova `ticket-detail` (mais rica). Live Chat Inbox vira **lista de conversas em andamento sem ticket** (raras — IA ainda atendendo) + atalho para o Kanban.

Implementação faseada respeitando dependências:

1. **Fase A (Domain + Data)**: nova migration que renomeia/expande `tickets`, cria `contacts`/`ticket_notes`/`pipelines`/`pipeline_columns`, adiciona FKs em `visitors`/`conversations`. Migration de dados para mapear status antigos → novos (`queued`→`new`, `assigned`/`open`→`in_progress`, `resolved`→`resolved`, `closed`→`cancelled`).
2. **Fase B (Backend core)**: domain entities expandidas, `TicketCreationGateway` real substituindo o stub, `TicketProtocolService`, `ContactDeduplicationService`, eventos Mongo, endpoints CRUD.
3. **Fase C (SLA)**: `TicketSlaMonitorJob`, pausa em `waiting_client`, eventos WebSocket warning/breach.
4. **Fase D (Frontend Kanban)**: componente Kanban com drag-drop, filtros, busca, detalhe.
5. **Fase E (Frontend complementar)**: perfil de contato, config pipeline, anotações.
6. **Fase F (polish)**: ajustar Live Chat Inbox para coexistir com nova tela de detalhe, integração de notificações (Spec 010 — opcional V1.1).

## Technical Context

**Backend**: C# .NET 10 — Minimal API + Endpoint Groups (continuação dos padrões 002–008)
**Frontend**: TypeScript — Angular 21 Standalone Components + Signals (CRM em `src/omniDesk.Crm/`)
**ORM**: Entity Framework Core 9 + Migrations SQL tenant-scoped (padrão `Add_*` em `Infrastructure/Persistence/Migrations/`)

**Storage**:

- PostgreSQL `tenant_{slug}.tickets` (**expandida** — 17 campos novos + status enum reescrito), `tenant_{slug}.contacts` (**nova**, ~10 campos), `tenant_{slug}.ticket_notes` (**nova**), `tenant_{slug}.pipelines` + `tenant_{slug}.pipeline_columns` (**novas**). Mais 1 coluna em `visitors` e 1 em `conversations`.
- PostgreSQL sequences `tenant_{slug}.ticket_protocol_seq_YYYYMMDD` (criadas on-demand para gerar a porção `XXXXX` do protocolo). Após 30 dias as sequences antigas podem ser dropadas por job de manutenção (V1.1; em V1 ficam no schema).
- Redis `{slug}:ticket:lock:{ticket_id}` (lock de mudança concorrente, TTL 5s) — reaproveita padrão do Spec 005 (`TicketAssignmentService`).
- Redis `{slug}:contact:dedup:lock:{email_hash}` e `{...}:{phone_normalized}` (lock curto para evitar criação de contato duplicado por race). TTL 3s.
- Redis `{slug}:ticket:{id}:sla_warned:{type}` (flag idempotente, TTL 24h).
- MongoDB `{slug}_ticket_events` (auditoria — Constituição §VI imutável).
- MinIO `tenant-{slug}/ticket-attachments/{ticket_id}/{message_id}-{filename}` (reaproveita pipeline de anexo do Live Chat 007 — sem mudança).

**Background jobs** (Hangfire):

| Worker | Schedule/Trigger | Responsabilidade |
|---|---|---|
| `IncomingMessageWorker` (Spec 006) | Fila `{slug}:incoming_messages` | **REUTILIZADO**. Em mensagem do cliente em conversa com `ticket_id` em `waiting_client`, dispara `WaitingClientResumerJob`. |
| `OutgoingMessageWorker` (Spec 006) | Fila `{slug}:outgoing_messages` | **REUTILIZADO**. Detecta `sender_type = attendant` + ticket sem `first_response_at` → preenche e emite evento. |
| `TicketSlaMonitorJob` | Cron `* * * * *` (a cada minuto) | Varre tickets `new`/`in_progress`/`waiting_client` no tenant; emite `ticket.sla_warning` (uma vez por tipo, via Redis flag) ao cruzar 80% do prazo; emite `ticket.sla_breached` ao expirar; persiste evento `sla_breached` em `{slug}_ticket_events`. **Idempotente**. |
| `WaitingClientResumerJob` | Sob demanda | Transição automática `waiting_client → in_progress` ao receber mensagem do cliente: calcula pausa (`now() - waiting_client_since`), soma a `sla_paused_duration_minutes`, zera `waiting_client_since`, atualiza `updated_at`, emite `ticket.status_changed`. |
| `AttendantAvailabilityHandler` (Spec 005) | Disparado por `attendant.online` / capacity-freed | **REUTILIZADO + ESTENDIDO**: agora prioriza tickets `new` (não `queued`); atribui o mais antigo dos departamentos do atendente respeitando `max_simultaneous_chats`. |
| `ContactBackfillJob` (one-shot) | Manual durante deploy | Migra `visitors` existentes (Spec 007) que têm `email`/`phone` para criar contatos correspondentes (idempotente). Roda 1× pós-deploy. |
| `BackfillTicketProtocolJob` (one-shot) | Manual durante deploy | Gera `protocol` retroativo para tickets criados pelo Stub Spec 006 (que existem sem o campo). Idempotente. |
| `TicketEventArchiverJob` (V1.1 — não bloqueia V1) | Cron `0 3 * * *` | Move eventos > 180 dias para collection fria. **Fora do escopo V1**. |

**WebSocket**: ASP.NET Core nativo + Redis Pub/Sub (ADR-005). **Sem novo endpoint** — reutiliza `/ws/crm` da Spec 007. 6 eventos novos publicados em `{slug}:crm:dept:{department_id}` (e `{slug}:crm:supervisor` para supervisores/admins):

- `ticket.created` — `{ ticket_id, protocol, department_id, channel, attendant_id? }`. Emitido por `TicketCreationGateway`.
- `ticket.assigned` — `{ ticket_id, attendant_id, attendant_name }`. Emitido por `TicketAssignmentService` + reatribuição.
- `ticket.status_changed` — `{ ticket_id, from_status, to_status, actor_id?, actor_type }`. Emitido em toda transição.
- `ticket.transferred` — `{ ticket_id, from_attendant_id?, to_attendant_id?, from_department_id?, to_department_id?, reason? }`. Emitido pelo endpoint de transferência.
- `ticket.sla_warning` — `{ ticket_id, sla_type, deadline, percent_consumed }`. Emitido pelo monitor ao cruzar 80%.
- `ticket.sla_breached` — `{ ticket_id, sla_type, deadline }`. Emitido pelo monitor ao expirar.

**Eventos auxiliares já existentes** que esta spec não duplica:

- `conversation.status_changed` (Spec 007) — emitido pelo encerramento de ticket que marca a conversa como `resolved` em cascata. CRM já consome.
- `message.created` (Spec 007) — usado para detectar `first_response_at`.

**Protocolo `TK-YYYYMMDD-XXXXX`**: gerado por `TicketProtocolService` em `Infrastructure/Tickets/`. Algoritmo:

1. Calcula data UTC (`yyyyMMdd`).
2. Tenta `nextval('tenant_{slug}.ticket_protocol_seq_20260511')`.
3. Se sequence não existir, executa DDL `CREATE SEQUENCE IF NOT EXISTS ...` dentro de transação `SERIALIZABLE` e re-tenta. (Pequeno custo aceitável para o **primeiro ticket do dia**; após criação, todas as chamadas usam a sequence existente.)
4. Formata para 5 dígitos zero-padded: `TK-{yyyyMMdd}-{nextval:D5}`.

**Garantia de unicidade**: o protocolo é a coluna `protocol UNIQUE` em `tickets`, e a sequence é per-tenant per-dia — colisões são impossíveis por construção (sequence é serial); o `UNIQUE` é defesa em profundidade.

**Full-text search**: PostgreSQL `tsvector` em `tickets.search_vector` (coluna gerada `GENERATED ALWAYS AS (...)`) com peso A em `protocol`, B em `subject`, C em `contact.name`. Index `GIN`. Busca em mensagens é fora desta coluna — pesquisa em `conversation_messages.content` via JOIN com `tsvector` de mensagens (gerado por trigger ou coluna materializada — decisão em research.md R4).

**Crypto**: nenhum dado novo criptografado nesta spec — `AesGcmEncryptionService` (Spec 008) não é tocado. Contacts não armazenam segredos.

**Testing**:

- Backend: xUnit + Testcontainers (Postgres + Redis + Mongo + MinIO — já configurados pelas specs 007/008).
- Concorrência de protocolo: teste paralelo criando 100 tickets simultâneos verifica unicidade.
- SLA pause/resume: teste com `IClock` fake (`FakeTimeProvider`) percorrendo cenários `waiting_client_since` → 30min → resume → 80% → warning → breach.
- Contact dedup: teste com 3 visitantes simultâneos via mesmo e-mail → deve resultar em 1 contato.
- Migration data: teste de upgrade copia 5 tickets do schema antigo, executa migration, valida que statuses foram mapeados corretamente.
- CRM: Angular TestBed (`.spec.ts` co-localizado).
- Kanban drag-drop: teste com CDK DragDropModule mock.
- E2E (Playwright opcional V1.1) para fluxo completo cliente → IA → transbordo → ticket → atendente.

**Target Platform**: Linux ARM64 (API); Cloudflare Pages (CRM). Sem widget novo.

**Project Type**: Web service (API .NET 10) + 1 SPA Angular (CRM). Sem novo projeto.

**Dependências backend** (zero NuGet novo):

| Pacote | Já em uso desde | Uso nesta spec |
|---|---|---|
| `Microsoft.EntityFrameworkCore` 9.x | Spec 002 | Migrations + DbContext |
| `StackExchange.Redis` | Spec 002 | Locks de protocolo, dedup contato, flags SLA |
| `Hangfire` | Spec 003 | `TicketSlaMonitorJob` + `WaitingClientResumerJob` + backfills |
| `MongoDB.Driver` | Spec 003 | Collection `ticket_events` |
| `FluentValidation.AspNetCore` | Constituição | Payloads de criação/edição |
| `Serilog` | Spec 002 | Logs estruturados de transições |

**Dependências frontend CRM** (built-ins + libs já em uso):

- PrimeNG 21+: `Card` (Kanban cards), `Tag` (status/priority badges), `Dropdown` (filters), `MultiSelect` (tag filter), `Calendar` (period filter), `Dialog` (transfer dialog, new ticket modal), `Tabs` (perfil contato), `Toast`, `Paginator`.
- `@angular/cdk` (drag-drop) — verificar transitividade; se ausente, adicionar `@angular/cdk` v21 (zero-cost — built-in Angular).
- `date-fns` + `date-fns-tz` (já em uso) — formatação de SLA, períodos.
- `@angular/forms` Reactive Forms (já em uso) — formulários de contato e ticket manual.

**Variáveis de configuração** (2 novas em `appsettings.json`):

| Chave | Default | Uso |
|---|---|---|
| `Tickets:SlaWarningThresholdPercent` | `80` | Limiar para emitir `ticket.sla_warning`. Configurável para testes. |
| `Tickets:KanbanRefreshSeconds` | `30` | Intervalo de auto-refresh do Kanban (front-end) para tickets que não recebem WS event (fallback). |

**Performance Goals** (alinhados aos SC do spec):

- Kanban com ≤ 100 tickets ativos: p95 < 1.5 s (SC-008).
- Busca full-text para corpus ≤ 10k mensagens: p95 < 1 s (SC-009).
- `ticket.sla_warning` WebSocket → CRM: ≤ 3 s após cruzar 80% (SC-010).
- Criação de ticket por transbordo: ≤ 2 s (SC-002).
- Drag-drop de card → status atualizado no banco: ≤ 500 ms (UX percebida).
- Round-trip de envio de mensagem do atendente (botão → cliente recebe): ≤ 1 s (delegado a 007/008).

**Constraints**:

- **Imutabilidade**: tickets `resolved`/`cancelled` MUST recusar mudanças (validator + DB check). Apenas adicionar notas internas é permitido (audit-only).
- **Anotações internas isoladas**: `ticket_notes` MUST NUNCA entrar em prompts da IA, payloads de mensagem, ou eventos públicos. Validado por teste explícito de pipeline IA + canais.
- **Protocolo concorrente**: sequence Postgres garante 0 colisão; constraint `UNIQUE` é defesa adicional.
- **SLA pause idempotente**: re-emissão de eventos `sla_warning`/`sla_breached` para o mesmo ticket+tipo evitada por flag Redis `{slug}:ticket:{id}:sla_warned:{type}` (TTL 24h).
- **Tickets V1 cap**: até ~500 tickets ativos por tenant (mais que isso passa a exigir partitioning, fora V1).
- **Mensagens internas em `ticket_notes` são append-only**: sem update, sem delete. Mesmo `tenant_admin` não edita notas (audit).
- **WebSocket scope**: `tenant_attendant` recebe eventos apenas dos seus departamentos; `supervisor`/`tenant_admin` recebem para todos. Filtro feito pelo `CrmWebSocketEndpoint` (Spec 007) ao subscrever.
- **Reagendamento de SLA em transferência**: ao mudar departamento, novo `sla_first_response_deadline` = `now() + dept.sla_first_response_minutes` (preserva `first_response_at` se já existir), novo `sla_resolution_deadline` = `now() + dept.sla_resolution_minutes`, **zera** `sla_paused_duration_minutes` (recomeço limpo). Decisão registrada nas Assumptions do spec.
- **Migration coexistência**: a migration de Spec 009 **substitui** a tabela `tickets` (não cria nova) — usa `ALTER TABLE` para renomear status + adicionar colunas. Rows existentes (criadas pelo `StubTicketCreationGateway`) são re-mapeadas: `queued`→`new`, `assigned`/`open`→`in_progress`, `resolved`→`resolved`, `closed`→`cancelled`. `protocol` é backfilled retroativamente via `BackfillTicketProtocolJob` (one-shot).
- **Soft delete**: contacts, tickets, ticket_notes, pipelines, pipeline_columns todos ganham `deleted_at timestamptz NULL` (Princípio IV). Em V1 só `contacts` tem deleção via UI (botão "Apagar contato" — opcional V1.1); tickets nunca são deletados (apenas `cancelled`).

**Scale/Scope**:

- ~500 tickets ativos por tenant em V1 (`new`/`in_progress`/`waiting_client`).
- ~50.000 tickets arquivados (`resolved`/`cancelled`) por tenant — busca em corpus grande precisa ser rápida.
- ~5–20 atendentes por tenant.
- ~3 departamentos por tenant (mediano), até ~10.
- ~200 contatos novos/dia/tenant em pico (clínicas com volume alto).
- ~10 transições de status/min/tenant em pico — `TicketSlaMonitorJob` deve sustentar.

## Constitution Check

*GATE: deve passar antes de Phase 0 e ser reavaliado após Phase 1.*

| Princípio | Status | Observação |
|---|---|---|
| I. Multi-Tenant Isolation (NN) | ✅ PASS | Todas as tabelas novas em `tenant_{slug}.*`. Sequences `ticket_protocol_seq_*` no schema do tenant (zero risco de leak entre tenants). Filas Redis e canais Pub/Sub prefixados (`{slug}:ticket:lock:*`, `{slug}:contact:dedup:lock:*`, `{slug}:crm:dept:*`). MongoDB `{slug}_ticket_events`. **Zero modificação em `public.*`**. RBAC em todos os endpoints valida que o ticket pertence ao schema do tenant resolvido pelo middleware. Atendente fora do departamento: `403` (FR-022, SC-013). |
| II. AI-First, Human-Assisted | ✅ PASS | **Reforça** a constituição: o ticket criado por transbordo **garante** que (a) histórico completo da IA acompanha (US1.6, FR-006, FR-018); (b) o snapshot `ai_handoff_snapshots` da Spec 006 é exibido no painel esquerdo de `ticket-detail` (atendente vê tudo que a IA disse antes de assumir — Princípio II §5). **Zero dead-end**: se não houver atendente no momento, ticket fica `new` em fila do depto (FR-007); um humano sempre aparece eventualmente. Anotações internas (US8, FR-025) **nunca** entram em prompts de IA — validado por teste de pipeline. |
| III. Channel Agnosticism | ✅ PASS | `tickets.channel` aceita 3 valores (`live_chat`, `whatsapp`, `manual`) sem branches em business logic. O `TicketCreationGateway` recebe `channel` como parâmetro e funciona idêntico independente da origem. Renderização da timeline em `ticket-detail` consome `conversation_messages` (channel-agnostic, Spec 007). Adicionar canal novo (V2: Instagram) não exige mudança em tickets. `manual` é um valor especial (sem `conversation_id`) — caso degenerado dentro do mesmo modelo. |
| IV. Security e LGPD (NN) | ✅ PASS | (a) Anotações internas isoladas — nunca chegam ao cliente (FR-025, SC-006). (b) Visibilidade por papel + departamento (FR-022/023). (c) Soft delete em todas as novas tabelas (Princípio IV §7). (d) Contacts armazenam PII — sob a LGPD do tenant; consentimento herdado do Live Chat (Spec 007 §LGPD) ou WhatsApp (cliente enviou ativamente, consentimento implícito). (e) `phone_normalized` é índice técnico — não é PII adicional. (f) Eventos `ticket_events` em MongoDB são pseudonimizados (gravam IDs, não conteúdo das mensagens). (g) Configurações de retenção (Princípio IV §8) aplicáveis: tickets antigos podem ser arquivados/anonimizados — política de retenção configurável é V1.1 (não bloqueia V1). |
| V. Simplicity | ✅ PASS | **Zero NuGet novo**; CDK drag-drop transitivo via PrimeNG (ou Angular built-in). **Zero novo projeto** (sem widget, sem CLI). Reaproveita 100% do pipeline conversacional (006), distribuição (005), WebSocket CRM (007), MinIO/MimeDetector (007). 4 tabelas novas — justificadas pela spec; nenhuma especulativa. 2 jobs Hangfire novos — ambos têm SLO claro. Migration **substitui** o scaffold (não acumula código zumbi). YAGNI aplicado: arquivamento (V1.1), retenção configurável (V1.1), full-text em mensagens (decisão em research) — fora V1 core. |
| VI. Observability e Auditability | ✅ PASS | **Coração da spec**: `{slug}_ticket_events` em MongoDB é log imutável de todas as mudanças relevantes (FR-035). Eventos cobrem: criação, atribuição, mudança de status, transferência, mudança de prioridade, adição/remoção de tag, adição de nota, breach de SLA, edição de subject. `ticket_notes` é append-only (FR-042). Histórico de conversa imutável já garantido pela Spec 007. Métricas dos SCs derivam direto de logs Serilog + Mongo. **Atendente vê tudo da IA** (Princípio VI §1 + §5). |
| VII. Test Discipline | ✅ PASS | Testcontainers para Postgres/Redis/Mongo/MinIO (já configurados 007/008). **Zero magic strings**: `Domain/Tickets/TicketStatus.cs` (enum + extensions com wire values), `Domain/Tickets/TicketPriority.cs`, `Domain/Tickets/TicketChannel.cs`, `Domain/Tickets/TicketEventType.cs`, `Hubs/Events/TicketCrmEvents.cs`, `Domain/Pipelines/PipelineDefaults.cs` (nomes default "Na Fila"/"Em Andamento"/"Aguardando Cliente"). Frontend CRM `.spec.ts` co-localizado. Contract tests para os 6 eventos WebSocket. Concorrência de protocolo testada com 100 inserções paralelas. Migration testada com fixtures (5 rows antigas → 5 rows novas com status mapeado). |

**Resultado**: Constitution Check **APROVADO sem desvios**. Reavaliação pós-Phase 1 — sem mudanças esperadas.

## Project Structure

### Documentation (this feature)

```text
specs/009-tickets-crm/
├── plan.md                          # Este arquivo
├── research.md                      # Phase 0 — decisões técnicas (R1–R10)
├── data-model.md                    # Phase 1 — entidades, migrations (SQL), transições, índices, dedup
├── quickstart.md                    # Phase 1 — fluxos de validação manual end-to-end
├── contracts/
│   ├── tickets-api.md               # CRUD + status + transfer + resolve/cancel + paginação
│   ├── contacts-api.md              # CRUD + history + dedup behavior
│   ├── pipelines-api.md             # listar pipelines + editar colunas (rename/reorder/color)
│   ├── ticket-notes-events-api.md   # notes (append-only) + events (read-only audit)
│   ├── ticket-creation-gateway.md   # contrato interno IA→Ticket (substitui StubTicketCreationGateway)
│   ├── ticket-websocket-events.md   # 6 eventos novos em /ws/crm
│   └── kanban-frontend-contract.md  # comportamento drag-drop, badges SLA, filtros
├── checklists/
│   └── requirements.md              # validado no /speckit-specify
└── tasks.md                         # Phase 2 — gerado por /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── omniDesk.Api/
│   ├── Domain/
│   │   ├── Tickets/                                          # EXPANDIDO (Spec 005 scaffold → V2)
│   │   │   ├── Ticket.cs                                     # entity completa (substitui scaffold)
│   │   │   ├── TicketStatus.cs                               # enum new/in_progress/waiting_client/resolved/cancelled + ToWireValue
│   │   │   ├── TicketPriority.cs                             # enum Low/Normal/High/Urgent
│   │   │   ├── TicketChannel.cs                              # enum LiveChat/WhatsApp/Manual
│   │   │   ├── TicketEventType.cs                            # const set para Mongo
│   │   │   ├── TicketNote.cs                                 # entity append-only
│   │   │   ├── TicketEvent.cs                                # value object para Mongo writes
│   │   │   ├── ITicketRepository.cs
│   │   │   ├── ITicketNoteRepository.cs
│   │   │   └── ITicketEventStore.cs                          # MongoDB write-only
│   │   ├── Contacts/                                         # NOVO
│   │   │   ├── Contact.cs
│   │   │   ├── ContactSourceChannel.cs                       # enum
│   │   │   ├── IContactRepository.cs
│   │   │   └── PhoneNormalizer.cs                            # static — extrai só dígitos
│   │   ├── Pipelines/                                        # NOVO
│   │   │   ├── Pipeline.cs
│   │   │   ├── PipelineColumn.cs
│   │   │   ├── PipelineDefaults.cs                           # static — 3 colunas default
│   │   │   ├── IPipelineRepository.cs
│   │   │   └── PipelineStatusMapping.cs                      # validador unicidade
│   │   ├── LiveChat/Conversation.cs                          # MODIFICADO — + TicketId (nullable)
│   │   └── LiveChat/Visitor.cs                               # MODIFICADO — + ContactId (nullable)
│   │
│   ├── Features/
│   │   ├── Tickets/                                          # NOVO
│   │   │   ├── TicketEndpoints.cs                            # GET list/{id}, POST manual, PUT, PATCH /status /attendant, POST /transfer /resolve /cancel
│   │   │   ├── Commands/
│   │   │   │   ├── CreateManualTicketCommand.cs
│   │   │   │   ├── UpdateTicketCommand.cs                    # subject/priority/tags
│   │   │   │   ├── ChangeTicketStatusCommand.cs              # valida transições válidas
│   │   │   │   ├── TransferTicketCommand.cs                  # REUSO/EVOLUI Spec 005 — agora recalcula SLA
│   │   │   │   ├── ResolveTicketCommand.cs                   # encerra ticket + conversa em cascata
│   │   │   │   └── CancelTicketCommand.cs
│   │   │   ├── Queries/
│   │   │   │   ├── ListTicketsQuery.cs                       # filtros (depto/atendente/canal/prioridade/tag/período) + paginação
│   │   │   │   ├── SearchTicketsQuery.cs                     # full-text (protocol/subject/contact.name/messages)
│   │   │   │   ├── GetTicketDetailQuery.cs                   # ticket + histórico de conversa + notes + dados contato
│   │   │   │   └── ListTicketEventsQuery.cs                  # leitura de Mongo
│   │   │   ├── Notes/
│   │   │   │   ├── TicketNotesEndpoints.cs                   # POST /api/tickets/{id}/notes, GET list
│   │   │   │   └── AddTicketNoteCommand.cs
│   │   │   ├── Validators/
│   │   │   │   ├── CreateManualTicketValidator.cs
│   │   │   │   ├── UpdateTicketValidator.cs
│   │   │   │   └── ChangeStatusValidator.cs                  # valida transições
│   │   │   ├── TicketCreationGateway.cs                      # NOVO — substitui StubTicketCreationGateway (Spec 006)
│   │   │   ├── TicketProtocolService.cs                      # gera TK-YYYYMMDD-XXXXX via sequence Postgres
│   │   │   ├── TicketSubjectAutogen.cs                       # primeiras 100 chars da última msg
│   │   │   └── SlaPauseCalculator.cs                         # cálculo de pausa em waiting_client
│   │   ├── Contacts/                                         # NOVO
│   │   │   ├── ContactEndpoints.cs                           # GET list/{id}, POST/PUT, GET /{id}/tickets /{id}/conversations
│   │   │   ├── Commands/
│   │   │   │   ├── CreateContactCommand.cs
│   │   │   │   ├── UpdateContactCommand.cs
│   │   │   │   └── MergeContactCommand.cs                    # V1.1 — manual; dedup automática V1 em ContactDeduplicationService
│   │   │   ├── Queries/
│   │   │   │   ├── ListContactsQuery.cs
│   │   │   │   ├── GetContactQuery.cs
│   │   │   │   ├── ListContactTicketsQuery.cs                # paginado 20/pg
│   │   │   │   └── ListContactConversationsQuery.cs          # paginado 20/pg
│   │   │   ├── ContactDeduplicationService.cs                # email P1 + phone_normalized P2 + Redis lock
│   │   │   └── ContactBackfillJob.cs                         # one-shot Hangfire — migra visitors → contacts
│   │   ├── Pipelines/                                        # NOVO
│   │   │   ├── PipelineEndpoints.cs                          # GET list/{id}, PUT /columns
│   │   │   ├── Commands/
│   │   │   │   └── UpdatePipelineColumnsCommand.cs           # valida unicidade de status_mapping
│   │   │   ├── Queries/
│   │   │   │   └── GetPipelineWithColumnsQuery.cs
│   │   │   └── PipelineProvisioningService.cs                # chamado por TenantProvisioningJob ao criar depto
│   │   ├── Distribution/                                     # MODIFICADO (Spec 005)
│   │   │   ├── TicketAssignmentService.cs                    # status: Queued→New; Assigned→InProgress
│   │   │   ├── PickupTicketEndpoint.cs                       # idem
│   │   │   ├── TransferTicketEndpoint.cs                     # delega para Features/Tickets/Commands/TransferTicketCommand
│   │   │   ├── SlaCalculator.cs                              # estendido — agora considera pausa em waiting_client
│   │   │   └── AttendantAvailabilityHandler.cs               # prioriza tickets `new` na fila
│   │   ├── AgentRuntime/                                     # MODIFICADO (Spec 006)
│   │   │   └── ITicketCreationGateway.cs                     # contrato evolui — adiciona `Channel`, `ContactHints` (email/phone)
│   │   ├── LiveChat/                                         # MODIFICADO (Spec 007)
│   │   │   └── EndConversationCommand.cs                     # ao resolver ticket, atualiza conversa em cascata (job ou trigger interno)
│   │   └── TenantProvisioning/                               # MODIFICADO (Spec 003)
│   │       └── TenantProvisioningJob.cs                      # + criar pipeline 1:1 com cada depto + 3 colunas default
│   │
│   ├── Hubs/                                                 # MODIFICADO (Spec 007)
│   │   ├── CrmWebSocketEndpoint.cs                           # filtragem por depto (atendente) já existe — confirmar reuso para ticket events
│   │   └── Events/
│   │       └── TicketCrmEvents.cs                            # NOVO — const: TicketCreated, TicketAssigned, TicketStatusChanged, TicketTransferred, TicketSlaWarning, TicketSlaBreached
│   │
│   ├── Infrastructure/
│   │   ├── Tickets/                                          # MODIFICADO + NOVO
│   │   │   ├── TicketConfiguration.cs                        # MODIFICADO — EF mapping expandido + tsvector
│   │   │   ├── TicketNoteConfiguration.cs                    # NOVO
│   │   │   ├── ContactConfiguration.cs                       # NOVO
│   │   │   ├── PipelineConfiguration.cs                      # NOVO
│   │   │   ├── PipelineColumnConfiguration.cs                # NOVO
│   │   │   ├── TicketProtocolSequenceProvider.cs             # Postgres sequence per-tenant per-day
│   │   │   ├── TicketRepository.cs                           # IQueryable + projeções
│   │   │   ├── ContactRepository.cs
│   │   │   ├── PipelineRepository.cs
│   │   │   └── MongoTicketEventStore.cs                      # writes em {slug}_ticket_events
│   │   ├── Jobs/                                             # NOVO
│   │   │   ├── TicketSlaMonitorJob.cs                        # cron */1 min
│   │   │   ├── WaitingClientResumerJob.cs                    # sob demanda
│   │   │   ├── ContactBackfillJob.cs                         # one-shot
│   │   │   └── BackfillTicketProtocolJob.cs                  # one-shot pós-migration
│   │   ├── WebSockets/
│   │   │   └── TicketEventPublisher.cs                       # encapsula publish em {slug}:crm:dept:{id} / {slug}:crm:supervisor
│   │   └── Persistence/Migrations/
│   │       ├── Add_Tickets_FullModel.sql                     # ALTER tickets — colunas novas + status rename + tsvector + protocol UNIQUE
│   │       ├── Add_Contacts.sql                              # CREATE TABLE contacts + index phone_normalized + index email lower(...)
│   │       ├── Add_TicketNotes.sql                           # CREATE TABLE ticket_notes (append-only)
│   │       ├── Add_Pipelines.sql                             # CREATE TABLE pipelines + pipeline_columns + UNIQUE (pipeline_id, status_mapping)
│   │       ├── Add_ContactId_To_Visitors.sql                 # ALTER visitors ADD COLUMN
│   │       └── Add_TicketId_To_Conversations.sql             # ALTER conversations ADD COLUMN (FK lógica já comentada)
│   │
│   └── tests/omniDesk.Api.Tests/
│       ├── Domain/
│       │   ├── Tickets/
│       │   │   ├── TicketStatusTransitionsTests.cs           # válidas/inválidas
│       │   │   └── SlaPauseCalculatorTests.cs                # pausa multi-ciclo
│       │   ├── Contacts/
│       │   │   └── PhoneNormalizerTests.cs                   # BR formats, edge cases
│       │   └── Pipelines/
│       │       └── PipelineStatusMappingTests.cs             # rejeita duplicatas
│       ├── Features/
│       │   ├── Tickets/                                      # CRUD + transitions + transfer + resolve + filters + search
│       │   ├── Contacts/                                     # CRUD + dedup race test + history pagination
│       │   ├── Pipelines/                                    # rename/reorder/color + provisioning
│       │   ├── TicketCreationGateway/                        # substitui StubTicketCreationGateway tests
│       │   ├── ConcurrentProtocolGeneration/                 # 100 inserções paralelas — 0 duplicatas
│       │   └── Distribution/                                 # adapta testes Spec 005 para novos status
│       ├── Infrastructure/
│       │   ├── Tickets/MongoTicketEventStoreTests.cs
│       │   └── Persistence/Migrations/
│       │       └── Add_Tickets_FullModel_DataMigrationTests.cs # fixture: 5 rows antigas → 5 novas com status correto
│       ├── Jobs/
│       │   ├── TicketSlaMonitorJobTests.cs                   # FakeTimeProvider; warning/breach emitidos uma vez
│       │   └── WaitingClientResumerJobTests.cs
│       └── Helpers/
│           ├── TicketTestHelpers.cs                          # cria ticket completo + contato + pipeline
│           └── FakeTicketEventStore.cs                       # capture-and-assert em memória
│
└── omniDesk.Crm/                                             # Angular 21 — features novas + ajustes
    └── src/app/features/
        ├── tickets-kanban/                                   # NOVO — rota principal do CRM
        │   ├── tickets-kanban.component.ts                   # standalone, signals, lazy
        │   ├── tickets-kanban.component.html
        │   ├── tickets-kanban.component.scss
        │   ├── tickets-kanban.component.spec.ts
        │   ├── components/
        │   │   ├── kanban-column.component.ts                # 1 coluna do pipeline
        │   │   ├── ticket-card.component.ts                  # card draggable
        │   │   ├── sla-badge.component.ts                    # verde/amarelo/vermelho
        │   │   ├── kanban-filters.component.ts               # filtros painel
        │   │   ├── search-bar.component.ts                   # full-text → resultado em lista
        │   │   ├── new-ticket-dialog.component.ts            # criação manual
        │   │   └── reminder-alert-badge.component.ts         # ⚠️ para has_reminder_alert
        │   └── services/
        │       ├── tickets.service.ts                        # signal store + HTTP
        │       └── kanban-websocket.service.ts               # consome 6 eventos novos em /ws/crm
        ├── ticket-detail/                                    # NOVO — detalhe do ticket
        │   ├── ticket-detail.component.ts
        │   ├── ticket-detail.component.html
        │   ├── ticket-detail.component.scss
        │   ├── ticket-detail.component.spec.ts
        │   ├── components/
        │   │   ├── conversation-timeline.component.ts        # reusa Spec 007/008 — mesma renderização
        │   │   ├── internal-notes-section.component.ts       # colapsável, append-only
        │   │   ├── ticket-side-panel.component.ts            # dados + ações + contato
        │   │   ├── transfer-dialog.component.ts              # selecionar atendente/depto + nota
        │   │   ├── inline-status-editor.component.ts         # status pelo painel
        │   │   ├── inline-priority-editor.component.ts
        │   │   ├── tags-editor.component.ts
        │   │   └── sla-countdown.component.ts                # contador com pausa
        │   └── services/
        │       └── ticket-detail.service.ts
        ├── contacts/                                         # NOVO — perfil de contato
        │   ├── contact-profile.component.ts                  # editável + tabs
        │   ├── contact-profile.component.html
        │   ├── contact-profile.component.scss
        │   ├── contact-profile.component.spec.ts
        │   ├── components/
        │   │   ├── contact-form.component.ts                 # edição inline
        │   │   ├── contact-tickets-list.component.ts         # paginação 20/pg
        │   │   └── contact-conversations-list.component.ts   # paginação 20/pg
        │   └── services/contacts.service.ts
        ├── pipeline-config/                                  # NOVO — CRM → Configurações → Pipeline
        │   ├── pipeline-config.component.ts                  # 1 pipeline por depto, edição de colunas
        │   ├── pipeline-config.component.html
        │   ├── pipeline-config.component.spec.ts
        │   └── services/pipeline-config.service.ts
        └── live-chat-inbox/                                  # MODIFICADO (Spec 007)
            ├── live-chat-inbox.component.ts                  # vira lista de conversas SEM ticket atribuído (raras — IA atendendo)
            └── components/
                └── conversation-row.component.ts             # se conversa tem ticket → link para /tickets/{id}
```

**Structure Decision**: Mantém topologia das specs anteriores (Domain / Features / Infrastructure / Hubs em `omniDesk.Api`; `features/*` em `omniDesk.Crm/`). Decisões-chave:

- **`Domain/Tickets/` expande, não duplica**: o scaffold Spec 005 é substituído in-place — Status enum reescrito, novos campos, novas interfaces de repositório. Histórico Git preserva a evolução.
- **`TicketCreationGateway` substitui `StubTicketCreationGateway`**: a interface `ITicketCreationGateway` (Spec 006) evolui (adiciona `Channel`, `ContactHints`), e a implementação stub vai para `_Obsolete/` (mantida 1 sprint para rollback rápido, removida em V1.1). DI rebinds para a real.
- **`Features/Distribution/` MODIFICADO mas não migrado**: a Spec 005 inteira (TicketAssignmentService, PickupTicketEndpoint, SlaCalculator) é estendida — não há motivo para mover para `Features/Tickets/`. `TransferTicketEndpoint` ganha uma referência ao `TransferTicketCommand` em `Features/Tickets/Commands/` (decoradores empilhados — endpoint = camada HTTP, command = orquestração).
- **Kanban como rota raiz do CRM (`/`)**: a Spec 007 entregava `/conversations` como home; agora o home vira `/kanban` (Tickets) e `/conversations` fica como "Inbox" para conversas sem ticket. Roteamento ajustado em `src/omniDesk.Crm/src/app/app.routes.ts`.
- **Frontend reaproveita timeline de mensagens** (`conversation-timeline.component`) já implementada em `live-chat-inbox` Spec 007 — extraída para `shared/components/` e consumida por ambas as features.

## Complexity Tracking

> Apenas violações **justificadas** do Constitution Check. Como o gate passou sem desvios, esta tabela documenta decisões de escopo que podem parecer violações mas não são.

| Decisão | Por que é necessária | Alternativa rejeitada |
|---|---|---|
| Sequence Postgres `ticket_protocol_seq_YYYYMMDD` por tenant/dia (não tabela counter) | Sequence é atômica nativa do banco; concorrência 100% segura sem lock. Counter em tabela exigiria lock pessimista ou `SELECT FOR UPDATE` — pior throughput. | Counter em tabela com lock: rejeitado — gargalo em alto volume. |
| Rename do enum `TicketStatus` (substitui Spec 005) em vez de novo enum + adapter | Spec 005 era admitidamente provisório ("Spec 008 will own the full ticket lifecycle"). Manter dois enums acoplados gera carga cognitiva permanente. Migration consome o débito de uma vez. | Adapter entre enum antigo (Distribution) e novo (Features/Tickets): rejeitado — código vivo permanente carregando dívida técnica de scaffold. |
| `TicketSlaMonitorJob` cron `* * * * *` (a cada minuto) em vez de scheduled per-ticket | Hangfire com agendamento por ticket gera milhões de jobs latentes — Redis lota. Cron varre N tickets ativos em <100ms (≤500 tickets/tenant V1). | Job por ticket: rejeitado em scale; cron `*/5 min`: rejeitado — perde 5min de granularidade no warning. |
| Re-emissão de SLA events idempotente via Redis flag (não tabela) | Flags ephemeral (TTL 24h) suficientes — `sla_warned` reaparece se o serviço Redis cair (aceitável; warning duplicado é benign). Sem custo de DB. | Coluna `sla_warning_emitted` no banco: rejeitado — escrita extra por ticket por monitor tick. |
| Pausa de SLA armazenada como `paused_duration_minutes` int (não como log de pausas) | UI exibe contador único; histórico de cada pausa não tem caso de uso V1. Log denormalizado em `ticket_events` já registra cada transição `waiting_client` — auditoria preservada. | Tabela `ticket_sla_pauses` com (start_at, end_at, duration): rejeitado V1 — overhead sem caso de uso; rastreabilidade via Mongo events. |
| `BackfillTicketProtocolJob` (one-shot) para tickets criados pelo Stub Spec 006 | Stub criou `tickets` sem `protocol` (campo não existia). Após migration, protocol vira NOT NULL — precisa backfill antes do constraint. | Permitir NULL: rejeitado — protocolo é identidade essencial. Recriar tickets: rejeitado — perda de FK em snapshots. |
| `tsvector` em `tickets` mais coluna materializada em messages (não OpenSearch) | Volume V1 (~50k tickets arquivados) cabe folgado em Postgres GIN. Adicionar OpenSearch = novo deploy, novo backup, nova falha. YAGNI. | OpenSearch: rejeitado V1; reavaliar em V2 se p95 search > 1s com volume 10×. |
| Dedup de contato com Redis lock (não unique index DB) | Visitor identifica e-mail/telefone em N requests concorrentes — `INSERT ON CONFLICT` ainda cria contas órfãs com mesmo dado. Lock Redis de 3s elimina race. | Apenas unique index: rejeitado — gera erros 500 em race normal; UX degradada. |
| `home = /kanban` em vez de `/conversations` | Tickets é o foco operacional do atendente V1.x. Conversas sem ticket são exceção (IA ainda em curso). | Manter `/conversations` home: rejeitado — desvia atenção do atendente do trabalho formal. |
