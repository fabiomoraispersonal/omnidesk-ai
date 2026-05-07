# Spec 11 — Agenda e Catálogo de Serviços
**Versão:** 1.0
**Status:** Aprovado
**Última atualização:** 2026-05

---

## 1. Visão Geral

Este módulo cobre dois cadastros interdependentes mantidos juntos nesta spec:

1. **Catálogo de Serviços:** Procedimentos, consultas, exames e avaliações com nome, duração e preço — vinculados aos profissionais que os executam.
2. **Agenda:** Disponibilidade semanal dos profissionais, bloqueios e agendamentos de clientes (1 profissional × 1 cliente × 1 serviço por agendamento na V1).

A IA pode consultar disponibilidade e criar agendamentos diretamente no chat. Clientes podem cancelar respondendo "NÃO" ao lembrete via WhatsApp. Lembretes automáticos são enviados 24h antes (Spec 10).

---

## 2. Entidades

### 2.1 Profissional (`professionals`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `name` | varchar(255) | sim | Nome. Ex: "Dra. Ana Lima" |
| `specialty` | varchar(100) | não | Especialidade. Ex: "Fisioterapeuta" |
| `department_id` | UUID | não | FK → departments. Departamento de referência. |
| `attendant_id` | UUID | não | FK → attendants. Preenchido se o profissional também usa o CRM. Opcional. |
| `is_active` | boolean | sim | Default: `true`. Inativos não aparecem para agendamento. |
| `created_at` | timestamptz | sim | — |
| `updated_at` | timestamptz | sim | — |

### 2.2 Serviço / Catálogo (`services`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `name` | varchar(100) | sim | Nome do serviço. Ex: "Consulta de Avaliação", "Sessão de Fisioterapia" |
| `description` | text | não | Descrição detalhada para exibição ao cliente. |
| `category` | varchar(100) | não | Categoria livre. Ex: "Consulta", "Procedimento", "Exame", "Avaliação" |
| `duration_minutes` | int | sim | Duração padrão em minutos. Define o tamanho do slot na agenda. |
| `price` | numeric(10,2) | não | Preço. `null` = a combinar / informado na consulta. |
| `requires_confirmation` | boolean | sim | Se `true`, agendamentos deste serviço exigem confirmação manual mesmo para clientes de retorno. Default: `false`. |
| `is_active` | boolean | sim | Default: `true`. |
| `created_at` | timestamptz | sim | — |

### 2.3 Serviços por Profissional (`professional_services`)

Vínculo entre profissional e os serviços que ele executa. Cada profissional só aparece para agendamento dos serviços aqui vinculados.

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `professional_id` | UUID | sim | FK → professionals |
| `service_id` | UUID | sim | FK → services |

### 2.4 Disponibilidade Semanal (`weekly_schedules`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `professional_id` | UUID | sim | FK → professionals |
| `day_of_week` | int | sim | 0 = Domingo … 6 = Sábado |
| `start_time` | time | sim | Início do expediente. Ex: `08:00` |
| `end_time` | time | sim | Fim do expediente. Ex: `17:00` |

> Um profissional pode ter múltiplas entradas por dia (dois turnos). O horário de trabalho não é vinculado a um serviço específico — o serviço define a duração do slot.

### 2.5 Bloqueio de Horário (`schedule_blocks`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `professional_id` | UUID | sim | FK → professionals |
| `start_at` | timestamptz | sim | Início do bloqueio |
| `end_at` | timestamptz | sim | Fim do bloqueio |
| `reason` | varchar(255) | não | Motivo interno. Ex: "Férias", "Congresso" |

### 2.6 Agendamento (`appointments`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `professional_id` | UUID | sim | FK → professionals |
| `service_id` | UUID | sim | FK → services. Serviço agendado (define duração do slot). |
| `contact_id` | UUID | não | FK → contacts. |
| `ticket_id` | UUID | não | FK → tickets. Ticket de origem. |
| `conversation_id` | UUID | não | FK → conversations. Conversa de origem. |
| `start_at` | timestamptz | sim | Início do agendamento. |
| `end_at` | timestamptz | sim | Calculado: `start_at + service.duration_minutes`. |
| `status` | enum | sim | `pending_confirmation`, `confirmed`, `cancelled`, `no_show`. |
| `client_type` | enum | sim | `new_client` ou `returning_client`. |
| `created_by` | enum | sim | `ai` ou `attendant`. |
| `notes` | text | não | Observações internas. |
| `reminder_sent_at` | timestamptz | não | Preenchido ao enviar lembrete. `null` = não enviado. |
| `cancelled_by` | enum | não | `client`, `attendant`, `system`. |
| `cancelled_at` | timestamptz | não | — |
| `cancellation_reason` | varchar(255) | não | — |
| `created_at` | timestamptz | sim | — |
| `updated_at` | timestamptz | sim | — |

---

## 3. Status e Fluxo

### 3.1 Status

| Status | Descrição |
|---|---|
| `pending_confirmation` | Aguardando confirmação manual do atendente. |
| `confirmed` | Confirmado. Elegível para lembrete WhatsApp. |
| `cancelled` | Cancelado (cliente, atendente ou sistema). |
| `no_show` | Marcado manualmente — cliente não compareceu. |

### 3.2 Fluxo por Tipo de Cliente

```
Cliente Novo (ou serviço com requires_confirmation = true):
  Agendamento criado → pending_confirmation
  → Atendente confirma manualmente no CRM
  → status: confirmed → WhatsApp: appointment_confirmation

Cliente de Retorno:
  Agendamento criado → confirmed (direto)
  → WhatsApp: appointment_confirmation enviado imediatamente

Ambos:
  → Job 24h antes → WhatsApp: appointment_reminder
```

`client_type` é determinado pelo histórico do contato: se já tem agendamento `confirmed` ou `no_show` anterior → `returning_client`; caso contrário → `new_client`.

### 3.3 Cancelamento via WhatsApp

Quando o cliente responde ao lembrete com **"NÃO"**:

1. Webhook recebe a mensagem de texto "NÃO"
2. Backend verifica se a conversa tem um agendamento `confirmed` com `reminder_sent_at` preenchido nas últimas 26h (janela de resposta ao lembrete)
3. Sistema cancela automaticamente: `status: cancelled`, `cancelled_by: client`, `cancelled_at: now()`
4. Cliente recebe confirmação: "Seu agendamento foi cancelado. [texto de política de cancelamento do tenant]"
5. Notificação in-app enviada ao atendente responsável
6. Se o cancelamento ocorrer com menos de **X horas** antes do agendamento (configurável no tenant, default: 24h), o texto de confirmação inclui o aviso sobre taxa/multa (o tenant configura o texto livremente — o sistema não cobra automaticamente, apenas informa)

> **Política de cancelamento tardio:** A taxa/multa em si não é cobrada automaticamente pelo sistema na V1. O tenant define o texto de aviso na configuração; a cobrança, se houver, é feita manualmente.

---

## 4. Integração com a IA (Tool Calls)

### 4.1 `check_availability`

```json
{
  "name": "check_availability",
  "parameters": {
    "professional_id": "UUID do profissional",
    "service_id": "UUID do serviço (define duração do slot)",
    "date": "YYYY-MM-DD"
  },
  "returns": [
    { "start_at": "2026-06-10T09:00:01-03:00", "end_at": "2026-06-10T09:45:01-03:00" }
  ]
}
```

**Lógica:** busca turnos do `weekly_schedule` → remove `schedule_blocks` → remove slots ocupados por agendamentos `confirmed` ou `pending_confirmation` → retorna slots livres.

### 4.2 `create_appointment`

```json
{
  "name": "create_appointment",
  "parameters": {
    "professional_id": "UUID",
    "service_id": "UUID",
    "start_at": "ISO 8601 com timezone",
    "client_name": "string",
    "client_phone": "E.164",
    "client_type": "new_client | returning_client"
  }
}
```

Cria o agendamento com `created_by: "ai"`. Status segue a regra da seção 3.2.

---

## 5. Interface CRM

### 5.1 Agenda — Visualizações

- **Grade semanal** por profissional: slots coloridos por status
- **Lista**: agendamentos em ordem cronológica com filtros (profissional, serviço, status, período)
- **Aba "Pendentes"**: agendamentos em `pending_confirmation` — atendente confirma ou edita e confirma

### 5.2 Card de Agendamento

Exibe: cliente (badge Novo/Retorno), serviço + preço, horário, status, profissional.

### 5.3 Tela de Detalhe

Dados editáveis, histórico de ações, link para ticket/conversa de origem, link para perfil do contato. Ações: Confirmar / Cancelar / No-show / Reenviar lembrete.

### 5.4 Catálogo de Serviços

**CRM → Configurações → Serviços**
- Listagem de serviços ativos/inativos
- Criar / editar serviço (nome, descrição, categoria, duração, preço, requires_confirmation)
- Desativar serviço (soft delete — não afeta agendamentos existentes)

### 5.5 Gestão de Profissionais

**CRM → Configurações → Profissionais**
- Criar / editar profissional
- Vinculação com atendente do sistema (opcional)
- Por profissional: selecionar quais serviços ele oferece (`professional_services`)
- Por profissional: configurar disponibilidade semanal e bloqueios

### 5.6 Configuração de Cancelamento

**CRM → Configurações → Agenda**

| Configuração | Tipo | Default |
|---|---|---|
| Janela de cancelamento tardio | Número (horas) | 24h |
| Texto de aviso de cancelamento tardio | Textarea | "Cancelamentos com menos de 24h poderão ser cobrados." |

---

## 6. Regras de Negócio

- Um slot não pode ser reservado duplamente para o mesmo profissional (validação no backend)
- A IA consulta disponibilidade filtrando por `professional_services` — só sugere profissionais que oferecem o serviço solicitado
- `end_at` é sempre calculado: `start_at + service.duration_minutes` — não editável diretamente
- `appointment_confirmation` enviado automaticamente ao confirmar o agendamento
- `appointment_reminder` enviado apenas para `status = confirmed` com `reminder_sent_at = null` no dia
- Cancelamento pelo cliente via WhatsApp só é processado se a conversa tiver agendamento com lembrete enviado nas últimas 26h (janela de resposta)
- O texto de política de cancelamento é responsabilidade do tenant — o sistema apenas inclui no template e não cobra automaticamente
- Profissional inativo não aparece em `check_availability`
- Serviço inativo não aparece para novos agendamentos, mas agendamentos existentes são preservados
- V1: 1 profissional × 1 cliente × 1 serviço por agendamento

---

## 7. Endpoints da API

```
# Serviços (catálogo)
GET    /api/services                                 → listar serviços
POST   /api/services                                 → criar serviço
PUT    /api/services/{id}                            → editar serviço
PATCH  /api/services/{id}/toggle                     → ativar / desativar

# Profissionais
GET    /api/professionals                            → listar profissionais
POST   /api/professionals                            → criar profissional
PUT    /api/professionals/{id}                       → editar profissional
PATCH  /api/professionals/{id}/toggle                → ativar / desativar
GET    /api/professionals/{id}/services              → serviços do profissional
PUT    /api/professionals/{id}/services              → atualizar serviços vinculados
GET    /api/professionals/{id}/schedule              → disponibilidade semanal
PUT    /api/professionals/{id}/schedule              → salvar disponibilidade semanal
GET    /api/professionals/{id}/blocks                → listar bloqueios futuros
POST   /api/professionals/{id}/blocks                → criar bloqueio
DELETE /api/professionals/{id}/blocks/{blockId}      → remover bloqueio

# Disponibilidade (usado pela IA via tool call)
GET    /api/availability?professional_id=&service_id=&date=

# Agendamentos
GET    /api/appointments                             → listar (filtros: profissional, serviço, status, período)
GET    /api/appointments/{id}                        → detalhar
POST   /api/appointments                             → criar
PUT    /api/appointments/{id}                        → editar
PATCH  /api/appointments/{id}/confirm                → confirmar
PATCH  /api/appointments/{id}/cancel                 → cancelar
PATCH  /api/appointments/{id}/no-show                → marcar no-show
POST   /api/appointments/{id}/resend-reminder        → reenviar lembrete

# Configurações da agenda
GET    /api/agenda-settings                          → obter configurações (cancelamento etc.)
PUT    /api/agenda-settings                          → salvar configurações
```

---

## 8. Critérios de Aceite

- [ ] Profissional pode ser cadastrado sem vínculo com atendente do CRM
- [ ] Cada profissional tem lista própria de serviços vinculados (`professional_services`)
- [ ] A IA só sugere profissionais que oferecem o serviço solicitado
- [ ] `check_availability` retorna apenas slots livres (sem conflito com agendamentos ou bloqueios)
- [ ] Backend valida disponibilidade ao criar agendamento (proteção contra race condition)
- [ ] `end_at` calculado automaticamente: `start_at + service.duration_minutes`
- [ ] Cliente novo → `pending_confirmation`; cliente retorno → `confirmed` (exceto `requires_confirmation = true`)
- [ ] `appointment_confirmation` WhatsApp enviado automaticamente ao confirmar
- [ ] Cancelamento via WhatsApp: resposta "NÃO" cancela o agendamento automaticamente
- [ ] Cancelamento tardio: texto de aviso de taxa/multa incluído na resposta de cancelamento
- [ ] Janela de resposta ao lembrete: apenas cancela se `reminder_sent_at` for das últimas 26h
- [ ] Notificação in-app ao atendente quando cliente cancela via WhatsApp
- [ ] Profissional inativo e serviço inativo não aparecem em `check_availability`
- [ ] Grade semanal exibe agendamentos por profissional com distinção visual de status
- [ ] Aba "Pendentes" lista todos os `pending_confirmation` para confirmação manual
- [ ] Preço do serviço exibido no card e detalhe do agendamento
- [ ] Soft delete de serviço preserva agendamentos existentes

---

## 9. Decisões Registradas

| # | Decisão | Registrado em |
|---|---|---|
| P1 | Profissional é sempre médico/prestador de serviço — CRM opcional. Catálogo de serviços com preço integrado à Spec 11 | v1.1 |
| P2 | V1: 1 profissional × 1 cliente × 1 serviço por agendamento | v1.1 |
| P3 | Cliente cancela respondendo "NÃO" ao lembrete WhatsApp. Taxa/multa por cancelamento tardio: apenas texto de aviso configurável pelo tenant — sem cobrança automática na V1 | v1.1 |
| P4 | Catálogo de serviços vinculado por profissional via `professional_services` | v1.1 |
