# Research: Agenda e Catálogo de Serviços (Spec 011)

**Branch**: `011-agenda-services` | **Phase**: 0 | **Date**: 2026-05-12

Phase 0 do plano: para cada decisão técnica não óbvia, documenta-se aqui a alternativa escolhida, o porquê e o que foi descartado. Resolve todos os pontos que ficariam como `NEEDS CLARIFICATION` em `Technical Context`.

---

## R1 — Algoritmo de cálculo de disponibilidade

**Decisão**: cálculo síncrono no backend via uma única query PostgreSQL parametrizada que executa:

```text
1. SELECT turnos do dia em weekly_schedules pelo day_of_week
2. LEFT JOIN/EXCEPT contra schedule_blocks (intervalos no dia)
3. LEFT JOIN/EXCEPT contra appointments (status IN pending/confirmed) do profissional no dia
4. Gera slots em memória C# de N em N minutos (duration_minutes do serviço) dentro dos turnos restantes
```

O serviço `AvailabilityCalculator` carrega os 3 conjuntos (~poucas dezenas de linhas no pior caso) e faz a subtração + slot-gen em memória. Sem materialização persistente de slots.

**Por quê**:

- Volume baixo: tipicamente <10 turnos no dia, <2 bloqueios no mês para o profissional, <30 agendamentos no dia.
- Disponibilidade muda a cada criação/cancelamento — cache invalidation seria mais custosa que o recalcule.
- Mesmo serviço serve `GET /api/availability` (REST) e a tool call `check_availability` (IA) — fonte única (FR-018).
- Performance suficiente para SC-008 (<500ms p95) sem cache, conforme medições em projetos análogos com EF Core + PG (consulta indexada por `(professional_id, start_at)` é trivial).

**Alternativas descartadas**:

| Opção | Por quê descartada |
|---|---|
| Materializar slots em tabela `available_slots` mantida via triggers | Complexidade de invalidação alta; risco de inconsistência; YAGNI para o volume V1. |
| Cache Redis com TTL curto (60s) | Adiciona inconsistência durante a janela; cliente vê slot livre que outro acabou de tomar; pior UX que recalcular. |
| Cálculo client-side (atendente recebe weekly_schedule + blocks + appointments e calcula no navegador) | Duplica lógica entre TypeScript e C#; IA precisaria de outra implementação; viola "fonte única" (FR-018). |

**Implementação**:

```csharp
public sealed class AvailabilityCalculator
{
    public async Task<IReadOnlyList<Slot>> GetSlotsAsync(
        Guid professionalId, Guid serviceId, DateOnly localDate, CancellationToken ct)
    {
        // 1. Carrega service.duration_minutes (cache de request em memória)
        // 2. Carrega turnos do day_of_week
        // 3. Carrega bloqueios que intersectam o dia
        // 4. Carrega appointments do dia status IN (pending_confirmation, confirmed)
        // 5. Para cada turno: gera slots de duration em duration; remove os que colidem com blocks ou appointments
        // 6. Retorna IReadOnlyList<Slot> ordenada
    }
}
```

---

## R2 — Proteção contra race condition na criação de agendamentos

**Decisão**: estratégia em 3 camadas defensivas:

1. **Redis SETNX lock** (`{slug}:appointment_slot_lock:{prof}:{start_iso}`, TTL 10s) — primeira barreira, latência sub-milissegundo, falha rápido.
2. **Index parcial UNIQUE** em `appointments(professional_id, start_at) WHERE status IN ('pending_confirmation','confirmed')` — barreira no DB; `unique_violation` (23505) traduzida em `APPOINTMENT_SLOT_CONFLICT`.
3. **Revalidação dentro da transação** — `CreateAppointmentCommand` faz `SELECT ... FOR UPDATE` no profissional + verifica novamente bloqueios e conflitos antes do `INSERT`.

**Por quê**:

- Lock Redis evita carga desnecessária no DB para conflitos comuns (~99% dos casos param na camada 1).
- UNIQUE constraint é a verdadeira fonte de verdade — Redis pode falhar (TTL expirado, eviction); o DB nunca permite duplicata.
- `FOR UPDATE` previne escalada para `unique_violation` na maioria das colisões, dando erro semântico mais cedo (FR-024 `APPOINTMENT_OUTSIDE_AVAILABILITY` vs FR-023 `APPOINTMENT_SLOT_CONFLICT` são diferentes mensagens UX).

**Alternativas descartadas**:

| Opção | Por quê descartada |
|---|---|
| Apenas `SELECT FOR UPDATE` (sem Redis) | Pega lock no DB para todas as tentativas; sob carga concorrente perde para a opção híbrida. |
| Apenas `INSERT ... ON CONFLICT DO NOTHING` | Não distingue "slot ocupado" de "fora da disponibilidade"; pior mensagem de erro. |
| Application-level mutex (`SemaphoreSlim` por profissional) | Não funciona em horizonte multi-instância (várias APIs); falha o critério de horizontalidade. |
| Hangfire SerialQueue por profissional | Mata a UX de criação síncrona (cliente espera fila); overkill. |

**Edge case — Redis offline**: lock fail é log warning; código continua para o DB. UNIQUE constraint protege. Pior caso: dois requests vêem disponibilidade, ambos chegam ao DB, um vence, o outro recebe 409. Aceitável.

---

## R3 — Detecção de overlap entre bloqueios e agendamentos

**Decisão**: usa **`btree_gist`** + índice GIST sobre `tstzrange` em `schedule_blocks`:

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE INDEX idx_schedule_blocks_overlap
  ON tenant_{slug}.schedule_blocks
  USING gist (professional_id, tstzrange(start_at, end_at, '[)'));
```

Query de overlap (usada quando admin cria bloqueio):

```sql
SELECT id FROM tenant_{slug}.appointments
WHERE professional_id = @prof
  AND status IN ('pending_confirmation', 'confirmed')
  AND tstzrange(start_at, end_at, '[)') && tstzrange(@block_start, @block_end, '[)')
```

**Por quê**:

- PG suporta tstzrange como tipo nativo (16+); operador `&&` é "overlaps" canônico.
- Index GIST torna a query O(log n) mesmo em tenants com milhares de appointments.
- Reaproveita extension já comum (alguns tenants podem já ter habilitado por outros usos); migration habilita idempotente.

**Alternativas descartadas**:

| Opção | Por quê descartada |
|---|---|
| Comparações manuais `start_at < @block_end AND end_at > @block_start` | Funciona, mas força full scan ou usa só metade do índice composto. Performance pior em larga escala. |
| Storing como `daterange[]` em campo único | Complica reads pontuais; CHECK constraints mais fracas. |
| Materializar slots ocupados em tabela auxiliar | Mesmo problema do R1 (duplicação de estado). |

**Compatibilidade**: PG 14+ tem `tstzrange` estável; o projeto roda em PG 16 (constituição §Tech Stack). OK.

---

## R4 — Normalização de "NÃO" no webhook WhatsApp

**Decisão**: comparação ignora caixa e remove diacríticos antes de comparar com a string canônica `"nao"`. Aceita:

- `"NÃO"`, `"NÂO"`, `"NAO"`, `"nao"`, `"não"`, `"Não"`, `"Nao"`, com ou sem espaços/quebras de linha em volta.

Implementação:

```csharp
static readonly string CancelToken = "nao";

public static bool IsCancelToken(string message)
{
    if (string.IsNullOrWhiteSpace(message)) return false;
    var normalized = message.Trim().Normalize(NormalizationForm.FormD);
    var withoutDiacritics = new string(normalized.Where(c =>
        System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
            System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    return string.Equals(withoutDiacritics, CancelToken, StringComparison.OrdinalIgnoreCase);
}
```

**Por quê**:

- A spec assumida (FR-032) define explicitamente lowercase + sem acentos.
- Não aceita variações amplas ("n", "cancelar", "desmarcar") na V1 — risco de falso positivo (cliente respondendo "n[ada]"). Spec marca como evolução futura.
- Algoritmo simples, testável, sem dependência externa.

**Alternativas descartadas**:

| Opção | Por quê descartada |
|---|---|
| Aceitar lista ampla (`["nao", "n", "cancelar", "desmarcar", "cancela"]`) | Spec explicitamente diz que "NÃO" é o gatilho; ampliar é mudança de produto, não de implementação. |
| Usar NLU/embedding para detectar intenção | Overkill para 1 palavra; latência adicional; não auditável; viola V (Simplicity). |
| Aceitar tudo que começa com "n" minúsculo | Falso positivo trivial. |

---

## R5 — Determinação autoritativa de `client_type`

**Decisão**: backend determina `client_type` em `ClientTypeResolver.ResolveAsync(contactId, ct)`:

```text
1. SE contact_id IS NULL OR NOT EXISTS contato — returning_client é impossível → new_client
2. SELECT EXISTS (
       SELECT 1 FROM tenant_{slug}.appointments
       WHERE contact_id = @contact
         AND status IN ('confirmed', 'no_show')
   )
3. true → returning_client; false → new_client
```

`client_type` informado pelo payload (CRM ou IA) é **completamente ignorado** — backend é autoritativo (FR-020). A spec da tool call exige o campo apenas para que a IA possa demonstrar coerência narrativa ("vou marcar pra você como cliente novo"), mas a fonte da verdade é a query.

**Por quê**:

- Protege contra alucinação da IA (cenário real: IA chama `create_appointment(client_type="returning_client")` quando o contato é novo).
- Protege contra atendente errar manualmente.
- O cálculo é O(1) com o index `(contact_id, status, start_at)` previsto.

**Alternativas descartadas**:

| Opção | Por quê descartada |
|---|---|
| Confiar no `client_type` enviado | Fere FR-020; risco de status errado (cliente novo virando confirmed direto sem confirmação do atendente). |
| Materializar `is_returning_client` na tabela `contacts` | Estado redundante; risco de drift; trigger custosa. |
| Validar com cache LRU | Premature optimization; query é O(1). |

**Edge case — agendamento em andamento na mesma transação**: se um cliente novo cria o agendamento atual, a query de `client_type` é executada **antes** do INSERT — então mesmo após criar, ele ainda é classificado como `new_client` para este agendamento. Comportamento esperado. Para o próximo agendamento, ele já será `returning_client` (porque o atual já estará em `pending_confirmation` ou `confirmed` — wait, `pending_confirmation` não conta). Revisão: a regra usa `status IN (confirmed, no_show)`, então `pending_confirmation` NÃO promove a retornante. Isto é intencional: cliente só vira "retorno" depois de ter algo confirmado/comparecido.

---

## R6 — Registro das tool calls da IA

**Decisão**: registrar `CheckAvailabilityTool` e `CreateAppointmentTool` via `IToolRegistry` (Spec 006), adicionando em `ToolRegistry.RegisterDefaults()` de forma condicional (`if (tenantHasAgenda)`).

**Por quê**:

- Spec 006 já tem o padrão. Não duplica infra.
- Tools são opt-in por tenant; tenant que não usa agenda não vê as ferramentas no prompt.
- Mesmo arquivo de configuração (`ai_settings`) governa.

**Detalhes**:

- `CheckAvailabilityTool` recebe `(professional_id?: Guid, service_id: Guid, date: string YYYY-MM-DD)`. Se `professional_id` for `null`, retorna slots de **todos** os profissionais que oferecem o serviço (lista limitada a 10 para não estourar o prompt). Decisão: V1 exige `professional_id` (alinhado com input do usuário); a flexibilidade fica em V2+.
- `CreateAppointmentTool` recebe o payload da spec; descarta `client_type`; preenche `created_by = ai`, `conversation_id` (do contexto do agente), `contact_id` (busca ou cria por telefone E.164).

**Alternativas descartadas**:

| Opção | Por quê descartada |
|---|---|
| Tools sempre registradas; tenant sem agenda recebe erro | UX ruim no orquestrador; IA tentaria usar e falharia visivelmente ao cliente. |
| Tools como handlers HTTP separados (sem IToolRegistry) | Duplica infra; viola padrão de Spec 006. |

---

## R7 — Edição de agendamento: `start_at` ou `service_id` mudou

**Decisão**: edição que altera `start_at` ou `service_id` é tratada como **cancelamento implícito + recriação** lógica, mas mantém o mesmo `id` para preservar histórico e referências externas.

Fluxo no `UpdateAppointmentCommand`:

1. Carrega appointment atual; lock Redis no novo slot (se mudou).
2. Revalida disponibilidade no novo slot (incluindo o próprio agendamento na exclusão para não conflitar consigo mesmo).
3. Atualiza `start_at`, `service_id`, recalcula `end_at`, registra em `appointment_events` com `action = 'rescheduled'`.
4. **NÃO** dispara `appointment_confirmation` novamente (UX: cliente já confirmou; mudança de horário é informada por outro canal — UI mostra alerta para atendente). Decisão consciente de não automatizar.

**Por quê**:

- Mantém referências (`ticket_id`, `conversation_id`, `appointment_events`) intactas.
- Preserva continuidade de auditoria.
- Evita disparo duplo de confirmação que confunde cliente.

**Alternativas descartadas**:

| Opção | Por quê descartada |
|---|---|
| Bloquear edição de `start_at` (forçar cancelar + criar novo) | UX ruim — atendente clica "reagendar" e o sistema cria 2 registros; quebra a expectativa. |
| Auto-disparar `appointment_confirmation` em toda edição | Spam de mensagens; cliente recebe múltiplas confirmações; ruído operacional. |
| Endpoint dedicado `POST /reschedule` | Reduz a API surface, mas duplica lógica de `UpdateAppointmentCommand`. |

---

## R8 — Política de visibilidade de agendamentos

**Decisão**: implementar `IAppointmentVisibilityPolicy.CanView(currentUser, appointment) → bool` com a seguinte tabela:

| Role | Regra |
|---|---|
| `TenantAdmin` | Sempre vê todos. |
| `Supervisor` | Vê se `professional.department_id ∈ supervisor.departments` OR `appointment.ticket.department_id ∈ supervisor.departments`. |
| `Attendant` | Vê se `professional.attendant_id == user.attendant_id` OR `appointment.ticket.department_id ∈ user.departments`. |

Aplicada em:

- Listagem (`GET /api/appointments`) — adiciona filtro SQL.
- Detalhe (`GET /api/appointments/{id}`) — após carregar, valida; 403 `NOT_AUTHORIZED` se falhar.
- Comandos (confirm/cancel/no-show/edit) — mesma validação antes de processar.

**Por quê**:

- Espelha a política de visibilidade da Spec 009 (tickets por departamento).
- Profissional vinculado a atendente é o caso "minha agenda pessoal" — atendente vê seus próprios agendamentos mesmo que de departamentos que não pertence.

**Alternativas descartadas**:

| Opção | Por quê descartada |
|---|---|
| Todos os atendentes veem todos os agendamentos do tenant | Vaza dados entre departamentos; viola Princípio I (intra-tenant também precisa de boundary lógico). |
| Só profissional vê seus próprios agendamentos | Quebra recepção/secretaria que agenda para o médico. |

---

## R9 — `agenda_settings` como singleton tenant-scoped

**Decisão**: tabela `tenant_{slug}.agenda_settings` com PK fixa via `CHECK (id = 1)` (singleton enforced). Default row inserida pela própria migration.

```sql
CREATE TABLE tenant_{slug}.agenda_settings (
    id smallint PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    late_cancel_window_hours int NOT NULL DEFAULT 24,
    late_cancel_text text NOT NULL DEFAULT 'Cancelamentos com menos de 24h poderão ser cobrados.',
    cancellation_policy_text text NOT NULL DEFAULT '',
    updated_at timestamptz NOT NULL DEFAULT now()
);
INSERT INTO tenant_{slug}.agenda_settings (id) VALUES (1) ON CONFLICT DO NOTHING;
```

**Por quê**:

- Pattern simples; `SELECT * FROM agenda_settings` sempre retorna 1 linha.
- Endpoint `PUT` faz upsert mas, na prática, sempre é UPDATE.
- Diferente de `tenant_notification_settings` (Spec 010), que vive em `public.*` porque é cfg sobre o tenant — `agenda_settings` é cfg operacional do CRM dentro do tenant.

**Alternativas descartadas**:

| Opção | Por quê descartada |
|---|---|
| Linha sem PK numérica (qualquer coisa em UUID PK + endpoint busca por LIMIT 1) | Permite múltiplas linhas por bug; pior data hygiene. |
| Tabela `tenant_settings` genérica chave-valor | Sem tipagem de campos; validação por código; pior DX. |
| `public.tenant_agenda_settings` | Mistura responsabilidade de schema; agenda é feature do tenant, não metadado. |

---

## R10 — Dependência da Spec 010 (`IAppointmentReadRepository`)

**Decisão**: manter o `IAppointmentReadRepository` da Spec 010 **inalterado** (mesma interface, mesmo DTO). Spec 011 apenas faz a tabela `appointments` existir com os campos esperados.

Compatibilidade verificada:

| Campo esperado pelo DTO de 010 | Origem em 011 |
|---|---|
| `Id` | `appointments.id` |
| `ContactId` | `appointments.contact_id` |
| `ScheduledFor` (DateTimeOffset) | `appointments.start_at` |
| `Status` | `appointments.status` (mapping: `'confirmed'` → enviar lembrete) |
| `TicketId` | `appointments.ticket_id` |
| `DepartmentId` | derivado de `appointments.professional → professional.department_id` (LEFT JOIN) |
| `ProfessionalName` | derivado de `appointments.professional → professional.name` |

O `AppointmentReadRepository` atual lê via SQL bruto com `LEFT JOIN`. Spec 011 mantém este código intocado — apenas remove o `LogWarning` de "tabela não existe" porque ela passará a existir.

**Por quê**:

- Minimiza acoplamento entre specs.
- Permite Spec 010 continuar funcionando sem refactor.
- Permite upgrade futuro (Spec 011 v1.1) para usar EF Core proper sem mudar Spec 010.

**Alternativas descartadas**:

| Opção | Por quê descartada |
|---|---|
| Refazer `IAppointmentReadRepository` em EF Core agora | Risco de regressão em Spec 010 (já mergeada e implementada); refactor escopo expandido sem ganho mensurável. |
| Mover o repositório para `Features/Agenda/` | Quebra a importação de Spec 010; cria dependência circular conceitual. |

---

## R11 — Cancelamento via WhatsApp: múltiplos agendamentos elegíveis

**Decisão**: quando 2+ appointments qualificam (mesmo `conversation_id`, status `confirmed`, `reminder_sent_at` na janela de 26h), cancelar **apenas o de `start_at` mais cedo**.

Justificativa da escolha:

- Mais provável que o lembrete tenha sido enviado para o agendamento mais próximo no tempo.
- Resposta WhatsApp menciona explicitamente a data/hora do cancelado, eliminando ambiguidade para o cliente.
- Se o cliente queria cancelar o outro, ele pode (a) re-responder "NÃO" novamente em até 26h após o próximo lembrete, ou (b) entrar em contato com o atendente.

**Por quê**:

- Único agendamento elegível por chamada minimiza confusão.
- Comportamento determinístico e auditável.
- Mensagem ao cliente sempre clara: "Agendamento de quinta 09:00 foi cancelado."

**Alternativas descartadas**:

| Opção | Por quê descartada |
|---|---|
| Cancelar todos os elegíveis | Pode cancelar agendamentos que o cliente NÃO queria cancelar; risco operacional alto. |
| Não cancelar nada e perguntar "qual?" | Perde a simplicidade do flow "NÃO"; UX pior. |
| Cancelar o agendamento que originou o último lembrete | Requer rastrear qual appointment originou cada mensagem template; complexidade adicional. |

---

## R12 — Métricas operacionais para Spec 011

**Decisão**: expor as seguintes métricas Prometheus via `IMetrics` (já em uso desde Spec 010):

| Métrica | Tipo | Labels | Origem |
|---|---|---|---|
| `appointments_created_total` | Counter | `tenant`, `source` (`ai`/`attendant`), `status_inicial` | `CreateAppointmentCommand` |
| `appointment_cancellations_total` | Counter | `tenant`, `by` (`client`/`attendant`/`system`), `channel` (`crm`/`whatsapp`) | `CancelAppointmentCommand` + `CancelAppointmentByClientCommand` |
| `appointment_no_show_total` | Counter | `tenant` | `MarkNoShowCommand` |
| `availability_query_duration_seconds` | Histogram | `tenant` | `AvailabilityCalculator.GetSlotsAsync` |
| `reminder_response_no_total` | Counter | `tenant`, `outcome` (`cancelled`/`ignored_outside_window`/`no_match`) | `ReminderResponseInterpreter` |
| `appointment_slot_conflict_total` | Counter | `tenant`, `layer` (`redis`/`unique_violation`) | `CreateAppointmentCommand` |

**Por quê**:

- Cobre os SC mensuráveis da spec (SC-005, SC-006, SC-008).
- Permite detectar regressão em produção (ex.: spike em `appointment_slot_conflict_total{layer=unique_violation}` sinaliza problema no lock Redis).

**Alternativas descartadas**: nenhuma — métricas são aditivas, sem trade-off relevante.

---

## Resumo Executivo das Decisões

| # | Decisão | Risco se errar |
|---|---|---|
| R1 | AvailabilityCalculator síncrono, sem cache | Performance ruim em tenants gigantes — mitigado por índices e volume V1 baixo. |
| R2 | Redis SETNX + UNIQUE parcial + FOR UPDATE | Overbooking se Redis e DB falharem juntos — extremamente improvável. |
| R3 | btree_gist + tstzrange para overlap | Migration falha se PG não tem extension — habilitada idempotentemente. |
| R4 | "nao" normalizado, apenas variações próximas | Cliente que escreve outra palavra não cancela — comportamento esperado. |
| R5 | client_type autoritativo no backend | Hallucination da IA não corrompe status — desejado. |
| R6 | Tools opt-in por tenant via IToolRegistry | Tenant sem agenda não vê tool — desejado. |
| R7 | Edição mantém id e não re-confirma | Cliente não é spammado, mas precisa ser avisado por outro canal — atendente responsável. |
| R8 | Visibility policy espelha Spec 009 | Atendente sem departamento não vê — desejado, igual a tickets. |
| R9 | `agenda_settings` singleton via CHECK (id=1) | Múltiplas linhas é impossível — CHECK previne. |
| R10 | `IAppointmentReadRepository` inalterado | Spec 010 não regressão — verificado por contract test. |
| R11 | Cancelar o mais cedo entre elegíveis | Cliente confuso se quis cancelar outro — mensagem mitiga. |
| R12 | 6 métricas Prometheus | Sem visibilidade de problemas — mitigado pelas próprias métricas. |

**Status**: ✅ Sem `NEEDS CLARIFICATION` remanescente. Phase 0 concluída. Pronto para Phase 1.
