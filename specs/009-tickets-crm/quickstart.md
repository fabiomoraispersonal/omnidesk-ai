# Quickstart — Validação Manual da Spec 009 Tickets/CRM

Roteiros de validação end-to-end que devem ser executáveis após a implementação. Pretende cobrir os caminhos felizes das 9 user stories. Cada roteiro é independente e contém pré-condições, passos numerados e critério de sucesso explícito.

**Setup base** (uma vez, para todos os roteiros):

- Tenant `clinica-abc` provisionado (Spec 003), com:
  - Departamento "Comercial" (SLA: 15min first response, 60min resolution).
  - Departamento "Financeiro" (SLA: 30min first response, 240min resolution).
  - 2 atendentes em Comercial: Maria (`tenant_attendant`) e Carlos (idem).
  - 1 atendente em Financeiro: Ana.
  - 1 supervisor: Beatriz.
  - 1 admin: Fabio.
  - Agentes IA de Spec 006 ativos e configurados.
  - Pipelines (3 colunas default) criados automaticamente pelo provisioning.

---

## QS1 — Abertura automática de ticket por transbordo da IA (US1)

**Pré-condição**: Maria está online no CRM em `/kanban`.

1. Abrir widget de Live Chat (Spec 007) em browser anônimo.
2. Aceitar termos LGPD e identificar-se: `email = joao.silva@email.com`, `name = João Silva`.
3. Enviar mensagens: "Olá, gostaria de remarcar meu agendamento" → IA responde.
4. Após algumas trocas, enviar: "Quero falar com um atendente".

**Critério de sucesso**:

- Em ≤ 2s o card aparece no Kanban de Maria (coluna "Em Andamento" — porque foi atribuída por round-robin).
- O card mostra:
  - Protocolo `TK-YYYYMMDD-XXXXX`.
  - Nome "João Silva", canal 💬 Live Chat.
  - Subject preenchido com as primeiras 100 chars da última mensagem da IA.
  - Atendente "Maria Silva".
  - Badge SLA 🟢 verde.
- Maria recebe toast "Novo ticket atribuído: TK-...".
- Maria clica no card → vê histórico completo da conversa (mensagens da IA + cliente em ordem cronológica).
- Após Maria responder pela primeira vez, `first_response_at` é preenchido (não verificável no UI direto; via `GET /api/tickets/{id}/events` filtrar pelo timestamp da mensagem).

---

## QS2 — Sem atendente disponível → ticket fica na fila (US1)

**Pré-condição**: Maria e Carlos estão offline. Departamento Comercial tem 0 atendentes online.

1. Repetir QS1 passos 1–4.

**Critério de sucesso**:

- Ticket criado com `attendant_id = null`, `status = new`.
- Card aparece na coluna "Na Fila" do Kanban (visível para Beatriz/supervisor mas para nenhum atendente offline).
- Beatriz vê: "Sem atendente" em cinza no card.

**Continuação**:

2. Maria entra no CRM (fica online).

**Critério**: Em ≤ 5s o card mais antigo na fila do Comercial migra para "Em Andamento" e é atribuído a Maria.

---

## QS3 — Drag-drop entre colunas muda status (US2)

**Pré-condição**: QS1 completou; Maria está com ticket `TK-...` em "Em Andamento".

1. Maria responde algo ao cliente.
2. Maria arrasta o card de "Em Andamento" para "Aguardando Cliente".

**Critério de sucesso**:

- Card visualmente em "Aguardando Cliente" imediatamente (optimistic update).
- `GET /api/tickets/{id}` retorna `status = waiting_client`, `waiting_client_since = now()`.
- Evento WebSocket `ticket.status_changed` recebido (verificável em DevTools console).
- SLA de resolução pausa: contador no card congela.

**Continuação**:

3. Cliente envia nova mensagem no widget: "Confirmado, pode marcar."

**Critério**:

- Em ≤ 2s o card volta automaticamente para "Em Andamento".
- `sla_paused_duration_minutes` incrementa pelos minutos em `waiting_client`.
- `waiting_client_since` zera.

---

## QS4 — SLA warning (80%) e breach (US3)

**Pré-condição**: ticket Maria criado, departamento Comercial com `sla_first_response_minutes = 15`. Maria **não** responde.

1. Esperar 12 minutos (80% de 15min).

**Critério**:

- Badge SLA no card vira 🟡 amarelo.
- Maria recebe toast warning ⚠️ "SLA de primeira resposta atingiu 80% — TK-...".
- Evento WebSocket `ticket.sla_warning` recebido.

**Continuação**:

2. Esperar mais 3 minutos (total 15min — SLA expira).

**Critério**:

- Badge SLA vira 🔴 vermelho.
- Maria recebe toast danger 🔴 "SLA de primeira resposta expirado — TK-...".
- `GET /api/tickets/{id}/events` mostra evento `sla_breached` com `sla_type = first_response`.
- Beatriz (supervisor) também recebeu o evento.

---

## QS5 — Transferência entre departamentos com recálculo de SLA (US4)

**Pré-condição**: Maria com ticket Comercial atribuído.

1. Maria clica "Transferir" no painel direito.
2. Seleciona "Departamento = Financeiro", escreve nota "Cliente quer renegociar contrato".
3. Confirma.

**Critério**:

- Ticket sai do Kanban de Maria.
- Ana (Financeiro) recebe toast + card aparece em "Em Andamento" do seu Kanban (atribuída via round-robin).
- `GET /api/tickets/{id}` mostra: `department_id = Financeiro.id`, `sla_first_response_deadline = now + 30min` (meta do Financeiro), `sla_resolution_deadline = now + 240min`, `sla_paused_duration_minutes = 0` (zerado).
- `first_response_at` (se já existia) preservado.
- Auto-criação de `ticket_note` com o conteúdo "Cliente quer renegociar contrato", autor = Maria.
- Evento Mongo `transferred` registrado com `from_department_id` e `to_department_id`.

---

## QS6 — Criação manual de ticket (US5)

**Pré-condição**: Maria está logada.

1. Maria clica "+ Novo Ticket".
2. Modal abre. Maria busca contato "Júlia": nenhum resultado.
3. Maria clica "Criar novo contato": `name = Júlia Pereira`, `phone = (11) 98888-7777`.
4. Preenche resto: departamento Comercial, subject "Pediu retorno", priority High, tag "telefone".
5. Marca "Atribuir a mim". Confirma.

**Critério**:

- Card aparece em "Em Andamento" de Maria.
- `channel = manual`, `conversation_id = null`, `contact_id` preenchido.
- Contato criado em `contacts` com `name`, `phone`, `phone_normalized = 5511988887777`.

**Continuação — dedup**:

6. Outro atendente repete o fluxo informando o mesmo telefone.

**Critério**: o contato existente é reutilizado (mesmo `id`), `source_channels` ganha "manual" se ainda não tinha.

---

## QS7 — Perfil do contato com histórico paginado (US6)

**Pré-condição**: contato "João Silva" com 25 tickets antigos + 8 conversas.

1. Maria abre detalhe de um ticket de João.
2. Clica no nome "João Silva" no painel direito → navega para `/contacts/{id}`.

**Critério**:

- Tela mostra dados editáveis (nome, e-mail, telefone, observações, source_channels).
- Aba "Tickets" mostra página 1 com 20 itens, mais recentes primeiro.
- Paginação permite navegar para página 2 (5 tickets restantes).
- Aba "Conversas" mostra 8 conversas paginadas.
- Clicar em um ticket antigo abre `/tickets/{old_id}` com histórico completo daquela época.

---

## QS8 — Filtros e busca full-text (US7)

**Pré-condição**: tenant tem ~50 tickets distribuídos.

1. Beatriz (supervisor) abre `/kanban`.
2. Filtra: Departamento = Comercial, Atendente = Maria, Período = Esta semana.

**Critério**: Kanban filtrado mostra apenas tickets de Maria, Comercial, criados nesta semana.

**Continuação — busca full-text**:

3. Beatriz limpa filtros, digita no campo de busca: `"sábado"`.

**Critério**:

- Resultado em modo lista (não Kanban), paginado 20/pg.
- Cada item retornado tem "sábado" no protocol, subject, contact.name ou em alguma mensagem da conversa.
- Ordem: relevância + mais recente primeiro.

4. Beatriz busca o protocolo exato: `TK-20260511-00042`.

**Critério**: 1 resultado único (match exato).

---

## QS9 — Anotações internas privadas (US8)

**Pré-condição**: Maria com ticket atribuído.

1. Maria expande seção "🔒 Anotações Internas" no painel esquerdo do detalhe.
2. Adiciona nota: "Cliente já solicitou desconto antes — verificar com gerência".

**Critério**:

- Nota aparece imediatamente na seção, com nome de Maria e timestamp.
- `POST /api/tickets/{id}/notes` registrou no banco.
- Cliente, no widget de Live Chat, **NÃO** recebeu nenhuma mensagem nem indicação visual da nota.
- Evento Mongo `note_added` registrado (apenas com `note_id`, sem conteúdo).

**Continuação — verificar isolamento da IA**:

3. Trigger uma resposta da IA na conversa (cliente envia mensagem). Verificar nos logs Serilog (`Destructure` mascarado) que o prompt enviado ao GPT-4o **NÃO** contém o texto da nota interna.

**Critério**: o `system prompt` da IA inclui histórico de mensagens, **não** notas internas.

---

## QS10 — Configuração visual do pipeline (US9)

**Pré-condição**: Fabio (admin) logado.

1. Fabio acessa `/settings/pipelines/{department_id_comercial}`.

**Critério**: vê 3 colunas com nomes default ("Na Fila", "Em Andamento", "Aguardando Cliente"), sem cor.

2. Fabio renomeia: "Na Fila" → "Aguardando atribuição".
3. Reordena: arrasta "Aguardando Cliente" para a posição 1.
4. Define cor `#7A9E7E` em "Em Andamento".
5. Salva.

**Critério**:

- Tela mostra confirmação de salvamento.
- Abrir `/kanban` (depto Comercial) reflete: novos nomes, nova ordem, cor de destaque na coluna "Em Andamento".

**Continuação — bloqueio de operações inválidas**:

6. Tentar (via DevTools / API direta) `PUT /api/pipelines/{id}/columns` com 2 colunas `status_mapping = in_progress`.

**Critério**: backend retorna `400 DUPLICATE_STATUS_MAPPING`. Pipeline não modificado.

7. Carlos (`tenant_attendant`) tenta acessar `/settings/pipelines/...`.

**Critério**: `403 Forbidden` — guard rejeita por papel.

---

## QS11 — Imutabilidade de tickets `resolved` (US2/regra 6.6)

**Pré-condição**: Maria com ticket em `in_progress`.

1. Maria clica "Encerrar". Confirma.

**Critério**:

- Ticket some do Kanban.
- `status = resolved`, `resolved_at` preenchido.
- Conversa vinculada também marcada como resolved (`conversation.status = resolved`).

**Continuação — verificar imutabilidade**:

2. Maria tenta editar o ticket via UI ou API: `PUT /api/tickets/{id}` com novo subject.

**Critério**: `409 TICKET_ALREADY_CLOSED`. Ticket não muda.

3. Cliente envia nova mensagem pelo widget.

**Critério**:

- Sistema **não reabre** o ticket.
- Cria nova conversa (e potencialmente novo ticket por transbordo).
- Histórico do contato no `/contacts/{id}` mostra os dois tickets (antigo + novo).

---

## QS12 — Concorrência: geração de protocolo (FR-001/002)

**Cenário sintético** (não E2E, mas verificável via teste):

1. Em um tenant, disparar 100 requests concorrentes que criam tickets simultâneos (via `POST /api/tickets` manual).

**Critério**:

- 100 tickets criados, **0 colisões** de `protocol`.
- Sequence `tenant_clinica_abc.ticket_protocol_seq_YYYYMMDD.nextval` retornou 100 valores únicos.

Implementado como teste paralelo em `tests/omniDesk.Api.Tests/Features/ConcurrentProtocolGeneration/`.

---

## QS13 — Visibilidade por papel (SC-013)

**Cenário**:

1. Maria (depto Comercial) tenta acessar `GET /api/tickets/{id}` de um ticket Financeiro.

**Critério**: `403 FORBIDDEN_DEPARTMENT`.

2. Ana (depto Financeiro) lista tickets sem filtro: `GET /api/tickets`.

**Critério**: retorna apenas tickets Financeiro.

3. Beatriz (supervisor) lista: `GET /api/tickets`.

**Critério**: retorna tickets de Comercial **E** Financeiro.

---

## Checklist Final de Aceitação

Após executar QS1–QS13, os seguintes critérios consolidados devem estar atendidos (alinhados aos SC do spec):

- [ ] SC-001: atendente acessa qualquer ticket em ≤ 2 cliques.
- [ ] SC-002: ticket criado em ≤ 2s do transbordo.
- [ ] SC-003: 0 tickets perdidos (fila garante).
- [ ] SC-004: 0 protocolos duplicados (QS12).
- [ ] SC-005: 0 tickets resolved/cancelled no Kanban (QS11).
- [ ] SC-006: 0% de anotações vazadas ao cliente (QS9).
- [ ] SC-007: dedup ≤ 1% duplicatas em scenario sintético (QS6 + ContactDeduplicationService).
- [ ] SC-008: Kanban P95 < 1.5s com 100 tickets.
- [ ] SC-009: busca P95 < 1s com 10k mensagens.
- [ ] SC-010: WS warning chega em ≤ 3s do limiar (QS4).
- [ ] SC-011: usabilidade do badge SLA ≥ 95% acerto (avaliação manual).
- [ ] SC-012: 100% histórico preservado em transferência (QS5).
- [ ] SC-013: 100% atendentes recebem 403 fora do depto (QS13).
