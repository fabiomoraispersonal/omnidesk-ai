# Spec 10 — Notificações
**Versão:** 1.0
**Status:** Aprovado
**Última atualização:** 2026-05

---

## 1. Visão Geral

O módulo de Notificações cobre dois contextos distintos:

1. **Notificações internas (para atendentes/supervisores):** Alertas sobre eventos do CRM — novo ticket, nova mensagem, SLA crítico, transferência. Entregues via sino de notificações in-app e push notification no browser.

2. **Notificações para clientes:** Mensagens proativas enviadas via WhatsApp (templates aprovados) — confirmação de agendamento, lembrete 24h antes, follow-up pós-atendimento. Acionadas automaticamente pelo sistema ou manualmente pelo atendente.

> Notificações por **e-mail para clientes** não estão previstas na V1. E-mail interno (SendGrid) é usado apenas para provisionamento de tenant (Spec 03).

---

## 2. Notificações Internas (Atendentes / Supervisores)

### 2.1 Canais de Entrega

| Canal | Descrição |
|---|---|
| **In-app (sino)** | Sino 🔔 no topo do CRM. Badge com contador de não lidas. Lista de notificações ao clicar. |
| **Browser Push** | Web Push API. Requer permissão explícita do usuário. Notifica mesmo com o CRM em segundo plano. |

> Não há notificação por e-mail para atendentes na V1.

### 2.2 Eventos que Geram Notificação

| Evento | Destinatário | In-App | Browser Push | Descrição |
|---|---|---|---|---|
| `ticket.assigned` | Atendente atribuído | ✅ | ✅ | "Você recebeu o ticket TK-XXXXX de [Contato]" |
| `ticket.new_message` | Atendente responsável | ✅ | ✅ | "Nova mensagem de [Contato] no ticket TK-XXXXX" |
| `ticket.transferred_to_me` | Atendente de destino | ✅ | ✅ | "[Atendente] transferiu o ticket TK-XXXXX para você" |
| `ticket.sla_warning` | Atendente responsável | ✅ | ✅ | "⚠️ SLA do ticket TK-XXXXX atinge o limite em breve" |
| `ticket.sla_breached` | Atendente + Supervisor | ✅ | ✅ | "🔴 SLA do ticket TK-XXXXX foi ultrapassado" |
| `ticket.client_replied` | Atendente responsável | ✅ | ✅ | "Cliente respondeu no ticket TK-XXXXX (estava aguardando)" |
| `ticket.queued` | Supervisores do departamento | ✅ | ✅ | "Ticket TK-XXXXX na fila de [Departamento] há mais de 5 minutos sem atendente" |
| `ticket.reminder_failed` | Atendente responsável | ✅ | ✅ | "⚠️ Falha ao enviar lembrete de agendamento para [Contato] no ticket TK-XXXXX" |

> **Regra de silêncio:** Se o atendente estiver com o ticket aberto na tela (aba ativa), as notificações `ticket.new_message` e `ticket.client_replied` desse ticket específico **não disparam** browser push — apenas atualizam a tela em tempo real via WebSocket.

### 2.3 Entidade: Notificação (`notifications`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `attendant_id` | UUID | sim | FK → attendants. Destinatário. |
| `event_type` | enum | sim | Tipo do evento (valores da tabela 2.2). |
| `title` | varchar(255) | sim | Título da notificação. |
| `body` | text | sim | Corpo da notificação. |
| `entity_type` | varchar(50) | sim | Tipo da entidade relacionada: `ticket`, `conversation`. |
| `entity_id` | UUID | sim | ID da entidade relacionada (ticket_id ou conversation_id). |
| `is_read` | boolean | sim | Default: `false`. Marcado ao abrir ou ao clicar "Marcar todas como lidas". |
| `created_at` | timestamptz | sim | — |

### 2.4 Entidade: Subscription de Push (`push_subscriptions`)

Armazena o endpoint de push de cada dispositivo/browser do atendente.

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `attendant_id` | UUID | sim | FK → attendants. |
| `endpoint` | text | sim | URL do push endpoint (gerado pelo browser via Web Push API). |
| `p256dh` | text | sim | Chave pública de criptografia. |
| `auth` | text | sim | Chave de autenticação. |
| `user_agent` | varchar(255) | não | User-agent do browser (para identificação). |
| `created_at` | timestamptz | sim | — |

> Um atendente pode ter múltiplos push subscriptions (diferentes browsers/dispositivos). Subscriptions inativas (endpoint retorna 410 Gone) são removidas automaticamente.

---

## 3. Notificações para Clientes (WhatsApp)

Enviadas via templates aprovados (ver Spec 08). Acionadas em dois contextos:

### 3.1 Automáticas (disparadas pelo sistema)

| Gatilho | Template | Quando |
|---|---|---|
| Agendamento confirmado pelo sistema/IA | `appointment_confirmation` | Imediatamente após confirmar agendamento (ver Spec 11 — Agenda) |
| 24h antes do agendamento | `appointment_reminder` | Job agendado: verifica agendamentos do dia seguinte às 20h |
| Ticket encerrado pelo atendente | `follow_up` | Imediatamente ao encerrar o ticket (se opt-in configurado) |

> O envio automático do `follow_up` é **opcional** — configurável por tenant (toggle em CRM → Configurações → Notificações).

### 3.2 Manuais (acionadas pelo atendente)

O atendente pode enviar um template manualmente na tela do ticket quando:
- A janela de 24h está expirada e precisa reativar a conversa
- Quer enviar um lembrete ou follow-up fora do fluxo automático

**Fluxo:**
1. Atendente clica "Enviar template" na tela do ticket
2. Modal abre com lista de templates `approved`
3. Atendente seleciona o template e preenche as variáveis
4. Preview da mensagem exibido antes do envio
5. Atendente confirma → mensagem enviada via Meta API

### 3.3 Configurações de Notificação para Clientes

Acessível em: **CRM → Configurações → Notificações**

| Configuração | Tipo | Descrição |
|---|---|---|
| Enviar follow-up ao encerrar ticket | Toggle | Ativa/desativa envio automático de `follow_up` ao encerrar ticket |
| Enviar lembrete de consulta | Toggle | Ativa/desativa o job de lembretes 24h antes do agendamento |
| Horário do lembrete | Seletor (HH:mm) | Horário do dia em que o job de lembretes é executado. Default: 20:00 |

---

## 4. Interface In-App (Sino de Notificações)

### 4.1 Comportamento do Sino

- Ícone 🔔 fixo no topo direito do CRM (header)
- Badge numérico vermelho com o total de notificações **não lidas** (máx. exibido: "99+")
- Zero notificações não lidas: badge oculto
- Ao clicar: painel deslizante ou dropdown com lista de notificações

### 4.2 Lista de Notificações

- Ordenadas por `created_at` decrescente (mais recente no topo)
- Notificações não lidas destacadas com fundo levemente diferente
- Cada item exibe: ícone do tipo, título, corpo (truncado em 80 chars), tempo relativo ("há 5 min")
- Clicar em uma notificação: marca como lida + navega para o ticket/conversa relacionado
- Botão "Marcar todas como lidas"
- Paginação: carrega 20 por vez com scroll infinito

### 4.3 Preferências de Notificação do Atendente

Cada atendente pode configurar individualmente (em Perfil → Preferências):

| Preferência | Descrição |
|---|---|
| Browser push ativado | Toggle global para ativar/desativar push do browser |
| Eventos que geram push | Checkboxes por tipo de evento (ex: pode desativar `ticket.queued` mas manter `ticket.sla_breached`) |

---

## 5. Entrega de Browser Push

### 5.1 Permissão

- Ao logar pela primeira vez, o CRM solicita permissão de notificação ao browser
- Se recusada, o atendente pode ativar depois em Perfil → Preferências
- O CRM não re-solicita permissão se o usuário já tiver recusado — apenas mostra link para configurar manualmente

### 5.2 Payload do Push

```json
{
  "title": "Nova mensagem — TK-20260503-00042",
  "body": "João Silva: Olá, preciso de ajuda com meu agendamento.",
  "icon": "/icon-192.png",
  "badge": "/badge-72.png",
  "data": {
    "url": "/tickets/uuid-do-ticket"
  }
}
```

Clicar na notificação push abre o CRM (ou foca a aba existente) e navega para a entidade relacionada.

### 5.3 Expiração e Limpeza

- Subscriptions que retornam `HTTP 410 Gone` são removidas automaticamente do banco
- Notificações in-app com mais de **90 dias** são removidas por job de limpeza (Hangfire)

---

## 6. Job de Lembretes de Agendamento

### 6.1 Funcionamento

- Job Hangfire agendado para executar diariamente no horário configurado (`horario_lembrete`)
- Consulta todos os agendamentos do dia seguinte em todos os tenants
- Para cada agendamento com `appointment_reminder` ativado e contato com telefone cadastrado:
  1. Monta as variáveis do template `appointment_reminder`
  2. Verifica se há uma conversa ativa com esse contato
  3. Envia via Meta API (via `OutgoingMessageWorker`)
  4. Registra o envio em `wa_message_statuses`

### 6.2 Condições para Envio

- O canal WhatsApp do tenant está `is_enabled = true`
- O contato tem `phone` cadastrado
- O tenant tem o template `appointment_reminder` com `status = approved`
- A configuração "Enviar lembrete de consulta" está ativada

**Se qualquer condição falhar e o agendamento estiver vinculado a um ticket:**
1. O job registra um evento `reminder_failed` no `ticket_events` do ticket
2. Um badge de alerta ⚠️ é exibido no card do ticket no Kanban (e na tela de detalhe)
3. Uma notificação in-app é enviada ao atendente responsável: "Falha ao enviar lembrete para [Contato] — corrija os dados e reenvie manualmente"
4. A notificação contém link direto para o ticket
5. O ticket continua no status normal — **não muda de status** por causa da falha
6. O atendente pode: corrigir o telefone do contato e reenviar o template manualmente; ou encerrar o ticket normalmente mesmo sem o lembrete ter sido enviado

**Se o agendamento não estiver vinculado a um ticket** (agendamento avulso):
- A falha é registrada em `agent_activity_logs` no MongoDB com `action: "reminder_failed"`
- O atendente responsável pelo departamento recebe notificação in-app

> O campo `has_reminder_alert` (boolean, default `false`) na entidade `tickets` (Spec 09) é setado para `true` quando `reminder_failed` é registrado; resetado para `false` quando o atendente reenvia o lembrete com sucesso ou encerra o ticket.

---

## 7. Regras de Negócio

- Notificações in-app são armazenadas em banco (Postgres, schema do tenant)
- Browser push é disparado em tempo real via WebSocket → Service Worker
- O atendente pode ter múltiplos push subscriptions ativos simultaneamente (vários browsers)
- Subscriptions com endpoint retornando `410 Gone` são removidas automaticamente
- Notificações não lidas com mais de 90 dias são arquivadas (soft delete)
- O total de não lidas retornado no badge é calculado em tempo real (não cacheado), máx. 99
- `ticket.sla_breached` notifica **tanto o atendente responsável quanto todos os supervisores** do departamento
- `ticket.queued` notifica supervisores quando um ticket fica na fila por mais de **5 minutos** sem atendente — tempo **fixo, não configurável**
- Envio proativo de WhatsApp (lembretes) é executado como job em background, não bloqueando o fluxo principal
- O sistema não reenvia o mesmo lembrete se já foi enviado no mesmo dia para o mesmo agendamento
- Falha no envio de lembrete gera: evento `reminder_failed` no `ticket_events` + badge de alerta no card do ticket + notificação in-app ao atendente responsável

---

## 8. Endpoints da API

```
# Notificações (autenticado — CRM)
GET    /api/notifications                        → listar notificações do atendente (paginado)
GET    /api/notifications/unread-count           → total de não lidas (usado pelo badge)
PATCH  /api/notifications/{id}/read             → marcar como lida
POST   /api/notifications/read-all              → marcar todas como lidas

# Push subscriptions
POST   /api/push/subscribe                       → registrar subscription do browser
DELETE /api/push/unsubscribe                     → remover subscription

# Preferências de notificação (atendente)
GET    /api/notifications/preferences            → obter preferências do atendente
PUT    /api/notifications/preferences            → salvar preferências

# Configurações de notificação para clientes (tenant)
GET    /api/notification-settings                → obter configurações do tenant
PUT    /api/notification-settings                → salvar configurações
```

---

## 9. Eventos WebSocket

| Evento | Payload | Descrição |
|---|---|---|
| `notification.new` | `{ id, event_type, title, body, entity_type, entity_id }` | Nova notificação in-app para o atendente |
| `notification.unread_count` | `{ count }` | Atualização do badge de não lidas |

---

## 10. Critérios de Aceite

- [ ] Sino de notificações exibe badge com total de não lidas (oculto quando zero)
- [ ] Lista de notificações carrega 20 por vez com scroll infinito
- [ ] Clicar em notificação: marca como lida + navega para o ticket/conversa
- [ ] "Marcar todas como lidas" funciona corretamente
- [ ] Browser push solicita permissão ao primeiro login
- [ ] Se permissão recusada, atendente não é re-solicitado automaticamente
- [ ] Atendente com ticket aberto na tela não recebe push para mensagens daquele ticket
- [ ] Múltiplos browsers do mesmo atendente recebem push simultaneamente
- [ ] Subscriptions com `410 Gone` são removidas automaticamente
- [ ] Notificações com mais de 90 dias são removidas por job de limpeza
- [ ] `ticket.sla_breached` notifica atendente responsável + todos os supervisores do departamento
- [ ] `ticket.queued` notifica supervisores exatamente após 5 min de ticket sem atendente na fila (fixo)
- [ ] Não há notificação automática para supervisores sobre novas conversas (antes de virar ticket)
- [ ] Job de lembretes executa diariamente no horário configurado pelo tenant
- [ ] Lembrete enviado apenas se: canal ativo + contato com telefone + template aprovado + toggle ativado
- [ ] Mesmo lembrete não enviado duas vezes no mesmo dia para o mesmo agendamento
- [ ] Falha no lembrete: evento `reminder_failed` no `ticket_events` + badge ⚠️ no card do ticket + notificação in-app ao atendente
- [ ] Badge de alerta do ticket resetado quando atendente reenvia com sucesso ou encerra o ticket
- [ ] Atendente pode reenviar lembrete manualmente após corrigir dados do contato
- [ ] Toggle de follow-up automático ao encerrar ticket funciona por tenant
- [ ] Atendente pode desativar browser push globalmente em Perfil → Preferências
- [ ] Atendente pode escolher quais eventos geram push (granularidade por tipo)

---

## 11. Decisões Registradas

| # | Decisão | Registrado em |
|---|---|---|
| P1 | `ticket.queued` fixo em 5 minutos — não configurável por tenant | v1.1 |
| P2 | `conversation.new` removido — supervisores não recebem notificação de conversas antes de virar ticket | v1.1 |
| P3 | Falha de lembrete: evento no `ticket_events` + badge de alerta no card do ticket + notificação in-app ao atendente responsável; atendente pode corrigir e reenviar ou encerrar manualmente | v1.1 |
