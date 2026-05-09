# Contract — Tool Calls dos Agentes (Assistants v2 Functions)

Tool calls são **functions** registradas no Assistant via `assistants.create({tools: [{type: 'function', function: {…}}]})`. O backend interpreta `runs.requires_action` e despacha via `ToolCallDispatcher`.

Constants: `Domain/AiAgents/ToolNames.cs`:

```csharp
public static class ToolNames
{
    public const string HandoffToAgent      = "handoff_to_agent";
    public const string TransferToHuman     = "transfer_to_human";
    public const string CheckAvailability   = "check_availability";
    public const string CreateAppointment   = "create_appointment";
}
```

---

## 1. `handoff_to_agent`

**Quando o agente chama**: identifica que outro agente é mais adequado.

**Schema da function**:

```json
{
  "name": "handoff_to_agent",
  "description": "Transfere a conversa para outro agente (Orchestrator ou Sub-agente) quando identificada mudança de contexto ou intenção.",
  "parameters": {
    "type": "object",
    "properties": {
      "agent_id": {
        "type": "string",
        "description": "UUID do agente de destino (deve estar ativo no tenant). Use 'orchestrator' como atalho para devolver ao Orchestrator."
      },
      "reason": {
        "type": "string",
        "description": "Motivo da transferência. Interno, não enviado ao cliente."
      }
    },
    "required": ["agent_id", "reason"]
  }
}
```

**Resposta do backend (`submit_tool_outputs`)**:

```json
{ "success": true, "next_agent_name": "Agente Suporte" }
```

ou erro:

```json
{ "success": false, "error": "AGENT_NOT_ACTIVE", "message": "Sub-agente não está ativo." }
```

**Side effects**:
- Atualiza `ai_threads.current_agent_id`.
- Grava `agent_activity_logs` com `action: handoff_to_agent` + `handoff_target_agent_id`.
- Após `submit_tool_outputs`, abre **novo run** no mesmo thread com o Assistant do agente destino (R4).

**Validação**:
- Se `agent_id == "orchestrator"` → resolve para o orchestrator do tenant.
- Se sub-agente alvo está `is_active=false` → retorna erro estruturado; o agente origem decide como continuar.
- Loop infinito: detecta se houve handoff para o mesmo agente nas últimas 3 chamadas dessa conversa → retorna erro `HANDOFF_LOOP_DETECTED`. Constraint anti-pong (não no Spec original, justificado em research como segurança).

---

## 2. `transfer_to_human`

**Quando o agente chama**: identifica necessidade de intervenção humana — palavra-chave do cliente, sensibilidade, falha técnica, etc.

**Schema**:

```json
{
  "name": "transfer_to_human",
  "description": "Transfere a conversa para um atendente humano e abre um ticket no departamento correto. Após esta tool, a IA não processa mais mensagens nesta conversa.",
  "parameters": {
    "type": "object",
    "properties": {
      "department_id": {
        "type": "string",
        "description": "UUID do departamento de destino. Para o Orchestrator, este parâmetro pode ser omitido — o sistema usa o departamento padrão do tenant."
      },
      "reason": {
        "type": "string",
        "description": "Motivo do transbordo. Registrado no ticket."
      }
    },
    "required": ["reason"]
  }
}
```

**Resposta do backend (`submit_tool_outputs`)**:

```json
{
  "success": true,
  "ticket_id": "uuid",
  "department_name": "Comercial",
  "instruction_for_agent": "Envie ao cliente: 'Vou transferir você para nossa equipe de Comercial. Aguarde um momento.'"
}
```

> O `instruction_for_agent` é **dica** — o LLM tipicamente já produz a mensagem; este campo serve como guard-rail/fallback. O backend **não** garante que a mensagem do agente chegue ao cliente: a próxima `assistant message` do run atual é capturada e enviada normalmente via outgoing.

**Side effects**:
- Resolve `department_id`:
  - Se preenchido na tool call → usa.
  - Se ausente E agente é orchestrator → usa `tenants.default_department_id`.
  - Se ausente E agente é sub_agent → usa `agent.department_id`.
  - Se nenhum disponível → fallback documentado em `cross-spec-pendencies.md` item 005-A.
- Chama `ITicketCreationGateway.CreateTicketFromAiHandoff(...)` com:
  - tenant, conversation_id, department_id, motivo, snapshot do histórico do thread.
- Atualiza `ai_threads.handed_off_to_human_at = now()`, `current_agent_id = null`.
- Grava `agent_activity_logs` com `action: transfer_to_human` + `handoff_target_department_id`.
- Após este turno, qualquer mensagem subsequente da mesma conversa recebe auto-reply do sistema (Spec FR-015).

**Mensagem do agente ao cliente**:
- Implementada **via prompt** do Orchestrator/sub-agente (FR-033).
- Backend valida que a próxima assistant message inclui menção a "transferir" (heurística simples) — apenas para telemetria; não bloqueia.

---

## 3. `check_availability` — STUB (Spec de Agenda)

**Schema**:

```json
{
  "name": "check_availability",
  "description": "Consulta horários disponíveis na agenda do tenant.",
  "parameters": {
    "type": "object",
    "properties": {
      "professional_id": { "type": "string" },
      "date": { "type": "string", "description": "YYYY-MM-DD" }
    },
    "required": ["professional_id", "date"]
  }
}
```

**Resposta na Spec 006**:

```json
{
  "success": false,
  "error": "TOOL_NOT_AVAILABLE",
  "message": "Funcionalidade de agenda ainda não disponível. Use transfer_to_human para encaminhar agendamentos a um atendente humano."
}
```

> O agente recebe o erro estruturado e — se bem prompted — aciona `transfer_to_human`. Esta é a forma de **degradar graciosamente** até a Spec de Agenda chegar (V2).

---

## 4. `create_appointment` — STUB (Spec de Agenda)

Mesmo padrão de `check_availability`: schema declarado, retorno `TOOL_NOT_AVAILABLE` no V1.

---

## 5. Tool calls inválidas

Se o LLM gera tool call com:
- nome desconhecido → retorna erro estruturado `UNKNOWN_TOOL`.
- parâmetros faltantes/inválidos → retorna erro estruturado `INVALID_TOOL_PARAMS` com lista de campos faltantes.
- json malformado → retorna `MALFORMED_TOOL_CALL`.

Em todos os casos, o erro é submetido via `submit_tool_outputs` para o LLM se auto-corrigir dentro do mesmo run. Se reincidir 3x no mesmo run → loga em `agent_activity_logs` (`action: api_error`, error.type: `tool_loop`) e aciona transbordo.

---

## 6. Registro de tools no Assistant

Ao criar Assistant (`AssistantsApi.CreateAsync`), o backend registra **todas** as 4 tools — independente do tipo do agente. Justificativa: simplifica gerenciamento; o prompt determina quais tools cada agente realmente usa. Tools indisponíveis na Spec 006 (`check_availability`, `create_appointment`) retornam `TOOL_NOT_AVAILABLE` em runtime.

---

## 7. Testes de contrato

- `ToolCallDispatcherTests.cs`:
  - handoff_to_agent: sucesso, agente inativo, agente inexistente, loop detectado.
  - transfer_to_human: depto explícito, fallback para `default_department_id`, fallback para `agent.department_id`, sem nenhum disponível (erro de configuração).
  - check_availability/create_appointment: sempre retorna `TOOL_NOT_AVAILABLE`.
  - Tool call malformado: retorna erro estruturado, não derruba o run.
