# Spec 05 — Departamentos e Atendentes
**Versão:** 1.0
**Status:** Aprovado
**Última atualização:** 2026-05

---

## 1. Visão Geral

Este módulo define a estrutura de times humanos dentro de cada tenant. Departamentos são grupos de atendentes com filas próprias de tickets. Atendentes são os operadores humanos que assumem conversas transferidas pela IA. O módulo controla disponibilidade em tempo real, distribuição de tickets, transferências entre atendentes e ferramentas de produtividade (respostas pré-formadas, sugestão via IA).

---

## 2. Entidades

### 2.1 Departamento (`departments`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `name` | varchar(100) | sim | Nome do departamento. Ex: "Comercial", "Suporte" |
| `description` | text | não | Descrição interna |
| `business_hours_start` | time | não | Início do horário de atendimento. Ex: `08:00` |
| `business_hours_end` | time | não | Fim do horário de atendimento. Ex: `18:00` |
| `business_days` | int[] | não | Dias da semana: 0=Dom, 1=Seg … 6=Sáb. Ex: `[1,2,3,4,5]` |
| `sla_first_response_minutes` | int | não | Meta de SLA: tempo máximo para primeira resposta humana (em minutos) |
| `sla_resolution_minutes` | int | não | Meta de SLA: tempo máximo para resolução do ticket (em minutos) |
| `is_active` | boolean | sim | Soft delete lógico |
| `created_at` | timestamptz | sim | — |
| `updated_at` | timestamptz | sim | — |

### 2.2 Atendente (`attendants`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `user_id` | UUID | sim | FK → users (tabela de autenticação) |
| `name` | varchar(255) | sim | Nome de exibição |
| `avatar_url` | varchar(500) | não | URL da foto no MinIO |
| `max_simultaneous_chats` | int | sim | Limite de atendimentos simultâneos. Default: 5 |
| `is_active` | boolean | sim | Conta ativa/desativada |
| `created_at` | timestamptz | sim | — |
| `updated_at` | timestamptz | sim | — |

### 2.3 Atendente ↔ Departamento (`attendant_departments`)

Tabela de relacionamento N:N — um atendente pode pertencer a vários departamentos.

| Campo | Tipo | Descrição |
|---|---|---|
| `attendant_id` | UUID | FK → attendants |
| `department_id` | UUID | FK → departments |
| `is_primary` | boolean | Indica se este é o departamento principal do atendente (para fins de relatório) |

### 2.4 Status do Atendente (`attendant_status`)

Tabela separada para controle de presença em tempo real — atualizada com frequência.

| Campo | Tipo | Descrição |
|---|---|---|
| `attendant_id` | UUID | PK + FK → attendants |
| `status` | enum | `online`, `away`, `offline` |
| `changed_at` | timestamptz | Momento da última mudança de status |
| `changed_by` | enum | `manual` (atendente escolheu) ou `system` (timeout automático) |

### 2.5 Log de Status (`MongoDB: attendant_status_logs`)

Cada mudança de status gera um documento no MongoDB para auditoria.

```json
{
  "tenant_slug": "clinica-abc",
  "attendant_id": "uuid",
  "attendant_name": "Maria",
  "from_status": "online",
  "to_status": "away",
  "changed_by": "manual",
  "timestamp": "2026-06-02T14:30:00Z"
}
```

### 2.6 Resposta Pré-formada (`canned_responses`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `title` | varchar(100) | sim | Título para busca. Ex: "Saudação inicial" |
| `content` | text | sim | Texto da resposta. Suporta variáveis: `{{client_name}}`, `{{attendant_name}}` |
| `department_id` | UUID | não | Se nulo, disponível para todos os departamentos |
| `created_by` | UUID | sim | FK → attendants |
| `created_at` | timestamptz | sim | — |

---

## 3. Regras de Negócio

### 3.1 Departamentos

- Criação totalmente dinâmica pelo tenant admin
- Não há departamentos pré-criados obrigatórios — o tenant cria os seus
- Departamentos podem ser desativados (`is_active = false`) mas não deletados fisicamente se houver tickets ou histórico vinculados
- O horário de atendimento do departamento é usado pela IA para informar disponibilidade ao cliente
- Um departamento sem horário configurado é tratado como disponível 24/7 (a IA não menciona horários)

### 3.2 Atendentes

- Um atendente pode pertencer a um ou mais departamentos
- O limite `max_simultaneous_chats` é respeitado na distribuição automática de tickets
- Atendentes desativados não recebem novos tickets e não aparecem como disponíveis

### 3.3 Status de Presença

**Transições de status:**

```
offline ──→ online ──→ away ──→ offline
              ↑_________________________|
```

- O atendente muda o status manualmente via toggle no CRM
- **Timeout automático para `away`:** se o atendente estiver `online` mas sem interação no CRM por 15 minutos, o sistema muda para `away` automaticamente (`changed_by: system`)
- **Timeout automático para `offline`:** se o atendente estiver `away` por 30 minutos sem retornar, o sistema muda para `offline` automaticamente
- O status é armazenado no Redis (`{slug}:attendant_status:{attendant_id}`) para acesso em tempo real
- A tabela `attendant_status` no Postgres é sincronizada a cada mudança (para relatórios)
- Toda mudança de status gera um documento no MongoDB

**Regra de transbordo x horário x status:**

| Situação | Comportamento |
|---|---|
| Dentro do horário comercial + atendente `online` | Transfere normalmente |
| Dentro do horário comercial + nenhum atendente `online` | IA informa que todos estão ocupados e abre ticket na fila |
| Fora do horário comercial + atendente `online` | Transfere normalmente (atendente online tem prioridade) |
| Fora do horário comercial + nenhum atendente `online` | IA informa o horário de atendimento e abre ticket na fila para ser atendido no próximo horário comercial |

### 3.4 Distribuição de Tickets (Atribuição)

Quando um ticket entra em um departamento:

1. **Busca atendentes elegíveis:** atendentes do departamento com status `online` e que não atingiram `max_simultaneous_chats`
2. **Algoritmo de distribuição:** round-robin entre os elegíveis (distribuição equilibrada)
3. **Se houver elegível:** ticket é atribuído automaticamente e atendente é notificado
4. **Se não houver elegível:** ticket fica na fila do departamento com status `queued` — será atribuído quando um atendente ficar disponível

**Lock de concorrência:** A atribuição usa Redis para garantir que dois atendentes não peguem o mesmo ticket simultaneamente:
```
SET {slug}:ticket_lock:{ticket_id} {attendant_id} NX EX 10
```
Se o lock falhar (outro atendente já pegou), o sistema tenta o próximo atendente elegível.

### 3.5 Assumir Ticket Manualmente

- Além da atribuição automática, qualquer atendente do departamento pode **assumir manualmente** um ticket da fila
- Se o ticket já estiver atribuído a outro atendente, o sistema exibe confirmação: "Este ticket está com [Nome]. Deseja assumir?"
- Ao assumir, o atendente anterior é notificado: "O ticket #XXXX foi assumido por [Nome]"

### 3.6 Transferência de Tickets

Um atendente pode transferir um ticket a qualquer momento:

- **Para outro atendente** do mesmo ou de outro departamento
- **Para um departamento** (sem atendente específico — entra na fila)
- O histórico completo da conversa acompanha a transferência
- O atendente destino é notificado
- A transferência é registrada no histórico do ticket com motivo (opcional)
- Ao transferir para outro departamento, o SLA é recalculado com base nas metas do novo departamento

### 3.7 Respostas Pré-formadas

- Acessíveis via atalho no campo de texto do chat (ex: digitar `/` abre busca)
- Busca por título ou conteúdo
- Variáveis disponíveis: `{{client_name}}`, `{{attendant_name}}`, `{{ticket_number}}`, `{{department_name}}`
- Escopo: global (sem departamento) ou restrito a um departamento específico
- Qualquer atendente pode criar respostas pré-formadas; tenant admin pode gerenciar todas

### 3.8 Sugestão de Resposta via IA

- Disponível como botão "Sugerir resposta com IA" no chat do atendente
- O sistema envia para a OpenAI:
  - As últimas N mensagens da conversa (contexto)
  - O prompt do sub-agente vinculado ao departamento (se houver)
  - Instrução: "Sugira uma resposta adequada para o atendente humano enviar"
- A sugestão aparece em um campo de pré-visualização — o atendente pode:
  - **Aprovar e enviar** (envia a sugestão como mensagem)
  - **Editar e enviar** (edita antes de enviar)
  - **Descartar** (ignora a sugestão)
- A sugestão **nunca é enviada automaticamente** — sempre requer aprovação humana
- O uso desta feature é registrado no MongoDB para fins de análise futura

---

## 4. SLA — Simples (V1)

Cada departamento define dois tempos-alvo opcionais:
- `sla_first_response_minutes`: tempo máximo para o atendente enviar a primeira mensagem após assumir o ticket
- `sla_resolution_minutes`: tempo máximo para o ticket ser marcado como resolvido

**Comportamento:**
- O CRM exibe um contador regressivo no card do ticket
- Tickets com mais de 80% do tempo consumido ficam com badge **amarelo** (atenção)
- Tickets com prazo expirado ficam com badge **vermelho** (atrasado)
- Se o departamento não tiver SLA configurado, nenhum contador é exibido
- Não há escalonamento automático nem relatórios de SLA no MVP — apenas visibilidade visual

**Contagem de tempo:**
- O timer de primeira resposta inicia quando o ticket é atribuído ao atendente
- O timer de resolução inicia quando o ticket é criado
- Períodos fora do horário comercial do departamento **não contam** no SLA (ex: ticket criado às 17h50, horário encerra às 18h — o timer pausa e retoma às 08h do próximo dia útil)

> SLA avançado (escalonamento, relatórios de performance por atendente e departamento) fica para V2.

---

## 5. Endpoints da API

Todos requerem autenticação e são resolvidos no contexto do tenant.

```
# Departamentos
GET    /api/departments                         → listar departamentos
GET    /api/departments/{id}                    → detalhar
POST   /api/departments                         → criar
PUT    /api/departments/{id}                    → editar
DELETE /api/departments/{id}                    → desativar (soft delete)
GET    /api/departments/{id}/attendants         → listar atendentes do departamento

# Atendentes
GET    /api/attendants                          → listar atendentes
GET    /api/attendants/{id}                     → detalhar
POST   /api/attendants                          → criar
PUT    /api/attendants/{id}                     → editar
DELETE /api/attendants/{id}                     → desativar
PATCH  /api/attendants/{id}/status              → atualizar status (online/away/offline)
GET    /api/attendants/{id}/tickets             → tickets ativos do atendente

# Respostas Pré-formadas
GET    /api/canned-responses                    → listar (com filtro por departamento)
POST   /api/canned-responses                    → criar
PUT    /api/canned-responses/{id}               → editar
DELETE /api/canned-responses/{id}               → excluir

# Sugestão IA
POST   /api/conversations/{id}/suggest-reply    → solicitar sugestão de resposta da IA
```

---

## 6. Eventos WebSocket

Eventos emitidos para atualização em tempo real no CRM.

| Evento | Payload | Descrição |
|---|---|---|
| `attendant.status_changed` | `{ attendant_id, status, changed_at }` | Status de um atendente mudou |
| `ticket.assigned` | `{ ticket_id, attendant_id }` | Ticket foi atribuído a um atendente |
| `ticket.transferred` | `{ ticket_id, from_attendant_id, to_attendant_id, to_department_id }` | Ticket transferido |
| `ticket.queued` | `{ ticket_id, department_id }` | Ticket entrou na fila sem atendente disponível |

---

## 7. Critérios de Aceite

- [ ] Um atendente pode pertencer a múltiplos departamentos
- [ ] O sistema impede dois atendentes de assumirem o mesmo ticket simultaneamente (lock Redis)
- [ ] Atendente com `max_simultaneous_chats` atingido não recebe novos tickets automaticamente
- [ ] Status muda para `away` automaticamente após 15 min de inatividade
- [ ] Status muda para `offline` automaticamente após 30 min em `away`
- [ ] Toda mudança de status gera documento no MongoDB
- [ ] Fora do horário comercial, se houver atendente online, o ticket é transferido normalmente
- [ ] Respostas pré-formadas suportam as variáveis `{{client_name}}`, `{{attendant_name}}`, `{{ticket_number}}`, `{{department_name}}`
- [ ] Sugestão de IA nunca é enviada sem aprovação explícita do atendente
- [ ] Transferência entre departamentos recalcula o SLA com as metas do novo departamento
- [ ] Departamentos com histórico de tickets não podem ser deletados fisicamente
