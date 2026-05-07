# Spec 06 — Agentes de IA
**Versão:** 1.0
**Status:** Aprovado
**Última atualização:** 2026-05

---

## 1. Visão Geral

Este módulo define a camada de inteligência artificial do OmniDesk. Todo contato inicial de um cliente — seja por Live Chat ou WhatsApp — é atendido pelo Agente de IA. A arquitetura usa **orquestração dinâmica em dois níveis**: um Agente Principal (Orchestrator) obrigatório por tenant e Sub-agentes especializados criados pelo próprio tenant. Os agentes são alimentados por GPT-4o via OpenAI Agents SDK e se comunicam com o restante do sistema via tool calls.

---

## 2. Arquitetura de Orquestração

```
Mensagem do cliente
  ↓
Redis Queue: {slug}:incoming_messages
  ↓
IncomingMessageWorker (Hangfire)
  ↓
AgentOrchestrator.ProcessAsync()
  ├── Monta contexto: histórico + lista de sub-agentes ativos (nome + descritivo)
  ├── Chama GPT-4o (Orchestrator)
  │     ├── Responde diretamente → resposta enviada ao canal
  │     ├── tool_call: handoff_to_agent → instancia Sub-agente → processa → responde
  │     └── tool_call: transfer_to_human → cria ticket → notifica atendente
  └── Resposta publicada em: Redis → OutgoingMessageWorker → canal (WS / WhatsApp)
```

### 2.1 Fluxo de Orquestração (passo a passo)

1. Cliente envia mensagem → entra na fila Redis do tenant
2. Worker consome a fila e chama `AgentOrchestrator.ProcessAsync()`
3. O backend monta o contexto:
   - Histórico das últimas **N mensagens** da conversa (N definido em `ai_settings.context_window_messages` do tenant, configurável no CRM, default: 20)
   - Lista de sub-agentes ativos: `[{ id, nome, descritivo }]`
4. Chama GPT-4o com o prompt do Orchestrator + contexto + lista de sub-agentes
5. **GPT-4o decide:**
   - Responder diretamente (caso genérico, saudação, dúvida que o Orchestrator resolve)
   - Chamar `handoff_to_agent` com `agent_id` → Sub-agente especializado assume
   - Chamar `transfer_to_human` com `department_id` e `reason` → Transbordo para atendente
6. Resposta é publicada na fila de saída e entregue ao canal

### 2.2 Persistência de Contexto

- Cada conversa tem um `thread_id` do OpenAI (Assistants API) armazenado em `conversations.openai_thread_id`
- O thread persiste toda a troca de mensagens — o backend não precisa reenviar histórico completo a cada mensagem; apenas envia a nova mensagem no thread existente
- Quando um Sub-agente assume, ele opera no **mesmo thread** — contexto completo preservado
- Threads são criados no primeiro contato e mantidos enquanto a conversa estiver `open`

---

## 3. Entidades

### 3.1 Agente de IA (`ai_agents`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `type` | enum | sim | `orchestrator` ou `sub_agent` |
| `name` | varchar(100) | sim | Nome de exibição. Ex: "Agente Comercial" |
| `short_description` | varchar(300) | sim | Descritivo curto usado pelo Orchestrator para decidir quando acionar este sub-agente. Não é visto pelo cliente. |
| `prompt` | text | sim | Prompt completo do agente (instruções de comportamento, tom, regras de negócio, limitações) |
| `model` | varchar(50) | sim | Modelo OpenAI a usar. Default: `gpt-4o`. Configurável por agente. |
| `department_id` | UUID | não | FK → departments. Obrigatório para `sub_agent`. Define para qual departamento o ticket vai em caso de transbordo humano. `null` para `orchestrator`. |
| `openai_assistant_id` | varchar(100) | não | ID do Assistant criado na OpenAI. Gerado automaticamente ao ativar o agente. |
| `is_active` | boolean | sim | Apenas agentes ativos aparecem na lista do Orchestrator. |
| `created_by` | UUID | sim | FK → attendants (quem criou). |
| `created_at` | timestamptz | sim | — |
| `updated_at` | timestamptz | sim | — |

> **Regra:** Cada tenant tem exatamente **um** agente com `type = orchestrator`. Não é possível criar ou deletar o Orchestrator — apenas editar seu prompt.

### 3.2 Configurações de IA do Tenant (`ai_settings`)

Uma configuração por tenant. Criada automaticamente no provisionamento.

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `tenant_id` | UUID | sim | FK → tenants (1:1) |
| `context_window_messages` | int | sim | Número de mensagens do histórico enviadas como contexto à IA. Default: `20`. Mín: 5, Máx: 100. Impacta custo de tokens. |
| `available_models` | text[] | não | Lista de modelos OpenAI habilitados para este tenant. Vazio = usa lista global do sistema. |
| `updated_at` | timestamptz | sim | — |

> Acessível em: **CRM → Configurações → Agentes de IA → Configurações Avançadas**.

### 3.3 Campo adicional em `conversations`

| Campo | Tipo | Descrição |
|---|---|---|
| `openai_thread_id` | varchar(100) | ID do thread OpenAI da conversa. Criado no primeiro contato. |
| `current_agent_id` | UUID | FK → ai_agents. Sub-agente ativo no momento. `null` se for o Orchestrator ou humano. |

### 3.4 Log de Atividade dos Agentes (`MongoDB: agent_activity_logs`)

Cada interação de IA gera um documento para análise e auditoria:

```json
{
  "tenant_slug": "clinica-abc",
  "conversation_id": "uuid",
  "agent_id": "uuid",
  "agent_name": "Agente Comercial",
  "agent_type": "sub_agent",
  "action": "respond" | "handoff_to_agent" | "transfer_to_human",
  "input_tokens": 450,
  "output_tokens": 120,
  "model": "gpt-4o",
  "latency_ms": 1240,
  "handoff_target_agent_id": null,
  "handoff_target_department_id": null,
  "timestamp": "2026-06-02T14:30:00Z"
}
```

---

## 4. Tool Calls dos Agentes

Os agentes têm acesso a um conjunto fixo de ferramentas implementadas no backend. **Todos os agentes** (Orchestrator e Sub-agentes) têm acesso às mesmas tools — um sub-agente pode fazer handoff para outro sub-agente ou devolver ao Orchestrator.

### 4.1 `handoff_to_agent` — disponível para todos os agentes

```json
{
  "name": "handoff_to_agent",
  "description": "Transfere a conversa para outro agente (Orchestrator ou Sub-agente) quando identificada mudança de contexto ou intenção.",
  "parameters": {
    "agent_id": "UUID do agente de destino (pode ser qualquer agente ativo do tenant)",
    "reason": "Motivo da transferência (interno, não enviado ao cliente)"
  }
}
```

### 4.2 `transfer_to_human`

```json
{
  "name": "transfer_to_human",
  "description": "Transfere a conversa para um atendente humano e abre um ticket no departamento correto.",
  "parameters": {
    "department_id": "UUID do departamento",
    "reason": "Motivo do transbordo (interno, usado na abertura do ticket)"
  }
}
```

### 4.3 `check_availability` — ⚠️ a detalhar na Spec de Agenda

```json
{
  "name": "check_availability",
  "description": "Consulta horários disponíveis na agenda do tenant.",
  "parameters": {
    "professional_id": "UUID do profissional",
    "date": "Data no formato YYYY-MM-DD"
  }
}
```

### 4.4 `create_appointment` — ⚠️ a detalhar na Spec de Agenda

```json
{
  "name": "create_appointment",
  "description": "Cria um agendamento para o cliente após confirmação.",
  "parameters": {
    "professional_id": "UUID do profissional",
    "datetime": "ISO 8601",
    "client_name": "string",
    "client_phone": "string"
  }
}
```

---

## 5. Detecção de Transbordo para Humano

O transbordo é acionado pelo agente via tool call `transfer_to_human`. **A decisão de quando transbordar é 100% responsabilidade do prompt de cada agente** — não há lógica hardcoded de detecção de frustração ou loop no backend. A única exceção são palavras-chave explícitas do cliente.

### 5.1 Gatilho obrigatório (hardcoded no backend)

| Gatilho | Descrição |
|---|---|
| **Palavra-chave explícita** | Cliente usa: "quero falar com alguém", "atendente", "humano", "gerente", "responsável". O backend detecta no texto e injeta instrução de transbordo no contexto antes de chamar a IA, garantindo que ela execute o transbordo mesmo que o prompt não preveja esta situação. |

### 5.2 Gatilhos configurados via prompt (responsabilidade do tenant)

- Solicitação de agendamento de novo cliente (exige confirmação humana)
- Reclamações formais
- Pedidos de reembolso ou cancelamento
- Qualquer situação que o tenant entenda como sensível

### 5.3 Fluxo do Transbordo

```
Agente chama transfer_to_human({ department_id, reason })
  ↓
Agente envia mensagem ao cliente: "Vou transferir você para nossa equipe de [Nome do Departamento].
Aguarde um momento."
  ↓
Backend cria Ticket (ver Spec 09):
  - canal: live_chat ou whatsapp
  - department_id: do parâmetro da tool
  - status: queued
  - histórico: todas as mensagens da conversa (incluindo as da IA)
  - motivo_abertura: reason da tool call
  ↓
conversation.attendant_id preenchido após atribuição
conversation.agent_id → null (IA não processa mais mensagens)
  ↓
Atendente humano notificado (ver Spec 10)
```

### 5.4 Falha na OpenAI (Timeout / Erro de API)

Se a chamada à API da OpenAI falhar (timeout, erro 5xx, rate limit esgotado):

1. O erro é registrado em `agent_activity_logs` com `action: "api_error"` e detalhes do erro
2. O sistema aciona automaticamente `transfer_to_human` com `reason: "Falha técnica no agente de IA"`
3. O cliente recebe mensagem: "Estamos com uma instabilidade técnica no momento. Vou transferir você para um de nossos atendentes."
4. O fluxo de transbordo segue normalmente (ticket criado, atendente notificado)
5. Se não houver departamento definido para o agente atual (ex: Orchestrator), o ticket vai para o **departamento padrão do tenant** (⚠️ campo `default_department_id` a ser adicionado nas configurações do tenant — ver Spec 03 — Tenants)

**Política de retry antes do transbordo:**
- 1 retry após 3 segundos — se falhar novamente, aciona o transbordo
- Sem retry em caso de erro de autenticação (401/403 da OpenAI)

---

## 6. Configuração dos Agentes no CRM

Acessível em: **CRM → Configurações → Agentes de IA**

### 6.1 Visão Geral da Tela

Listagem de todos os agentes do tenant em cards:
- Badge de tipo: **Orchestrator** (único, fixo) ou **Sub-agente**
- Status: ativo / inativo
- Nome + descritivo curto
- Departamento vinculado (para sub-agentes)
- Botão de editar

### 6.2 Orchestrator — Edição

O Orchestrator não pode ser criado nem excluído. O tenant edita apenas:
- **Nome** — exibido ao cliente como identificador do remetente nas mensagens. O tenant decide se usa um nome neutro (ex: "Assistente") ou declara que é IA (ex: "Aria | IA"). Não há obrigatoriedade técnica de revelar que é IA — fica a critério do tenant e de suas obrigações legais.
- **Prompt** (campo de texto grande com editor simples)
- **Modelo** (seletor de modelo OpenAI — default: `gpt-4o`)

> O prompt base do Orchestrator é gerado automaticamente no provisionamento a partir do template global definido no Admin (ver Spec 03). O tenant edita sobre esse template.

### 6.3 Sub-agente — Criação / Edição

Formulário:

| Campo | Descrição |
|---|---|
| Nome | Nome de exibição do agente |
| Descritivo curto | Texto que o Orchestrator usa para decidir quando acionar este agente. Dica de preenchimento exibida no formulário: "Descreva em uma frase o que este agente faz e quando deve ser acionado." |
| Departamento vinculado | Seletor dos departamentos do tenant. Define para onde vai o ticket em caso de transbordo. |
| Modelo | Seletor de modelo OpenAI. Default: `gpt-4o`. |
| Prompt completo | Editor de texto grande. Suporta variáveis: `{{company_name}}`, `{{attendant_name}}` (do departamento vinculado), `{{department_name}}`. |
| Status | Toggle ativo/inativo |

### 6.4 Desativação de Sub-agente

- Sub-agente inativo **não aparece** na lista enviada ao Orchestrator
- O Orchestrator não poderá mais rotear para ele
- Conversas em andamento com o sub-agente são concluídas; novas mensagens caem no Orchestrator
- Sub-agentes não podem ser deletados fisicamente se houver histórico de conversas vinculado (soft delete via `is_active = false` + `deleted_at`)

### 6.5 Testador de Agente (Playground)

Disponível na tela de edição de cada agente:
- Campo de texto simples para enviar mensagem de teste
- Resposta do agente exibida abaixo em tempo real
- **Não cria conversa real** — usa uma thread temporária descartada após o teste
- Útil para validar o prompt antes de ativar o agente

---

## 7. Integração com OpenAI

### 7.1 Modelo de Assistants

- Cada agente (`ai_agents`) corresponde a um **OpenAI Assistant** criado via API
- O `openai_assistant_id` é armazenado no banco e reutilizado nas chamadas
- Ao criar ou editar o prompt/modelo de um agente → o Assistant correspondente é atualizado via API OpenAI (`PATCH /v1/assistants/{id}`)
- Ao ativar um agente que ainda não tem `openai_assistant_id` → o Assistant é criado automaticamente

### 7.2 Threads

- Cada conversa tem um `openai_thread_id` — criado no primeiro contato
- Todos os agentes da mesma conversa compartilham o mesmo thread
- O handoff entre Orchestrator e Sub-agente é feito alterando qual Assistant processa o próximo `run` no mesmo thread

### 7.3 Credenciais OpenAI

Ordem de prioridade para a API Key e credenciais:
1. **Key própria do tenant** (`tenants.openai_api_key`) — se configurada
2. **Key global do sistema** (`OPENAI_API_KEY` no `.env` da API) — fallback padrão

---

## 8. Regras de Negócio

- Cada tenant tem exatamente um agente `orchestrator` — criado no provisionamento, não pode ser deletado
- O Orchestrator recebe **todas** as mensagens iniciais — nunca um sub-agente recebe a primeira mensagem diretamente
- Um agente (Orchestrator ou Sub-agente) pode fazer handoff para qualquer outro agente ativo do tenant via `handoff_to_agent`
- A decisão de quando transbordar para humano é 100% responsabilidade do prompt — não há lógica de detecção de frustração hardcoded no backend
- Após transbordo para humano, a IA **não processa mais mensagens** na conversa — mesmo que o cliente envie algo enquanto aguarda o atendente
- Se o cliente enviar mensagem enquanto aguarda atendente, o sistema envia mensagem automática do sistema: "Sua mensagem foi recebida. Um atendente responderá em breve." (sem processar pela IA)
- Se nenhum sub-agente for adequado e o Orchestrator não conseguir resolver, ele deve acionar `transfer_to_human` — nunca deve deixar o cliente sem resposta
- Em caso de falha na API da OpenAI: 1 retry após 3s; se persistir, transbordo automático para humano com log do erro
- O nome do agente exibido ao cliente é de responsabilidade do tenant — não há obrigatoriedade técnica de revelar que é IA
- O modelo GPT-4o é o padrão; o tenant pode selecionar outro modelo OpenAI disponível
- O número de mensagens de contexto enviadas à IA é configurável por tenant (`ai_settings.context_window_messages`, default: 20)
- Logs de atividade (`agent_activity_logs`) são gravados no MongoDB a cada run — incluindo tokens consumidos, latência e erros de API

---

## 9. Endpoints da API

```
# Agentes (autenticado — CRM)
GET    /api/agents                          → listar agentes do tenant
GET    /api/agents/{id}                     → detalhar agente
POST   /api/agents                          → criar sub-agente
PUT    /api/agents/{id}                     → editar agente (orchestrator ou sub-agente)
DELETE /api/agents/{id}                     → desativar sub-agente (soft delete)
PATCH  /api/agents/{id}/toggle              → ativar / desativar sub-agente

# Testador (playground)
POST   /api/agents/{id}/test                → enviar mensagem de teste → retorna resposta do agente

# Sugestão de resposta (para atendente humano — ver Spec 05)
POST   /api/conversations/{id}/suggest-reply → solicitar sugestão de resposta da IA
```

---

## 10. Critérios de Aceite

- [ ] Cada tenant tem exatamente um agente `orchestrator` criado no provisionamento
- [ ] O Orchestrator não pode ser criado, excluído nem ter seu `type` alterado
- [ ] Sub-agentes inativos não aparecem na lista enviada ao Orchestrator
- [ ] Ao criar/editar um agente, o Assistant correspondente na OpenAI é criado/atualizado automaticamente
- [ ] Todas as mensagens de uma conversa compartilham o mesmo `openai_thread_id`
- [ ] Handoff entre agentes não reinicia o thread — contexto completo preservado
- [ ] Um sub-agente pode fazer handoff para outro sub-agente ou devolver ao Orchestrator
- [ ] Ao detectar palavras-chave de transbordo, o backend injeta instrução de transbordo no contexto antes de chamar a IA
- [ ] Após `transfer_to_human`, a IA não processa mais mensagens naquela conversa
- [ ] Mensagens enviadas pelo cliente enquanto aguarda atendente recebem resposta automática do sistema (sem IA)
- [ ] Em falha de API OpenAI: 1 retry após 3s → se persistir, transbordo automático com log do erro
- [ ] Falha de autenticação OpenAI (401/403) não faz retry — transbordo imediato
- [ ] O testador (playground) não cria conversa real — usa thread temporária descartada
- [ ] A key OpenAI do tenant tem prioridade sobre a key global do sistema
- [ ] Cada run gera documento em `agent_activity_logs` (tokens, latência, erros)
- [ ] Sub-agentes com histórico não são deletados fisicamente (soft delete)
- [ ] `context_window_messages` do tenant é respeitado na montagem do contexto enviado à IA
- [ ] Variáveis `{{company_name}}`, `{{department_name}}` são substituídas no prompt antes de enviar à OpenAI

---

## 11. Decisões Registradas

| # | Decisão | Registrado em |
|---|---|---|
| P1 | Janela de contexto configurável por tenant (`ai_settings.context_window_messages`, default: 20) | v1.1 |
| P2 | Identidade da IA é decisão do tenant — não há obrigatoriedade técnica | v1.1 |
| P3 | Detecção de frustração 100% via prompt — sem lógica hardcoded no backend | v1.1 |
| P4 | Sub-agentes podem fazer handoff para qualquer outro agente ativo | v1.1 |
| P5 | Falha OpenAI: 1 retry (3s) → log de erro → transbordo automático para humano | v1.1 |
