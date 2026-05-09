# Quickstart — Departamentos e Atendentes

Este documento serve a duas audiências:

1. **Desenvolvedor** que vai implementar/estender este módulo.
2. **Revisor / QA** que vai validar manualmente os fluxos críticos antes do release.

---

## A) Adicionar um novo departamento (fluxo do tenant admin)

### Pré-condições

- API rodando local (`dotnet run --project src/omniDesk.Api`).
- CRM rodando (`ng serve --project omniDesk.Crm`).
- Tenant `clinica-x` provisionado (Spec 003).
- Login no CRM como `tenant_admin`.

### Passos

1. Menu **Times → Departamentos → Novo**.
2. Preencher: nome `Suporte`, dias úteis `Seg–Sex`, expediente `09:00–18:00`, SLA primeira resposta `30 min`, SLA resolução `4 h`.
3. Salvar. Esperado: redirect para detalhe; toast "Departamento criado".
4. No detalhe, clicar **Vincular atendente** → escolher atendente cadastrado → marcar como **principal**.

### Critérios de aceite

- O departamento aparece em `GET /api/departments` com `attendant_count >= 1`.
- O atendente aparece em `GET /api/departments/{id}/attendants` com `is_primary_department=true`.
- Tabela `tenant_clinica_x.departments` tem a linha; tabela `attendant_departments` tem o vínculo com `is_primary=true`.

---

## B) Verificar round-robin de distribuição automática

### Pré-condições

- 1 departamento `Comercial` ativo.
- 3 atendentes vinculados, todos `online`, todos com `max_simultaneous_chats=5` e 0 tickets ativos.

### Passos

1. Disparar 6 chamadas `POST /api/tickets` (Spec 008 mock — pode ser via fixture de teste) com `department_id = Comercial.id`.
2. Após cada chamada, capturar `data.assigned_attendant_id`.

### Resultado esperado

- 6 tickets distribuídos exatamente: 2 para cada atendente, em sequência alternada (`A, B, C, A, B, C` ou rotação consistente).
- Diferença máxima de tickets por atendente = **0** (SC-003 satisfeito).
- Eventos WebSocket `ticket.assigned` recebidos no canal `attendant:{id}` correspondente.

### Métrica em CI

Teste `RoundRobinCursorTests.DistributesEvenlyAcross100Tickets` em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Distribution/`.

---

## C) Verificar lock de concorrência no "Assumir"

### Cenário

1 ticket em fila, 2 atendentes na mesma sala tentando assumir simultaneamente.

### Setup

```bash
# Terminal 1 — atendente A
curl -X POST -H "Authorization: Bearer $TOKEN_A" \
     http://localhost:5000/api/tickets/$TICKET_ID/pickup &

# Terminal 2 — atendente B (disparado < 50 ms depois)
curl -X POST -H "Authorization: Bearer $TOKEN_B" \
     http://localhost:5000/api/tickets/$TICKET_ID/pickup &
wait
```

### Resultado esperado

- Exatamente 1 dos terminais responde 200 com `assigned_attendant_id` igual ao caller.
- O outro responde 409 `ALREADY_PICKED_UP` em ≤ 200 ms.
- Banco mostra apenas uma atribuição.

### Métrica em CI

Teste `TicketLockTests.RejectsConcurrentAcquisitions` simula 50 pares concorrentes. SC-002 = **0** atribuições duplicadas.

---

## D) Verificar timeout de presença (online → away → offline)

### Pré-condições

- Atendente `online`, com `last_heartbeat_at` recente.

### Passos

1. Parar o CRM (fechar aba) sem clicar em logout.
2. Aguardar **15 minutos**.
3. Verificar:
   - Job Hangfire `PresenceTimeoutJob` rodou (logs).
   - `attendant_status` no Postgres: `status=away, changed_by=system`.
   - Mongo `{slug}_attendant_status_logs` tem documento com `from_status=online, to_status=away`.
   - Painel do supervisor (em outra aba) mostra a mudança em ≤ 1 s após o tick do job.
4. Aguardar mais **30 minutos** (total 45).
5. Repetir as verificações para `away → offline`.

### Métrica em CI

`PresenceTimeoutJobTests` simula relógio com `IClock` mockável e valida ambas as transições + emissão de eventos.

---

## E) Verificar comportamento de transbordo

### Casos da matriz (Spec 005 §3.3)

| Situação | Setup | Esperado |
|---|---|---|
| Dentro do horário + alguém online | `Comercial` 09–18, atendente A online às 14:00, IA decide handoff | Ticket atribuído a A |
| Dentro do horário + ninguém online | Atendente A `offline`, IA decide handoff às 14:00 | Ticket fica `queued`, IA fala "todos ocupados" |
| Fora do horário + alguém online | Atendente A `online` às 23:00 | Ticket atribuído a A normalmente (online tem prioridade) |
| Fora do horário + ninguém online | 23:00, sem ninguém | Ticket `queued`, IA fala "atendemos seg-sex 09–18" |

### Validação

- Resposta da IA contém o texto correto (verificar em `{slug}_messages` do Mongo, conversa do cliente).
- Status do ticket em `tickets`: `assigned` ou `queued` conforme tabela.
- Evento WebSocket emitido no canal correto.

---

## F) Verificar substituição de variáveis em canned response

### Pré-condições

- Resposta cadastrada: `Olá {{client_name}}, sou {{attendant_name}} do {{department_name}}. Seu ticket é #{{ticket_number}}.`
- Conversa ativa: cliente "Maria Silva", atendente "Carlos", ticket #4321, dept "Comercial".

### Passos

1. No chat, digitar `/saudacao` → escolher a resposta.
2. Verificar preview no campo de texto.

### Resultado esperado

`Olá Maria Silva, sou Carlos do Comercial. Seu ticket é #4321.`

### Casos de borda

- Cliente sem nome cadastrado (anônimo): `cliente` (fallback FR-034).
- Variável desconhecida `{{foo_bar}}`: preservada literalmente; log `Warning` no Serilog.

---

## G) Verificar sugestão de IA com aprovação humana

### Pré-condições

- Conversa ativa com 5 mensagens.
- Departamento tem sub-agente "Suporte" vinculado (Spec 002).
- `OPENAI_API_KEY` válida em ambiente.

### Passos

1. Atendente clica **Sugerir resposta com IA** no chat.
2. Aguardar resposta (≤ 3 s).
3. Verificar que aparece **preview editável**, **não** mensagem na conversa.
4. Editar texto, clicar **Enviar**.
5. Verificar que a mensagem chega ao cliente exatamente como editada.
6. Conferir no Mongo `{slug}_ai_suggestion_logs`: documento com `human_action=edited`, `final_message_text` igual à versão enviada.

### Resultado esperado

- Cliente **nunca** recebe a sugestão original automaticamente (SC-007 = 0).
- Log Mongo registra a ação humana.

### Caso de erro do provedor

1. Bloquear o tráfego para `api.openai.com` (ex.: hosts file).
2. Clicar em sugerir.
3. Esperado: toast "Não foi possível gerar sugestão agora. Tente novamente em alguns segundos.".
4. Conversa segue normal — nada quebra.

---

## H) Como esta spec é consumida pelas demais

| Spec | Ponto de consumo |
|---|---|
| Spec 002 (Agentes IA) | Recebe disponibilidade do departamento (R5) e prompt do sub-agente para sugestão IA (R6) |
| Spec 004 (Roles e Permissões) | `attendant_departments` popula `dept_ids` na claim do JWT (R8) — `DepartmentScopeFilter` da Spec 004 passa a filtrar tickets por esse vínculo |
| Spec 008 (Tickets) | Consome `TicketAssignmentService` para atribuição automática; consome `BusinessHoursEvaluator` para SLA |
| Spec 011 (Auditoria) | Lê `{slug}_attendant_status_logs` e `{slug}_ai_suggestion_logs` |

---

## I) Rodar testes localmente

```bash
# Backend (xUnit + Testcontainers — exige Docker)
dotnet test src/omniDesk.Api/tests/omniDesk.Api.Tests \
  --filter "FullyQualifiedName~Distribution|Departments|Attendants|CannedResponses|AiSuggestions"

# Frontend (Karma + Jasmine)
cd src/omniDesk.Crm
ng test --watch=false --include='src/app/features/{departments,attendants,canned-responses,ticket-queue,ai-suggestion}/**/*.spec.ts'
```

---

## J) Ambiente de desenvolvimento

| Variável | Valor recomendado em dev |
|---|---|
| `MAX_SUGGESTION_CONTEXT_MESSAGES` | `20` |
| `OPENAI_API_KEY` | conta de dev com cap de gasto |
| `REDIS_URL` | `redis://localhost:6379` |
| `MONGODB_URL` | `mongodb://localhost:27017` |

Em produção, garantir que **todas as conexões Redis/Mongo/OpenAI** usem TLS e que `OPENAI_API_KEY` seja específica do ambiente (nunca compartilhada com staging).
