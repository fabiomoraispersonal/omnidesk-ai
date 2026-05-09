# Contract — `LiveChatConversationGateway` (impl real)

Substitui `ChannelStubGateway` da Spec 006. Implementa `IConversationGateway` (`Features/AgentRuntime/IConversationGateway.cs`) operando sobre `tenant_{slug}.conversations` e `tenant_{slug}.messages` em vez da tabela transitória `ai_threads`.

**Namespace**: `omniDesk.Api.Features.LiveChat.Adapters`
**Registrado em DI**: substitui o registro `services.AddScoped<IConversationGateway, ChannelStubGateway>()` por `services.AddScoped<IConversationGateway, LiveChatConversationGateway>()`. (Modificação do `Program.cs` no momento desta spec.)

---

## Interface (referência da Spec 006)

```csharp
public interface IConversationGateway
{
    Task<AiThreadDto> GetOrCreateThreadAsync(string tenantSlug, string externalConversationRef, CancellationToken ct);
    Task<IReadOnlyList<ConversationMessage>> GetRecentMessagesAsync(Guid threadId, int limit, CancellationToken ct);
    Task EnqueueOutgoingAsync(Guid threadId, OutgoingMessage message, CancellationToken ct);
    Task MarkHandedOffAsync(Guid threadId, CancellationToken ct);
    Task SetCurrentAgentAsync(Guid threadId, Guid? agentId, CancellationToken ct);
    Task<bool> IsHandedOffAsync(Guid threadId, CancellationToken ct);
}
```

---

## Mapeamento conceitual

| Conceito Spec 006 (transitional) | Conceito Spec 007 (real) |
|---|---|
| `AiThread.Id` | `Conversation.Id` |
| `AiThread.OpenAiThreadId` | `Conversation.OpenAiThreadId` |
| `AiThread.CurrentAgentId` | `Conversation.AgentId` |
| `AiThread.HandedOffToHumanAt` | `Conversation.AttendantId IS NOT NULL` (presença de atendente) |
| `external_conversation_ref` | `Conversation.Id` (string-ificada) |

---

## Implementação por método

### `GetOrCreateThreadAsync(tenantSlug, externalRef, ct)`

```csharp
public async Task<AiThreadDto> GetOrCreateThreadAsync(string tenantSlug, string externalRef, CancellationToken ct)
{
    // externalRef é a Conversation.Id em string. Para Live Chat, criamos a conversa no
    // POST /api/public/widget/conversations — então quando a IA pede o thread, ela já existe.
    var convId = Guid.Parse(externalRef);
    var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == convId, ct);
    if (conv is null)
        throw new InvalidOperationException($"Conversation {convId} not found.");

    // Idempotente: se já tem thread OpenAI, reusa; senão deixa null e a Spec 006 cria via Assistants API.
    return new AiThreadDto(
        Id: conv.Id,
        TenantSlug: tenantSlug,
        OpenAiThreadId: conv.OpenAiThreadId,
        CurrentAgentId: conv.AgentId,
        HandedOffToHumanAt: conv.AttendantId.HasValue ? conv.UpdatedAt : null
    );
}
```

### `GetRecentMessagesAsync(threadId, limit, ct)`

Retorna as N últimas mensagens **da conversa atual** (em ordem cronológica ascendente). Filtra `system_event` (FR-045). Inclui mensagens do visitante e da IA.

```csharp
public async Task<IReadOnlyList<ConversationMessage>> GetRecentMessagesAsync(Guid convId, int limit, CancellationToken ct)
{
    return await _db.Messages
        .Where(m => m.ConversationId == convId
                 && m.ContentType != MessageContentType.SystemEvent)
        .OrderByDescending(m => m.CreatedAt)
        .Take(limit)
        .OrderBy(m => m.CreatedAt)
        .Select(m => new ConversationMessage(
            Role: m.SenderType switch {
                MessageSenderType.Visitor   => "user",
                MessageSenderType.AiAgent   => "assistant",
                MessageSenderType.Attendant => "assistant",   // atendente entra como assistant para a IA
                MessageSenderType.System    => "system",
                _ => "user"
            },
            Content: m.Content ?? "",
            CreatedAt: m.CreatedAt))
        .ToListAsync(ct);
}
```

**Caso "reabertura"** (Spec 007 FR-017): quando uma conversa nova é criada via `POST /api/public/widget/conversations` e existe uma `resolved` anterior do mesmo `visitor_id`, o orchestrator pede `GetRecentMessagesAsync(newConvId, limit=20)` e recebe vazio (conversa nova). A injeção do contexto pré-conversa fica no caminho da criação:

```
StartConversationCommand:
  1. Cria Conversation(status=open).
  2. Se há Conversation anterior do mesmo visitor com status=resolved:
       Para cada uma das últimas WIDGET_RESUMED_CONTEXT_MESSAGE_LIMIT mensagens não-system_event,
       INSERT em messages com sender_type=system, content_type=system_event,
         content=`[contexto anterior] {role}: {text}`
       (Essas mensagens aparecem no histórico mas são excluídas pelo filtro system_event do GetRecentMessages...)
     ❌ Não — system_event seria filtrado, perdendo o contexto.
     ✅ Alternativa: insira como sender_type=system, content_type=text, com prefix `[contexto]`.
        Marcar com flag em metadata: { is_resumed_context: true } para o widget ocultar.
```

> **Decisão de design**: o contexto retomado é injetado **apenas no thread da OpenAI** (via `system message` na criação do Run) — NÃO em `messages` da conversa nova (evita poluir histórico do usuário). A Spec 006 já tem hook `BeforeFirstRun` no `AgentOrchestrator` para isso. Esta spec passa o contexto via novo método `IConversationGateway.GetResumedContextAsync(visitorId, limit, ct)` (extensão da interface — coberta no plan via PR companion).

### `EnqueueOutgoingAsync(threadId, message, ct)`

```csharp
public async Task EnqueueOutgoingAsync(Guid convId, OutgoingMessage msg, CancellationToken ct)
{
    // 1. Persiste em messages.
    var entity = new Message {
        Id = Guid.NewGuid(),
        ConversationId = convId,
        SenderType = msg.SenderType,            // ai_agent ou system
        SenderId   = msg.AgentId,
        ContentType = MessageContentType.Text,
        Content = msg.Content,
        IsRead = false,
        CreatedAt = DateTime.UtcNow
    };
    _db.Messages.Add(entity);
    await _db.SaveChangesAsync(ct);
    // 2. Publica no canal Redis para o widget.
    await _redis.PublishAsync(
        channel: $"{_tenant.Slug}:conv:{convId}",
        message: JsonSerializer.Serialize(new {
            type = "message.new",
            payload = new {
                message_id = entity.Id,
                sender_type = entity.SenderType.ToSnake(),
                sender_id = entity.SenderId,
                content_type = "text",
                content = entity.Content,
                created_at = entity.CreatedAt
            }
        }));
    // 3. Também publica no canal CRM caso tenha atendente atribuído ou departamento ativo.
    var conv = await _db.Conversations.FindAsync(new object?[] { convId }, ct);
    if (conv?.AttendantId is not null)
        await _redis.PublishAsync($"{_tenant.Slug}:crm:user:{conv.AttendantId}",
            JsonSerializer.Serialize(new { type = "chat.message_received", /* ... */ }));
}
```

### `MarkHandedOffAsync(threadId, ct)`

Não faz mais nada além de garantir que existe ticket (criação fica em `ITicketCreationGateway` da Spec 008). Esta spec apenas garante que a conversa fique fora do pipeline de IA até alguém assumir:

```csharp
public async Task MarkHandedOffAsync(Guid convId, CancellationToken ct)
{
    var conv = await _db.Conversations.FindAsync(new object?[] { convId }, ct)
        ?? throw new InvalidOperationException();
    conv.AgentId = null;                        // libera agente
    // attendant_id é setado pelo Atendente quando assume — daqui só sinalizamos a transição
    conv.UpdatedAt = DateTime.UtcNow;
    // INSERT message system_event "handoff_to_human"
    _db.Messages.Add(new Message {
        Id = Guid.NewGuid(),
        ConversationId = convId,
        SenderType = MessageSenderType.System,
        ContentType = MessageContentType.SystemEvent,
        Content = "handoff_to_human",
        CreatedAt = DateTime.UtcNow
    });
    await _db.SaveChangesAsync(ct);
}
```

### `SetCurrentAgentAsync(convId, agentId, ct)`

```csharp
public async Task SetCurrentAgentAsync(Guid convId, Guid? agentId, CancellationToken ct)
{
    await _db.Conversations
        .Where(c => c.Id == convId)
        .ExecuteUpdateAsync(s => s.SetProperty(c => c.AgentId, agentId)
                                  .SetProperty(c => c.UpdatedAt, DateTime.UtcNow), ct);
}
```

### `IsHandedOffAsync(convId, ct)`

```csharp
public async Task<bool> IsHandedOffAsync(Guid convId, CancellationToken ct)
{
    return await _db.Conversations
        .Where(c => c.Id == convId)
        .Select(c => c.AttendantId)
        .FirstOrDefaultAsync(ct) is not null;
}
```

---

## Testes (xUnit + Testcontainers)

`tests/omniDesk.Api.Tests/Features/LiveChat/Adapters/LiveChatConversationGatewayTests.cs`:

| Teste | Cobre |
|---|---|
| `GetOrCreateThread_existing_returns_dto` | Idempotência |
| `GetOrCreateThread_missing_throws` | Conversa inexistente |
| `GetRecentMessages_filters_system_events` | FR-045 |
| `GetRecentMessages_orders_chronologically` | Ordem ascendente |
| `EnqueueOutgoing_persists_and_publishes_redis` | Pipeline completo (verificar via subscriber de teste) |
| `MarkHandedOff_clears_agent_and_emits_event` | Transição de estado |
| `SetCurrentAgent_updates_db` | UPDATE atômico |
| `IsHandedOff_true_when_attendant_set` | Lógica de detecção |
| `Replaces_ChannelStubGateway` | Test de fiação no `Program.cs` (registro em DI) |

---

## Compatibilidade com testes da Spec 006

A Spec 006 tem testes que usam o `ChannelStubGateway`. Após esta substituição, esses testes devem **continuar passando** porque:

- `ChannelStubGateway` permanece no código como fallback / fixture de testes que não envolvem WebSocket.
- Testes de unidade do `AgentOrchestrator` injetam mock de `IConversationGateway` — não dependem da impl concreta.
- Testes de integração end-to-end da Spec 006 usavam `ai_threads`; agora rodam sobre `conversations` + `messages` — adicionar setup helper que cria uma conversa antes de invocar o orchestrator (`WidgetTestHelpers.SeedOpenConversationAsync`).
