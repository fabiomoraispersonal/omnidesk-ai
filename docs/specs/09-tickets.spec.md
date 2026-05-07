# Spec 09 — Tickets / CRM
**Versão:** 1.0
**Status:** Aprovado
**Última atualização:** 2026-05

---

## 1. Visão Geral

O módulo de Tickets é o coração do CRM. Um ticket representa um atendimento formal — criado automaticamente quando a IA transfere a conversa para um humano, ou manualmente por um atendente. Cada ticket concentra o histórico completo da conversa, dados do cliente, anotações internas, SLA e ciclo de vida até a resolução. O CRM exibe os tickets em um pipeline Kanban por departamento.

---

## 2. Entidades

### 2.1 Ticket (`tickets`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `protocol` | varchar(20) | sim | Número único de identificação. Gerado automaticamente. Formato: `TK-YYYYMMDD-XXXXX` (ex: `TK-20260503-00042`). Imutável. |
| `channel` | enum | sim | `live_chat` ou `whatsapp` |
| `status` | enum | sim | `new`, `in_progress`, `waiting_client`, `resolved`, `cancelled`. Ver seção 3.1. |
| `priority` | enum | sim | `low`, `normal`, `high`, `urgent`. Default: `normal` |
| `conversation_id` | UUID | sim | FK → conversations. Conversa que originou o ticket. |
| `contact_id` | UUID | não | FK → contacts. Cliente vinculado ao ticket. |
| `department_id` | UUID | sim | FK → departments. Departamento responsável. |
| `attendant_id` | UUID | não | FK → attendants. **Um único atendente responsável** por vez. `null` = na fila. Para colaboração, o ticket é transferido e pode ser devolvido. |
| `tags` | text[] | não | Tags livres. Ex: `["agendamento", "novo-cliente"]` |
| `subject` | varchar(255) | não | Assunto/título do ticket. Preenchido automaticamente com base na primeira mensagem ou manualmente. |
| `resolved_at` | timestamptz | não | Momento da resolução. Preenchido ao mudar status para `resolved`. |
| `cancelled_at` | timestamptz | não | Momento do cancelamento. |
| `first_response_at` | timestamptz | não | Momento da primeira mensagem enviada pelo atendente humano. Usado para SLA de primeira resposta. |
| `sla_first_response_deadline` | timestamptz | não | Prazo absoluto de primeira resposta (calculado no momento da atribuição). |
| `sla_resolution_deadline` | timestamptz | não | Prazo absoluto de resolução (calculado no momento da criação do ticket). |
| `sla_paused_duration_minutes` | int | sim | Tempo total (em minutos) em que o SLA de resolução ficou pausado por status `waiting_client`. Default: `0`. Somado ao prazo final para cálculo real do tempo restante. |
| `waiting_client_since` | timestamptz | não | Momento em que o status mudou para `waiting_client`. `null` se não estiver neste status. Usado para calcular a pausa do SLA. |
| `has_reminder_alert` | boolean | sim | `true` quando o envio automático de lembrete de agendamento falhou. Exibe badge ⚠️ no card do Kanban. Resetado para `false` após reenvio bem-sucedido ou encerramento do ticket. Default: `false`. |
| `created_at` | timestamptz | sim | — |
| `updated_at` | timestamptz | sim | — |

### 2.2 Anotação Interna (`ticket_notes`)

Visíveis apenas para atendentes e supervisores — **nunca** para o cliente.

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `ticket_id` | UUID | sim | FK → tickets |
| `attendant_id` | UUID | sim | FK → attendants. Quem escreveu. |
| `content` | text | sim | Conteúdo da anotação |
| `created_at` | timestamptz | sim | — |

### 2.3 Evento do Ticket (`ticket_events`) — `MongoDB: ticket_events`

Log imutável de todas as mudanças relevantes do ticket para auditoria.

```json
{
  "tenant_slug": "clinica-abc",
  "ticket_id": "uuid",
  "protocol": "TK-20260503-00042",
  "event_type": "status_changed" | "attendant_assigned" | "transferred" | "priority_changed" | "tag_added" | "note_added" | "sla_breached",
  "actor_type": "attendant" | "system",
  "actor_id": "uuid",
  "actor_name": "Maria",
  "from": "new",
  "to": "in_progress",
  "reason": "texto opcional",
  "timestamp": "2026-06-03T10:00:00Z"
}
```

### 2.4 Pipeline Kanban (`pipelines`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `department_id` | UUID | sim | FK → departments (1:1 por padrão — cada departamento tem um pipeline) |
| `name` | varchar(100) | sim | Nome do pipeline. Ex: "Atendimento Comercial" |
| `created_at` | timestamptz | sim | — |

### 2.5 Coluna do Pipeline (`pipeline_columns`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `pipeline_id` | UUID | sim | FK → pipelines |
| `name` | varchar(100) | sim | Nome da coluna. Ex: "Na Fila", "Em Andamento", "Aguardando Retorno" |
| `status_mapping` | enum | sim | Status do ticket mapeado para esta coluna. **Único por pipeline** — não é permitido ter duas colunas com o mesmo `status_mapping` no mesmo pipeline. Valores possíveis: `new`, `in_progress`, `waiting_client`. |
| `order` | int | sim | Posição da coluna (1, 2, 3…) |
| `color` | varchar(7) | não | Cor hex da coluna para destaque visual. |

### 2.6 Contato (`contacts`)

Representa o cliente que entrou em contato. Pode ser criado a partir do visitante identificado. O sistema faz **deduplicação automática** ao identificar um visitante.

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `name` | varchar(255) | não | Nome completo |
| `email` | varchar(255) | não | E-mail. Usado como chave de deduplicação (prioridade 1). |
| `phone` | varchar(20) | não | Telefone com DDD. Usado como chave de deduplicação (prioridade 2, quando sem e-mail). |
| `phone_normalized` | varchar(20) | não | Telefone normalizado (apenas dígitos). Indexado para busca de deduplicação. |
| `notes` | text | não | Observações internas sobre o contato |
| `source_channels` | text[] | não | Canais pelos quais o contato já interagiu. Ex: `["live_chat", "whatsapp"]` |
| `created_at` | timestamptz | sim | — |
| `updated_at` | timestamptz | sim | — |

**Lógica de deduplicação automática:**
1. Quando um visitante fornece e-mail → busca contato existente com mesmo e-mail
2. Quando fornece telefone (sem e-mail) → busca por `phone_normalized`
3. **Se encontrado:** vincula a conversa/ticket ao contato existente; atualiza `name` e demais campos se o novo valor não estiver vazio
4. **Se não encontrado:** cria novo contato
5. Um contato pode ter múltiplos tickets e múltiplos canais (`source_channels`)

> O histórico completo de um contato (tickets + conversas antigas) é acessível pelo atendente na tela de perfil do contato, com paginação. O atendente pode abrir qualquer ticket ou conversa anterior diretamente dali.

---

## 3. Status e Ciclo de Vida do Ticket

### 3.1 Status Disponíveis

| Status | Nome Exibido | Descrição |
|---|---|---|
| `new` | Novo | Ticket criado, na fila, sem atendente atribuído |
| `in_progress` | Em Andamento | Atendente atribuído e interagindo |
| `waiting_client` | Aguardando Cliente | Atendente respondeu, aguarda retorno do cliente |
| `resolved` | Resolvido | Atendimento concluído |
| `cancelled` | Cancelado | Ticket cancelado (sem resolução) |

### 3.2 Transições de Status

```
new ──→ in_progress ──→ waiting_client ──→ in_progress
                ↓                 ↓
           resolved            resolved
                ↓
           cancelled (de qualquer status exceto resolved)
```

- A transição `new → in_progress` ocorre automaticamente quando um atendente é atribuído
- A transição para `waiting_client` é feita manualmente pelo atendente
- A transição `waiting_client → in_progress` ocorre automaticamente quando o cliente envia nova mensagem
- Tickets `resolved` não podem ser reabertos — se o cliente entrar em contato novamente, um novo ticket é criado
- Tickets `cancelled` também não podem ser reabertos

### 3.3 Mapeamento Status → Coluna do Pipeline

Cada coluna do Kanban mapeia para **exatamente um status**. Cada status pode aparecer em **no máximo uma coluna** por pipeline. O tenant pode renomear as colunas, mas não pode criar novos status nem duplicar mapeamentos.

| Status | Coluna padrão criada no provisionamento | Visível no Kanban |
|---|---|---|
| `new` | "Na Fila" | ✅ |
| `in_progress` | "Em Andamento" | ✅ |
| `waiting_client` | "Aguardando Cliente" | ✅ |
| `resolved` | *(sem coluna)* | ❌ |
| `cancelled` | *(sem coluna)* | ❌ |

> Tickets `resolved` e `cancelled` saem do Kanban automaticamente e ficam acessíveis apenas via filtro ou busca. Não é possível ter duas colunas para o mesmo status.

---

## 4. Abertura de Ticket

### 4.1 Automática (por Transbordo da IA)

Quando a IA chama `transfer_to_human`:

1. Backend cria `ticket` com:
   - `status: new`
   - `channel`: canal de origem da conversa
   - `department_id`: vindo do parâmetro da tool call
   - `contact_id`: se o visitante já foi identificado
   - `subject`: primeiras 100 chars da última mensagem da IA como sugestão (editável)
   - `conversation_id`: conversa atual
2. SLA calculado imediatamente (ver Spec 05 — seção 4)
3. Atribuição automática via round-robin (ver Spec 05 — seção 3.4)
4. Evento `ticket_created` registrado no MongoDB

### 4.2 Manual (por Atendente)

Atendente pode criar ticket manualmente a partir do CRM:
- Seleciona ou cria um Contato
- Preenche: departamento, assunto, prioridade, tags
- Pode vincular a uma conversa existente ou criar ticket sem conversa (ex: atendimento por telefone)
- `channel`: `manual` quando criado sem conversa

---

## 5. Painel CRM — Interface do Atendente

### 5.1 Layout Geral

```
┌────────────┬────────────────────────────────────────────────┐
│  Sidebar   │              Área Principal                    │
│            │                                                │
│  🎫 Tickets │  [Filtros]  [Busca]  [+ Novo Ticket]          │
│  💬 Chats  │  ──────────────────────────────────────────   │
│  📅 Agenda │  ┌──────────┬──────────┬────────────────────┐ │
│  ⚙️ Config │  │ Na Fila  │Em Andamt.│ Aguard. Cliente    │ │
│            │  │ (3)      │ (7)      │ (2)                │ │
│            │  │ [card]   │ [card]   │ [card]             │ │
│            │  │ [card]   │ [card]   │                    │ │
│            │  │ [card]   │          │                    │ │
│            │  └──────────┴──────────┴────────────────────┘ │
└────────────┴────────────────────────────────────────────────┘
```

### 5.2 Card de Ticket no Kanban

Cada card exibe:
- Protocolo (ex: `TK-20260503-00042`)
- Nome do contato (ou "Visitante Anônimo")
- Canal de origem (ícone: 💬 Live Chat / WhatsApp)
- Assunto (truncado em 60 chars)
- Atendente atribuído (avatar + nome) ou "Sem atendente"
- Tempo desde a criação
- Badge SLA: **verde** (dentro do prazo), **amarelo** (>80% do tempo consumido), **vermelho** (expirado)
- Tags (até 3 visíveis, "+N" se houver mais)

Cards são arrastáveis entre colunas (drag-and-drop muda o status do ticket).

### 5.3 Tela de Detalhe do Ticket

Acessada ao clicar em um card. Layout em dois painéis:

```
┌──────────────────────────────┬──────────────────────────┐
│  Histórico da Conversa       │  Painel Lateral          │
│                              │                          │
│  [mensagens em ordem         │  Protocolo: TK-...       │
│   cronológica, incluindo     │  Status: Em Andamento    │
│   mensagens da IA]           │  Prioridade: Normal      │
│                              │  Atendente: Maria S.     │
│  [Anotações Internas]        │  Departamento: Comercial │
│                              │  Tags: [lead] [novo]     │
│  [Campo de resposta]         │  SLA: ⏱ 2h restantes    │
│  [📎] [Sugerir IA] [Enviar]  │                          │
│                              │  Contato: João Silva     │
│                              │  Email: joao@email.com   │
│                              │  Tel: (11) 99999-9999    │
│                              │                          │
│                              │  [Transferir]            │
│                              │  [Encerrar]              │
│                              │  [Cancelar]              │
└──────────────────────────────┴──────────────────────────┘
```

**Painel esquerdo:**
- Histórico completo da conversa (mensagens da IA + atendente + cliente)
- Seção de Anotações Internas (colapsável, separada visualmente das mensagens)
- Campo de resposta com: anexo, sugestão de IA, envio

**Painel direito:**
- Dados do ticket (editáveis inline: status, prioridade, tags, assunto)
- SLA com contador regressivo
- Dados do contato (com link para perfil completo)
- Botões de ação: Transferir, Encerrar, Cancelar

### 5.4 Filtros e Busca

**Filtros disponíveis (painel de filtros acima do Kanban):**

| Filtro | Opções |
|---|---|
| Departamento | Todos / [lista dos departamentos do tenant] |
| Atendente | Todos / Sem atendente / [lista de atendentes] |
| Canal | Todos / Live Chat / WhatsApp / Manual |
| Prioridade | Todos / Baixa / Normal / Alta / Urgente |
| Tags | Campo de busca por tag |
| Período | Criados hoje / esta semana / este mês / personalizado |

**Busca full-text:**
- Busca em: protocolo, assunto, nome do contato, conteúdo das mensagens
- Resultados exibidos em lista (não Kanban) ao buscar

---

## 6. Regras de Negócio

### 6.1 Protocolo

- Gerado automaticamente no formato `TK-YYYYMMDD-XXXXX` (sequencial por dia dentro do tenant)
- Imutável após criação
- Único dentro do tenant

### 6.2 Atribuição

- Cada ticket tem **um único atendente responsável** por vez (`attendant_id`)
- Ao criar o ticket, o sistema tenta atribuição automática via round-robin (ver Spec 05)
- Se não houver atendente disponível, ticket fica `new` na fila
- Quando um atendente fica disponível, o sistema verifica tickets `new` na fila do seu departamento e atribui o mais antigo
- Para colaboração entre atendentes, o mecanismo é a **transferência** (ver 6.3): o atendente A transfere para o B, o B resolve e pode transferir de volta para o A se necessário

### 6.3 Transferência de Ticket

- Atendente pode transferir o ticket a qualquer momento para:
  - Outro atendente do mesmo ou de outro departamento
  - Um departamento (sem atendente específico — entra na fila)
- O histórico completo acompanha a transferência
- Ao transferir para outro departamento, o SLA é recalculado com as metas do novo departamento
- A transferência gera evento no MongoDB e notificação para o novo atendente (ver Spec 10)
- O atendente anterior é notificado da transferência

### 6.4 SLA — Pausa durante `waiting_client`

- O timer de **resolução** (`sla_resolution_deadline`) **pausa** enquanto o ticket estiver em `waiting_client`
- Ao entrar em `waiting_client`: `waiting_client_since` é preenchido com o timestamp atual
- Ao sair de `waiting_client` (cliente responde → `in_progress`): o tempo pausado é calculado e somado a `sla_paused_duration_minutes`; `waiting_client_since` volta para `null`
- O prazo efetivo de resolução = `sla_resolution_deadline` + `sla_paused_duration_minutes`
- O contador exibido no card e na tela do ticket sempre usa o prazo efetivo (com pausa descontada)
- O timer de **primeira resposta** (`sla_first_response_deadline`) **não pausa** — é medido desde a atribuição até a primeira mensagem do atendente

### 6.5 Encerramento

- Qualquer atendente com acesso ao ticket pode encerrar
- Ao encerrar: `status → resolved`, `resolved_at` preenchido
- Se estava em `waiting_client` ao encerrar: pausa final do SLA é calculada antes de registrar
- Evento `status_changed` registrado no MongoDB
- O cliente recebe notificação (se opt-in ativo — ver Spec 10)
- A conversa vinculada também é marcada como `resolved`

### 6.6 Tickets Resolvidos

- Tickets `resolved` não aparecem no Kanban
- Acessíveis via filtro "Período" ou busca full-text
- Não podem ser reabertos — novo contato do cliente gera novo ticket

### 6.7 Visibilidade

- `attendant` vê apenas tickets dos departamentos aos quais pertence
- `supervisor` e `tenant_admin` veem todos os tickets do tenant
- Anotações internas nunca são enviadas ao cliente por nenhum canal

### 6.8 Perfil de Contato e Histórico

- Na tela de detalhe do ticket, o nome do contato é um link para a **tela de perfil do contato**
- A tela de perfil exibe:
  - Dados do contato (nome, e-mail, telefone, canais) — editáveis
  - Lista paginada de todos os tickets do contato (mais recentes primeiro)
  - Lista paginada de todas as conversas do contato
  - O atendente pode abrir qualquer ticket ou conversa antiga diretamente dali, com histórico completo
- Paginação: 20 itens por página

### 6.9 Assunto (Subject)

- Gerado automaticamente com base na primeira mensagem da conversa (primeiras 100 chars) ao criar por transbordo
- Editável manualmente pelo atendente a qualquer momento
- Exibido como título do ticket no Kanban e na listagem

---

## 7. Endpoints da API

```
# Tickets
GET    /api/tickets                              → listar tickets (com filtros e paginação)
GET    /api/tickets/{id}                         → detalhar ticket
POST   /api/tickets                              → criar ticket manualmente
PUT    /api/tickets/{id}                         → editar ticket (assunto, prioridade, tags)
PATCH  /api/tickets/{id}/status                  → mudar status
PATCH  /api/tickets/{id}/attendant               → atribuir / reatribuir atendente
POST   /api/tickets/{id}/transfer                → transferir para outro atendente ou departamento
POST   /api/tickets/{id}/notes                   → adicionar anotação interna
GET    /api/tickets/{id}/notes                   → listar anotações internas
GET    /api/tickets/{id}/events                  → histórico de eventos do ticket
POST   /api/tickets/{id}/resolve                 → encerrar ticket
POST   /api/tickets/{id}/cancel                  → cancelar ticket

# Pipelines (configuração)
GET    /api/pipelines                            → listar pipelines do tenant
GET    /api/pipelines/{id}                       → detalhar pipeline (com colunas)
PUT    /api/pipelines/{id}/columns               → reordenar / renomear colunas (sem duplicar status)

# Contatos
GET    /api/contacts                             → listar contatos
GET    /api/contacts/{id}                        → detalhar contato (perfil completo)
POST   /api/contacts                             → criar contato
PUT    /api/contacts/{id}                        → editar contato
GET    /api/contacts/{id}/tickets                → tickets do contato (paginado)
GET    /api/contacts/{id}/conversations          → conversas do contato (paginado)
```

---

## 8. Eventos WebSocket

| Evento | Payload | Descrição |
|---|---|---|
| `ticket.created` | `{ ticket_id, protocol, department_id }` | Novo ticket criado |
| `ticket.assigned` | `{ ticket_id, attendant_id, attendant_name }` | Ticket atribuído a atendente |
| `ticket.status_changed` | `{ ticket_id, from_status, to_status }` | Status do ticket mudou |
| `ticket.transferred` | `{ ticket_id, from_attendant_id, to_department_id }` | Ticket transferido |
| `ticket.sla_warning` | `{ ticket_id, type, deadline }` | SLA atingiu 80% do tempo |
| `ticket.sla_breached` | `{ ticket_id, type }` | SLA expirado |

---

## 9. Critérios de Aceite

- [ ] Ticket criado automaticamente ao receber `transfer_to_human` da IA
- [ ] Protocolo gerado no formato `TK-YYYYMMDD-XXXXX`, único por tenant, imutável
- [ ] Ao criar por transbordo, o `subject` é preenchido automaticamente com as primeiras 100 chars da última mensagem
- [ ] Um ticket tem exatamente um `attendant_id` por vez — colaboração feita via transferência
- [ ] Status `new → in_progress` ocorre automaticamente ao atribuir atendente
- [ ] Status `waiting_client → in_progress` ocorre automaticamente ao receber nova mensagem do cliente
- [ ] SLA de resolução pausa ao entrar em `waiting_client` e retoma ao sair — tempo pausado acumulado em `sla_paused_duration_minutes`
- [ ] Prazo efetivo de SLA exibido no card considera o tempo pausado
- [ ] Não é permitido ter duas colunas do Kanban com o mesmo `status_mapping` no mesmo pipeline
- [ ] Drag-and-drop de card no Kanban altera o status do ticket
- [ ] Tickets `resolved` e `cancelled` não aparecem no Kanban — acessíveis via filtro/busca
- [ ] Tickets `resolved` não podem ser reabertos
- [ ] Anotações internas nunca são enviadas ao cliente por nenhum canal
- [ ] Atendente só vê tickets dos seus departamentos
- [ ] Supervisor e tenant_admin veem todos os tickets do tenant
- [ ] Ao transferir para outro departamento, o SLA é recalculado com as metas do novo departamento
- [ ] Toda mudança relevante (status, atribuição, transferência, SLA) gera evento no MongoDB
- [ ] Badge de SLA no card: verde / amarelo (>80%) / vermelho (expirado)
- [ ] Evento WebSocket `ticket.sla_warning` emitido ao atingir 80% do tempo do SLA
- [ ] Busca full-text funciona em: protocolo, assunto, nome do contato, conteúdo das mensagens
- [ ] Ticket criado manualmente pode existir sem `conversation_id` (ex: atendimento por telefone)
- [ ] Contato deduplicado automaticamente por e-mail (prioridade 1) ou telefone normalizado (prioridade 2)
- [ ] Ao identificar contato existente, dados são atualizados com os valores mais recentes não-nulos
- [ ] Tela de perfil do contato exibe histórico paginado (20/página) de tickets e conversas anteriores
- [ ] Atendente pode abrir qualquer ticket ou conversa antiga pelo perfil do contato

---

## 10. Decisões Registradas

| # | Decisão | Registrado em |
|---|---|---|
| P1 | Um único responsável por ticket — colaboração via transferência e devolução | v1.1 |
| P2 | SLA de resolução pausa em `waiting_client`; retoma quando cliente responde | v1.1 |
| P3 | Cada coluna mapeia para exatamente um status — sem duplicatas por pipeline | v1.1 |
| P4 | Deduplicação automática de contatos por e-mail (P1) ou telefone normalizado (P2) | v1.1 |
| P5 | Perfil do contato com histórico paginado de tickets e conversas; atendente acessa histórico completo | v1.1 |
