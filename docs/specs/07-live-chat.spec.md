# Spec 07 — Live Chat (Widget)
**Versão:** 1.0
**Status:** Aprovado
**Última atualização:** 2026-05

---

## 1. Visão Geral

O módulo Live Chat fornece um widget JavaScript instalável em qualquer site do tenant. O widget é o canal de entrada de conversas via web — o cliente final interage com o Agente de IA (e, quando necessário, com um atendente humano) sem sair do site do tenant. Este módulo cobre: o widget front-end, a configuração visual por tenant, a gestão de sessões de conversa, a persistência do histórico e a comunicação em tempo real via WebSocket.

O comportamento de conversas é similar ao WhatsApp Business: múltiplas conversas simultâneas são listadas no CRM à esquerda, com a conversa selecionada à direita.

---

## 2. Componentes

| Componente | Responsável |
|---|---|
| **Widget** | Script JS carregado no site do tenant. Interface do cliente final. |
| **Painel de Configuração** | Tela no CRM do tenant para personalizar e controlar o widget. |

---

## 3. Entidades

### 3.1 Configuração do Widget (`widget_config`)

Uma configuração por tenant. Criada automaticamente no provisionamento com valores padrão.

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `tenant_id` | UUID | sim | FK → tenants (1:1) |
| `widget_token` | UUID | sim | Token público fixo gerado no provisionamento. Identifica o tenant nas requisições públicas do widget. Não é secreto. Não expira. Imutável. |
| `is_enabled` | boolean | sim | Liga/desliga o widget. Default: `true` |
| `primary_color` | varchar(7) | sim | Cor principal em hex. Default: `#2563EB` |
| `launcher_icon` | enum | sim | `chat`, `message`, `support`. Default: `chat` |
| `company_name` | varchar(100) | sim | Nome exibido no cabeçalho do widget |
| `welcome_message` | text | sim | Mensagem exibida antes de o cliente iniciar a conversa |
| `input_placeholder` | varchar(150) | não | Default: "Digite uma mensagem…" |
| `position` | enum | sim | `bottom_right` ou `bottom_left`. Default: `bottom_right` |
| `require_identification` | boolean | sim | Se `true`, exibe formulário pré-chat. Default: `false` |
| `identification_fields` | jsonb | não | Campos do formulário pré-chat. Ver seção 3.2. |
| `allowed_domains` | text[] | não | Domínios autorizados. Vazio = sem restrição. |
| `privacy_policy_text` | text | sim | Texto dos termos de privacidade/LGPD. Obrigatório para que o widget aceite mensagens. |
| `privacy_policy_url` | varchar(500) | não | Link para política completa (abre em nova aba). |
| `abandonment_timeout_hours` | int | sim | Horas de inatividade (conversa com IA) até marcar como `abandoned`. Default: `8`. |
| `inactivity_close_hours` | int | sim | Horas de inatividade (conversa com humano) até encerramento automático. Default: `24`. |
| `updated_at` | timestamptz | sim | — |

### 3.2 Formulário Pré-chat (`identification_fields`)

JSONB dentro de `widget_config`. Ativo apenas quando `require_identification = true`.

```json
[
  { "field": "name",  "label": "Seu nome",   "required": true  },
  { "field": "email", "label": "Seu e-mail", "required": false }
]
```

- Campos disponíveis: `name`, `email`, `phone`
- Cada campo pode ser obrigatório ou opcional individualmente

### 3.3 Conversa (`conversations`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `channel` | enum | sim | `live_chat` (nesta spec) ou `whatsapp` (spec 06) |
| `status` | enum | sim | `open`, `resolved`, `abandoned` |
| `visitor_id` | UUID | sim | FK → visitors |
| `contact_id` | UUID | não | FK → contacts. Preenchido se o visitante se identificou |
| `agent_id` | UUID | não | FK → ai_agents. Agente de IA responsável atual |
| `attendant_id` | UUID | não | FK → attendants. Atendente humano responsável (após transbordo) |
| `department_id` | UUID | não | FK → departments |
| `ticket_id` | UUID | não | FK → tickets. Preenchido após transbordo |
| `lgpd_consent_at` | timestamptz | não | Momento do aceite dos termos pelo visitante. `null` = não aceitou. |
| `ended_by` | enum | não | `attendant`, `ai_agent`, `system_inactivity`, `system_disable` |
| `metadata` | jsonb | não | `page_url`, `page_title`, `referrer`, `user_agent`, `ip_address` (3 primeiros octetos IPv4) |
| `ended_at` | timestamptz | não | — |
| `created_at` | timestamptz | sim | — |
| `updated_at` | timestamptz | sim | — |

### 3.4 Visitante (`visitors`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `anonymous_id` | UUID | sim | UUID gerado via `crypto.randomUUID()` na primeira visita e salvo em `localStorage`. Permite reconhecer o mesmo navegador sem fingerprinting. |
| `name` | varchar(255) | não | Informado no formulário pré-chat ou durante a conversa |
| `email` | varchar(255) | não | Idem |
| `phone` | varchar(20) | não | Idem |
| `created_at` | timestamptz | sim | — |

> **Privacidade:** Não utiliza fingerprinting de dispositivo (canvas, fontes, etc.). Se o visitante limpar o `localStorage`, um novo `anonymous_id` é gerado e uma nova conversa é iniciada.

### 3.5 Mensagem (`messages`)

Tabela compartilhada com o módulo WhatsApp (spec 06).

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `conversation_id` | UUID | sim | FK → conversations |
| `sender_type` | enum | sim | `visitor`, `ai_agent`, `attendant`, `system` |
| `sender_id` | UUID | não | UUID do remetente. Nulo para mensagens `system`. |
| `content_type` | enum | sim | `text`, `image`, `file`, `system_event` |
| `content` | text | não | Texto da mensagem |
| `attachment_url` | varchar(500) | não | URL no MinIO |
| `attachment_name` | varchar(255) | não | Nome original do arquivo |
| `attachment_size_bytes` | int | não | Tamanho em bytes |
| `is_read` | boolean | sim | Default: `false` |
| `created_at` | timestamptz | sim | — |

---

## 4. Instalação do Widget

```html
<script>
  window.OmniDeskConfig = { token: "WIDGET_TOKEN_UUID" };
</script>
<script src="https://cdn.omnicare.ia.br/widget/v1/loader.js" async></script>
```

- O `token` é o `widget_token` (UUID fixo) — público, não secreto, não expira
- Script servido via CDN (Cloudflare), carregamento assíncrono

### 4.1 Restrição por Domínio

Se `allowed_domains` estiver preenchido, o backend valida o header `Origin` de cada requisição WebSocket e chamada à API pública. Origens não autorizadas: `403 Forbidden`. Campo vazio = sem restrição.

---

## 5. Interface do Widget (Front-end)

### 5.1 Botão Flutuante (Launcher)

- Ícone e cor conforme configuração; posicionado conforme `position`
- Ao clicar: abre o painel com animação de slide-up
- Badge numérico exibe mensagens não lidas (quando widget fechado)
- Badge é zerado quando o visitante abre o widget (evento `messages.read` enviado ao backend)

### 5.2 Painel do Widget

```
┌─────────────────────────────┐
│  [Avatar] NomeDaEmpresa  [X]│  ← cabeçalho com cor primária
├─────────────────────────────┤
│                             │
│    Área de mensagens        │  ← scroll, bolhas
│                             │
├─────────────────────────────┤
│  [📎] [Campo de texto] [➤]  │
└─────────────────────────────┘
```

**Estados do painel:**

| Estado | Exibição |
|---|---|
| Primeira visita | `welcome_message` + formulário pré-chat (se configurado) + checkbox LGPD |
| Conversa ativa | Histórico de mensagens em tempo real |
| Widget desabilitado | "No momento o atendimento está indisponível." (sem campo de envio) |
| Conversa encerrada | Mensagem de encerramento + botão "Iniciar nova conversa" |

**Layout das mensagens:**
- IA/atendente: alinhadas à esquerda, com avatar
- Visitante: alinhadas à direita
- `system_event`: centralizadas, texto menor, cor neutra
- Indicador "digitando…": quando agente ou atendente está compondo

### 5.3 Consentimento LGPD

Obrigatório antes do envio de qualquer mensagem:

- Checkbox: "Li e aceito os [Termos de Privacidade]" (link abre `privacy_policy_url` em nova aba)
- O texto exibido é `privacy_policy_text` cadastrado no CRM do tenant
- **Botão de envio fica desabilitado até o aceite**
- Aceite registrado em `conversations.lgpd_consent_at`
- Se `privacy_policy_text` estiver vazio, o widget exibe um texto padrão genérico de aviso de coleta de dados e o CRM exibe alerta ao tenant

### 5.4 Envio de Arquivos

- Tipos: imagens (jpg, png, gif, webp), documentos (pdf, docx, xlsx)
- Tamanho máximo: **10 MB**
- Upload via `POST /api/public/widget/upload` → salvo no MinIO → URL enviada via WebSocket

### 5.5 Persistência Local (`localStorage`)

| Chave | Valor |
|---|---|
| `omnidesk_visitor_id` | `anonymous_id` do visitante (UUID) |
| `omnidesk_conversation_id` | ID da conversa ativa |

**Comportamento ao retornar:**

| Status da conversa | Ação |
|---|---|
| `open` (com IA) | Retoma normalmente — IA continua de onde parou |
| `open` (com humano) | Retoma normalmente — aguarda atendente |
| `resolved` | Exibe histórico + botão "Iniciar nova conversa" |
| `abandoned` | Inicia nova conversa automaticamente |

---

## 6. Fluxo de uma Conversa

### 6.1 Início

```
Visitante abre o widget
  ↓
[Se widget desabilitado] → exibe "indisponível". Fim.
  ↓
[Se há conversa open no localStorage] → retoma (seção 5.5)
  ↓
[Nova conversa]
  ├── Exibe welcome_message
  ├── [Se require_identification] → formulário pré-chat
  └── Checkbox LGPD (obrigatório)
  ↓
Visitante aceita LGPD e envia primeira mensagem
  ↓
Backend cria/reutiliza visitor (pelo anonymous_id) + cria conversation (status = open)
  ↓
Agente Orchestrator recebe e responde via WebSocket
```

### 6.2 Encerramento pela IA

A IA encerra quando detecta conclusão natural do fluxo (ex: agendamento confirmado, dúvida resolvida):
1. Envia mensagem de despedida ao visitante
2. `status → resolved`, `ended_by = ai_agent`, `ended_at` preenchido
3. Widget exibe mensagem de encerramento + botão "Iniciar nova conversa"

### 6.3 Encerramento pelo Atendente Humano

Após transbordo, apenas o atendente pode encerrar manualmente:
- Clica em "Encerrar conversa" no CRM
- `status → resolved`, `ended_by = attendant`
- Widget notifica o visitante: "O atendimento foi encerrado."
- Se o visitante enviar nova mensagem após encerramento → nova conversa com Agente Orchestrator (fluxo inicial)

**Encerramento por inatividade (conversa com humano):**
- Após `inactivity_close_hours` sem mensagem nova → encerramento automático
- `ended_by = system_inactivity`
- Visitante que retornar cai no fluxo inicial (Agente Orchestrator)

### 6.4 Abandono (conversa com IA)

- Sem mensagem por `abandonment_timeout_hours` horas → `status = abandoned`
- O timer reinicia a cada nova mensagem
- Job automático (Hangfire) verifica a cada hora
- Conversas com atendente humano **não** são marcadas como `abandoned` — seguem a regra de `inactivity_close_hours`

### 6.5 Desabilitação do Widget pelo Tenant

Quando `is_enabled` é alterado para `false`:
1. Todas as conversas `open` recebem mensagem automática: "O atendimento foi encerrado pelo sistema."
2. Status → `resolved`, `ended_by = system_disable`
3. Na próxima visita, o widget exibe: "No momento o atendimento está indisponível." (sem campo de envio)
4. Histórico de conversas anteriores é preservado no CRM

### 6.6 Reabertura de Conversa `resolved`

- Visitante pode iniciar nova conversa após uma `resolved`
- O Agente Orchestrator recebe como contexto as últimas **50 mensagens** da conversa anterior (limite configurável via variável de ambiente do backend)
- Esse limite existe para controlar custo e tamanho de prompt enviado à OpenAI

---

## 7. Múltiplas Conversas — Visão do Atendente no CRM

```
┌──────────────────┬─────────────────────────────────────┐
│  Lista de        │                                     │
│  Conversas       │      Conversa Selecionada           │
│                  │                                     │
│  [🔴] João S.    │   João Silva — Live Chat            │
│  [🟡] Maria O.   │   ─────────────────────────────     │
│  [⚪] Carlos M.  │   Histórico de mensagens...         │
│                  │                                     │
│  ─ Encerradas ─  │   [📎] [Campo de texto]  [Enviar]   │
│  (não exibidas)  │                                     │
└──────────────────┴─────────────────────────────────────┘
```

- Painel esquerdo: lista de conversas **ativas** do atendente (ou do departamento)
- Badge colorido: nova (vermelho), em andamento (amarelo), aguardando cliente (cinza)
- Conversas `resolved` e `abandoned` **não aparecem** na lista
- Atendente pode ter múltiplas conversas abertas (respeitando `max_simultaneous_chats`)

---

## 8. Notificações do Atendente (Browser)

O CRM solicita permissão de notificações do browser na primeira sessão do atendente.

| Evento | Notificação |
|---|---|
| Nova conversa atribuída | "Nova conversa de [Nome ou Anônimo]" |
| Nova mensagem em conversa aberta | "[Nome]: [prévia da mensagem]" |
| Conversa transferida ao atendente | "Conversa transferida por [Nome]" |

**Regras:**
- Notificação apenas quando o CRM está em background ou minimizado
- Se o atendente estiver com a conversa focada → apenas indicador visual na lista
- Gerenciamento de permissão disponível em CRM → Configurações → Notificações

---

## 9. Comunicação em Tempo Real (WebSocket)

Toda comunicação em andamento é via WebSocket. O histórico inicial (ao abrir o widget ou retomar conversa) é carregado via REST antes de conectar o WebSocket.

### 9.1 Eventos backend → widget

| Evento | Payload | Descrição |
|---|---|---|
| `message.new` | `{ message_id, sender_type, content_type, content, attachment_url, created_at }` | Nova mensagem |
| `agent.typing` | `{ sender_type }` | Agente/atendente digitando |
| `conversation.assigned` | `{ attendant_name }` | Atendente assumiu |
| `conversation.resolved` | `{ ended_by }` | Conversa encerrada |

### 9.2 Eventos widget → backend

| Evento | Payload | Descrição |
|---|---|---|
| `message.send` | `{ conversation_id, content_type, content }` | Visitante envia mensagem |
| `visitor.typing` | `{ conversation_id }` | Visitante digitando (debounce 1s) |
| `messages.read` | `{ conversation_id }` | Visitante abriu o widget |

### 9.3 Eventos backend → CRM

| Evento | Payload | Descrição |
|---|---|---|
| `chat.new_conversation` | `{ conversation_id, visitor_name, page_url }` | Nova conversa iniciada |
| `chat.message_received` | `{ conversation_id, message_id, content }` | Nova mensagem do visitante |
| `chat.visitor_typing` | `{ conversation_id }` | Visitante digitando |
| `chat.browser_notify` | `{ type, title, body, conversation_id }` | Gatilho para browser notification |

### 9.4 Reconexão

- Backoff exponencial: 1s, 2s, 4s… até 30s
- Mensagens enviadas durante desconexão são enfileiradas no cliente e enviadas ao reconectar
- Banner discreto no widget informa o visitante quando desconectado

---

## 10. Painel de Configuração do Widget (CRM)

Acessível em: **CRM → Configurações → Live Chat**

### 10.1 Aba "Aparência"

Campos: cor primária (color picker), ícone do launcher (seleção visual), posição, nome da empresa, mensagem de boas-vindas, placeholder do campo de texto.

**Preview ao vivo:** widget real renderizado à direita do formulário. Consome `GET /api/public/widget/init` com o `widget_token` do próprio tenant. Alterações no formulário refletem no preview em tempo real (sem salvar). O preview funciona dentro do CRM sem restrição de domínio.

### 10.2 Aba "Identificação"

- Toggle: `require_identification`
- Se ativado: seleção de campos (nome, e-mail, telefone) com flag de obrigatório por campo

### 10.3 Aba "Privacidade / LGPD"

- Textarea para `privacy_policy_text` (obrigatório)
- Campo URL para `privacy_policy_url` (opcional)
- Alerta visível se `privacy_policy_text` estiver vazio: "⚠️ Configure os termos de privacidade. O widget exibirá um texto genérico enquanto este campo estiver vazio."

### 10.4 Aba "Comportamento"

- Campo numérico: `abandonment_timeout_hours` — "Timeout de abandono (conversa com IA, em horas)". Default: 8.
- Campo numérico: `inactivity_close_hours` — "Encerramento por inatividade (conversa com humano, em horas)". Default: 24.

### 10.5 Aba "Segurança"

- Campo de texto: domínios autorizados (um por linha)

### 10.6 Aba "Instalação"

- Trecho de código HTML pronto para copiar (somente leitura)
- Botão "Copiar código"

### 10.7 Toggle Geral

- Botão liga/desliga no topo da página (`is_enabled`)
- Ao desligar: encerra todas as conversas abertas com mensagem automática (ver seção 6.5)

---

## 11. Regras de Negócio

- Configuração única por tenant — não há configurações por página ou por domínio
- `widget_token` é imutável após geração no provisionamento
- O visitante não pode enviar mensagem sem aceitar os termos LGPD (`lgpd_consent_at` deve estar preenchido)
- Mensagens `system_event` não são processadas pelo Agente de IA
- Arquivos são validados por tipo MIME no backend, não apenas pela extensão
- O histórico inicial ao retomar conversa é carregado via REST; toda comunicação posterior é via WebSocket
- Roles com permissão para alterar configuração do widget: a ser definido na Spec de Autenticação

---

## 12. Endpoints da API

```
# Configuração do widget (autenticado — CRM)
GET    /api/widget/config                              → configuração atual
PUT    /api/widget/config                              → salvar configuração
PATCH  /api/widget/config/toggle                       → ligar/desligar widget

# Endpoints públicos (autenticados pelo widget_token)
GET    /api/public/widget/init                         → config pública + verificar conversa ativa pelo anonymous_id
POST   /api/public/widget/conversations                → criar nova conversa
GET    /api/public/widget/conversations/{id}/messages  → histórico paginado (carga inicial)
POST   /api/public/widget/upload                       → upload de arquivo → retorna URL MinIO

# WebSocket
WS     /ws/widget/{conversation_id}                    → canal em tempo real (visitante ↔ backend)
WS     /ws/crm                                         → canal do CRM (atendente ↔ backend)
```

---

## 13. Critérios de Aceite

- [ ] O widget carrega de forma assíncrona sem impactar o tempo de carregamento da página host
- [ ] `allowed_domains` configurado: origens não autorizadas recebem `403`
- [ ] O visitante não consegue enviar mensagem sem aceitar os termos LGPD
- [ ] `lgpd_consent_at` é preenchido no momento do aceite
- [ ] O `anonymous_id` é gerado com `crypto.randomUUID()` e persistido em `localStorage`
- [ ] O histórico da conversa `open` é restaurado corretamente na próxima visita
- [ ] Conversa `open` com IA ao retornar: IA continua sem solicitar dados já fornecidos
- [ ] Conversa `resolved` ao retornar: exibe histórico e oferece nova conversa
- [ ] Nova conversa após `resolved`: IA recebe contexto das últimas 50 mensagens anteriores
- [ ] Conversas sem atividade por `abandonment_timeout_hours` (IA) são marcadas como `abandoned`
- [ ] Conversas com humano sem atividade por `inactivity_close_hours` são encerradas automaticamente
- [ ] Ao desligar o widget: todas as conversas abertas são encerradas com mensagem automática
- [ ] Próxima visita com widget desabilitado: exibe "indisponível" sem campo de envio
- [ ] Indicador "digitando…" aparece no widget quando agente/atendente compõe resposta
- [ ] Widget reconecta automaticamente após queda, sem perda de mensagens
- [ ] Arquivos > 10 MB são rejeitados com mensagem de erro clara
- [ ] Tipo MIME validado no backend (não apenas extensão)
- [ ] Preview ao vivo no CRM reflete a configuração real usando o `widget_token` do tenant
- [ ] Atendente recebe browser notification para nova conversa e nova mensagem (quando CRM em background)
- [ ] Badge de não lidas no launcher é zerado ao abrir o widget
- [ ] Mensagens `system_event` não são enviadas ao Agente de IA
