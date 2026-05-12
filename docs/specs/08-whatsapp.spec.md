# Spec 08 — WhatsApp
**Versão:** 1.0
**Status:** SUPERSEDED — consulte [specs/008-whatsapp-channel/](../../specs/008-whatsapp-channel/)
**Última atualização:** 2026-05

> ⚠️ **Este documento é histórico.** A spec ativa do canal WhatsApp foi migrada para o
> formato speckit em [`specs/008-whatsapp-channel/`](../../specs/008-whatsapp-channel/),
> que mantém:
> - [`spec.md`](../../specs/008-whatsapp-channel/spec.md) — requisitos + user stories
> - [`plan.md`](../../specs/008-whatsapp-channel/plan.md) — Constitution Check + design
> - [`research.md`](../../specs/008-whatsapp-channel/research.md) — R1–R10 decisões
> - [`data-model.md`](../../specs/008-whatsapp-channel/data-model.md) — entidades + migrations
> - [`contracts/`](../../specs/008-whatsapp-channel/contracts/) — 6 contratos (webhook, config, templates, Meta Graph, adapters, WS events)
> - [`tasks.md`](../../specs/008-whatsapp-channel/tasks.md) — 149 tarefas, progresso por user story
>
> O conteúdo abaixo foi a versão inicial usada como input para `/speckit-specify`. Mantida
> para histórico — não edite. Atualizações vão no novo formato.

---

## 1. Visão Geral

Este módulo define a integração do OmniDesk com a API Oficial do WhatsApp Business (Meta). Cada tenant pode ter um número de WhatsApp vinculado ao sistema. Mensagens recebidas entram no mesmo pipeline de conversas dos demais canais — a IA responde e, quando necessário, transfere para um atendente humano. O módulo cobre: configuração do canal, recepção via webhook, envio de mensagens, gestão de templates aprovados pela Meta e janela de sessão de 24 horas.

---

## 2. Entidades

### 2.1 Configuração do Canal WhatsApp (`whatsapp_config`)

Uma configuração por tenant. Criada vazia no provisionamento — ativada quando o tenant configura seu número.

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `tenant_id` | UUID | sim | FK → tenants (1:1) |
| `is_enabled` | boolean | sim | Ativa/desativa o canal. Default: `false` (canal inativo até ser configurado). |
| `phone_number` | varchar(20) | não | Número de WhatsApp registrado na Meta (formato E.164: `+5511999999999`). |
| `display_name` | varchar(100) | não | Nome de exibição da conta WhatsApp Business. |
| `waba_id` | varchar(100) | não | WhatsApp Business Account ID (WABA ID) fornecido pela Meta. |
| `phone_number_id` | varchar(100) | não | Phone Number ID da Meta. Usado nas chamadas de API de envio. |
| `access_token` | text | não | Token de acesso permanente da Meta (criptografado em repouso com AES-256). Nunca retornado em texto plano na API. |
| `webhook_verify_token` | varchar(100) | sim | Token usado para verificação do webhook pela Meta. Gerado automaticamente no provisionamento. |
| `business_hours_enabled` | boolean | sim | Se `true`, fora do horário configurado no departamento a IA informa o horário e registra o contato. Default: `false`. |
| `updated_at` | timestamptz | sim | — |

### 2.2 Template de Mensagem (`whatsapp_templates`)

Templates usados para envio de mensagens fora da janela de 24 horas. O sistema possui **tipos pré-definidos** — o tenant personaliza apenas o conteúdo das variáveis. Templates são submetidos à aprovação da Meta diretamente pelo CRM.

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `meta_template_id` | varchar(100) | não | ID do template na Meta. Preenchido após criação/aprovação. |
| `type` | enum | sim | Tipo pré-definido do sistema. Valores: `appointment_reminder`, `appointment_confirmation`, `appointment_cancellation`, `follow_up`, `custom`. |
| `name` | varchar(100) | sim | Nome do template na Meta (snake_case). Gerado automaticamente a partir do `type` + slug do tenant. Ex: `lembrete_consulta_clinicaabc`. |
| `category` | enum | sim | Categoria Meta. Sempre `utility` no MVP. |
| `language` | varchar(10) | sim | Código de idioma. Ex: `pt_BR`. |
| `status` | enum | sim | `draft`, `pending_meta`, `approved`, `rejected`. |
| `body_template` | text | sim | Estrutura fixa do corpo (definida pelo sistema por `type`). Contém placeholders `{{1}}`, `{{2}}` etc. |
| `variable_labels` | text[] | sim | Descrição de cada variável. Ex: `["nome do cliente", "data da consulta", "horário"]`. |
| `submitted_at` | timestamptz | não | Momento do envio para aprovação na Meta. |
| `created_at` | timestamptz | sim | — |
| `updated_at` | timestamptz | sim | — |

### 2.3 Tipos de Template Pré-definidos

| Tipo | Uso | Corpo padrão (editável pelo tenant) |
|---|---|---|
| `appointment_reminder` | Lembrete de consulta (enviado pela Agenda — Spec 11) | "Olá, {{1}}! Lembramos que você tem uma consulta agendada para {{2}} às {{3}}. Confirme com SIM ou cancele com NÃO." |
| `appointment_confirmation` | Confirmação imediata de agendamento | "Olá, {{1}}! Seu agendamento para {{2}} às {{3}} foi confirmado. Até lá!" |
| `appointment_cancellation` | Notificação de cancelamento | "Olá, {{1}}! Seu agendamento de {{2}} foi cancelado. Entre em contato para remarcar." |
| `follow_up` | Follow-up pós-atendimento | "Olá, {{1}}! Seu atendimento foi encerrado. Ficou com alguma dúvida? Estamos à disposição." |
| `custom` | Template livre criado pelo tenant | *(corpo definido inteiramente pelo tenant)* |

> O tenant pode **editar o corpo** dos templates pré-definidos, mas a estrutura de variáveis (quantidade e ordem) é fixa por tipo. O tipo `custom` permite corpo livre.

### 2.4 Campo adicional em `conversations`

| Campo | Tipo | Descrição |
|---|---|---|
| `wa_contact_phone` | varchar(20) | Número de WhatsApp do cliente (formato E.164). Preenchido nas conversas do canal `whatsapp`. |
| `wa_session_expires_at` | timestamptz | Momento em que a janela de 24h expira. Atualizada a cada mensagem recebida do cliente. |

### 2.5 Status de Mensagem WhatsApp (`MongoDB: wa_message_statuses`)

A Meta envia atualizações de status para cada mensagem enviada pelo sistema. Registradas para auditoria.

```json
{
  "tenant_slug": "clinica-abc",
  "message_id": "uuid",
  "wa_message_id": "wamid.xxx",
  "status": "sent" | "delivered" | "read" | "failed",
  "error_code": null,
  "error_message": null,
  "timestamp": "2026-06-03T10:00:00Z"
}
```

---

## 3. Janela de Sessão de 24 Horas (Meta Policy)

A Meta permite envio de mensagens livres (sem template) **apenas dentro de 24 horas após a última mensagem recebida do cliente**. Fora desta janela, apenas templates aprovados podem ser enviados.

### 3.1 Regras da Janela

| Situação | Comportamento |
|---|---|
| Cliente enviou mensagem há menos de 24h | Mensagens livres permitidas. IA e atendente respondem normalmente. |
| Janela de 24h expirada | Somente templates `utility` podem ser enviados. IA não pode iniciar conversa livre. |
| Nenhuma interação anterior | Janela não existe — apenas templates para iniciar contato. |

- `wa_session_expires_at` é atualizado para `now() + 24h` a cada mensagem recebida do cliente
- O backend verifica a janela antes de cada envio de mensagem
- Se a janela estiver expirada, o sistema bloqueia o envio de mensagem livre e sinaliza ao atendente

### 3.2 Envio de Template (fora da janela)

Quando o atendente tenta responder e a janela está expirada:
1. CRM exibe aviso: "A janela de 24h expirou. Selecione um template para enviar."
2. Atendente escolhe o template e preenche as variáveis
3. Mensagem é enviada via API Meta com o template aprovado
4. Se o cliente responder, a janela de 24h é reiniciada

---

## 4. Fluxo de Mensagem Recebida (Webhook)

```
Meta envia POST para: /api/public/whatsapp/webhook
  ↓
Middleware valida assinatura HMAC-SHA256 (X-Hub-Signature-256)
  ↓
Backend retorna 200 OK imediatamente (obrigatório pela Meta — timeout de 20s)
  ↓
Payload enfileirado em Redis: {slug}:incoming_messages
  ↓
IncomingMessageWorker (Hangfire) processa a mensagem:
  ├── Cria/reutiliza visitor pelo wa_contact_phone
  ├── Cria/reutiliza conversation (channel = whatsapp)
  ├── Salva mensagem em messages
  ├── Atualiza wa_session_expires_at
  └── Encaminha para AgentOrchestrator.ProcessAsync()
  ↓
IA responde → OutgoingMessageWorker → Meta API (POST /messages)
```

### 4.1 Verificação do Webhook (Setup)

A Meta faz uma requisição GET no webhook ao configurar o canal:
```
GET /api/public/whatsapp/webhook
  ?hub.mode=subscribe
  &hub.verify_token={webhook_verify_token}
  &hub.challenge={challenge}
```
Backend valida `hub.verify_token` contra `whatsapp_config.webhook_verify_token` e retorna `hub.challenge`.

---

## 5. Tipos de Mensagem Suportados

| Tipo Meta | Suporte no MVP | Armazenamento |
|---|---|---|
| `text` | ✅ | `messages.content` |
| `image` | ✅ | Upload para MinIO → `messages.attachment_url` |
| `document` | ✅ | Upload para MinIO → `messages.attachment_url` |
| `audio` | ✅ (receber) | Upload para MinIO → `messages.attachment_url` |
| `video` | ❌ (ignorado) | — |
| `sticker` | ❌ (ignorado) | — |
| `location` | ❌ (ignorado) | — |
| `contacts` | ❌ (ignorado) | — |
| `reaction` | ❌ (ignorado) | — |
| `interactive` (botões) | ❌ (V2) | — |

> Mensagens de tipos não suportados são ignoradas silenciosamente — não geram erro nem notificação ao cliente.

> Áudio é **recebido e armazenado** no MVP, mas **não há transcrição automática**. O atendente vê o arquivo de áudio e pode ouvir diretamente no CRM.

---

## 6. Envio de Mensagens

### 6.1 Fluxo de Envio

```
Atendente digita mensagem no CRM (ou IA gera resposta)
  ↓
Backend verifica janela de 24h:
  ├── Dentro da janela → envia mensagem de texto livre
  └── Fora da janela → bloqueia; exige template
  ↓
OutgoingMessageWorker envia via Meta API:
  POST https://graph.facebook.com/v19.0/{phone_number_id}/messages
  ↓
Meta retorna wa_message_id
  ↓
Salva wa_message_id na mensagem para rastrear status
  ↓
Status updates chegam via webhook (sent → delivered → read)
```

### 6.2 Status de Entrega no CRM

O CRM exibe ícones de status ao lado de cada mensagem enviada pelo sistema:

| Ícone | Status | Descrição |
|---|---|---|
| ✓ | `sent` | Enviado para os servidores da Meta |
| ✓✓ | `delivered` | Entregue ao dispositivo do cliente |
| ✓✓ (azul) | `read` | Lido pelo cliente |
| ✗ | `failed` | Falha no envio — motivo exibido em tooltip |

---

## 7. Configuração do Canal no CRM

Acessível em: **CRM → Configurações → WhatsApp**

### 7.1 Tela de Configuração

**Status do canal** (badge no topo):
- 🔴 **Não configurado** — nenhum número vinculado
- 🟡 **Configurado / Inativo** — número salvo mas `is_enabled = false`
- 🟢 **Ativo** — canal funcionando

**Campos configuráveis pelo tenant:**

| Campo | Descrição |
|---|---|
| Phone Number ID | ID do número na Meta (copiado do Meta Business Manager) |
| WABA ID | WhatsApp Business Account ID |
| Access Token | Token de acesso permanente da Meta |
| Nome de Exibição | Nome da conta WhatsApp Business |
| Ativar canal | Toggle `is_enabled` |

**Webhook URL (somente leitura):**
```
https://api.omnicare.ia.br/api/public/whatsapp/webhook/{tenant_slug}
```
Exibida para o tenant copiar e configurar no Meta Business Manager.

**Verify Token (somente leitura):**
Exibido para o tenant usar na configuração do webhook na Meta.

### 7.2 Quem configura o canal?

O registro do número na Meta (criação de app, verificação de negócio, geração do Access Token) é feito **manualmente pelo Operador SaaS** fora do sistema. O tenant apenas insere as credenciais prontas nos campos do CRM. Por isso:
- `tenant_admin` insere as credenciais fornecidas pelo Operador no CRM e ativa o canal
- `supervisor` pode **visualizar** se o canal está configurado, mas **não pode editar** o Access Token
- O `saas_admin` pode configurar via impersonation quando necessário

### 7.3 Gestão de Templates

Acessível em: **CRM → Configurações → WhatsApp → Templates**

**Fluxo de criação de template:**
1. Tenant seleciona um **tipo pré-definido** (ou `custom`)
2. Sistema pré-preenche o corpo do template com o padrão do tipo
3. Tenant edita o conteúdo textual (sem alterar a estrutura de variáveis nos tipos pré-definidos)
4. Tenant clica em "Submeter para aprovação da Meta"
5. Backend chama a API da Meta (`POST /message_templates`) com o body e categoria `utility`
6. Status muda para `pending_meta`
7. Quando a Meta aprovar/rejeitar, o webhook de status de template atualiza o campo `status`

**Lista de templates no CRM:**
- Exibe todos os templates com badge de status: Rascunho / Aguardando Meta / Aprovado / Rejeitado
- Templates `approved` ficam disponíveis para seleção ao enviar mensagem fora da janela
- Templates `rejected` exibem o motivo da rejeição retornado pela Meta
- Somente `tenant_admin` e `supervisor` podem criar, editar e submeter templates

---

## 8. Regras de Negócio

- Cada tenant tem **no máximo um número** de WhatsApp vinculado na V1. Múltiplos números são V2.
- O registro do número na Meta é feito **manualmente pelo Operador SaaS** fora do sistema — o sistema apenas armazena as credenciais geradas
- O `access_token` é criptografado com AES-256 antes de salvar e **nunca retornado em texto plano** pela API
- O `webhook_verify_token` é gerado automaticamente no provisionamento e imutável
- Antes de processar qualquer webhook, o backend valida a assinatura HMAC-SHA256 (`X-Hub-Signature-256`). Payloads com assinatura inválida são rejeitados com `403 Forbidden`
- O backend retorna `200 OK` imediatamente ao receber o webhook, antes de processar a mensagem (requisito da Meta — timeout de 20s)
- Mensagens de tipos não suportados são silenciosamente ignoradas
- A IA **não envia templates** — envio de templates é exclusivo do atendente humano
- Se a janela de 24h expirar durante uma conversa com a IA, a IA não pode mais responder; o sistema notifica o atendente para assumir com um template
- Mensagens enviadas fora da janela sem template resultam em erro da API da Meta — o sistema registra o erro e notifica o atendente
- O status `read` (lido pelo cliente) é **apenas visual** no CRM — não impacta SLA, não altera status do ticket, não dispara nenhuma ação automática

---

## 9. Endpoints da API

```
# Webhook público (sem autenticação de usuário — validado por assinatura Meta)
GET    /api/public/whatsapp/webhook/{tenant_slug}   → verificação do webhook (Meta setup)
POST   /api/public/whatsapp/webhook/{tenant_slug}   → recepção de mensagens e status updates

# Configuração do canal (autenticado — CRM)
GET    /api/whatsapp/config                          → obter configuração atual
PUT    /api/whatsapp/config                          → salvar configuração
PATCH  /api/whatsapp/config/toggle                   → ativar / desativar canal

# Templates
GET    /api/whatsapp/templates                       → listar templates do tenant
POST   /api/whatsapp/templates                       → criar template (rascunho)
PUT    /api/whatsapp/templates/{id}                  → editar template (apenas status draft)
POST   /api/whatsapp/templates/{id}/submit           → submeter template para aprovação na Meta
DELETE /api/whatsapp/templates/{id}                  → remover template (apenas draft ou rejected)

# Envio (usado internamente pelo OutgoingMessageWorker — não exposto ao frontend diretamente)
POST   /api/whatsapp/send                            → enviar mensagem (texto livre ou template)
```

---

## 10. Eventos WebSocket (CRM)

| Evento | Payload | Descrição |
|---|---|---|
| `wa.message_status` | `{ message_id, status, timestamp }` | Status de entrega atualizado (sent/delivered/read/failed) |
| `wa.session_expiring` | `{ conversation_id, expires_at }` | Janela de 24h vai expirar em menos de 1 hora |
| `wa.session_expired` | `{ conversation_id }` | Janela de 24h expirou — atendente precisa usar template |

---

## 11. Critérios de Aceite

- [ ] Webhook verificado corretamente com `webhook_verify_token` durante o setup na Meta
- [ ] Assinatura HMAC-SHA256 (`X-Hub-Signature-256`) validada em cada requisição de webhook; payloads inválidos recebem `403`
- [ ] Backend retorna `200 OK` imediatamente ao receber webhook, antes de processar
- [ ] Mensagens de texto, imagem e documento recebidas são salvas e exibidas no CRM
- [ ] Mensagens de áudio são recebidas e armazenadas no MinIO; exibidas no CRM como player de áudio
- [ ] Tipos não suportados (video, sticker, location etc.) são silenciosamente ignorados
- [ ] `wa_session_expires_at` atualizado a cada mensagem recebida do cliente
- [ ] Dentro da janela de 24h: mensagens livres enviadas normalmente
- [ ] Fora da janela: CRM bloqueia envio e exige seleção de template
- [ ] A IA não envia templates — bloqueado no backend
- [ ] Status de entrega (sent/delivered/read/failed) exibido no CRM por mensagem
- [ ] `access_token` nunca retornado em texto plano pela API
- [ ] `supervisor` não pode editar o `access_token` — somente visualizar que está configurado
- [ ] Templates criados com tipo pré-definido têm corpo pré-preenchido e variáveis fixas
- [ ] Tipo `custom` permite corpo livre
- [ ] Submissão de template para Meta feita via botão no CRM (`POST /message_templates` na Graph API)
- [ ] Status do template atualizado via webhook da Meta (approved / rejected)
- [ ] Templates `rejected` exibem motivo da rejeição no CRM
- [ ] Somente templates `approved` ficam disponíveis para seleção ao enviar mensagem fora da janela
- [ ] Status `read` exibido visualmente no CRM — sem impacto em SLA ou fluxo
- [ ] Evento WebSocket `wa.session_expiring` emitido quando janela de 24h expira em menos de 1 hora

---

## 12. Decisões Registradas

| # | Decisão | Registrado em |
|---|---|---|
| P1 | Registro na Meta feito manualmente pelo Operador SaaS fora do sistema — CRM só armazena as credenciais | v1.1 |
| P2 | Templates criados e gerenciados no CRM; tipos pré-definidos pelo sistema; tenant edita conteúdo; submissão à Meta via API pelo CRM incluso na V1 | v1.1 |
| P3 | V1: exatamente 1 número por tenant — múltiplos números são V2 | v1.1 |
| P4 | Status `read` é apenas visual — sem impacto em SLA, status de ticket ou ações automáticas | v1.1 |
