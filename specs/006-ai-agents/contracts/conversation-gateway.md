# Contract — `IConversationGateway` (interno, ponte para Spec 007)

Interface interna que o `AgentOrchestrator` usa para interagir com a camada de conversa/canal. **Não** é um endpoint HTTP — é um contrato C#.

A Spec 006 entrega uma implementação stub (`ChannelStubGateway`) que persiste em `ai_threads` (transitional) e loga via Serilog. A Spec 007 substitui pela impl real (Live Chat) e a Spec 008 (WhatsApp) usa adapter dedicado.

---

## C# Interface

```csharp
namespace omniDesk.Api.Features.AgentRuntime;

public interface IConversationGateway
{
    /// <summary>
    /// Resolve ou cria thread (AiThread) para uma conversa externa.
    /// Idempotente — chamadas repetidas com o mesmo external_ref retornam o mesmo AiThread.
    /// </summary>
    Task<AiThreadDto> GetOrCreateThreadAsync(
        string tenantSlug,
        string externalConversationRef,
        CancellationToken ct);

    /// <summary>
    /// Carrega as N últimas mensagens da conversa (em ordem cronológica) — usado pela primeira chamada à OpenAI.
    /// Apenas o adapter de canal (Live Chat / WhatsApp) tem acesso ao histórico real; o stub retorna lista vazia.
    /// </summary>
    Task<IReadOnlyList<ConversationMessage>> GetRecentMessagesAsync(
        Guid threadId,
        int limit,
        CancellationToken ct);

    /// <summary>
    /// Enfileira mensagem de saída (assistant ou system) para entrega ao cliente via canal.
    /// </summary>
    Task EnqueueOutgoingAsync(
        Guid threadId,
        OutgoingMessage message,
        CancellationToken ct);

    /// <summary>
    /// Marca a thread como sob controle humano. Próximas mensagens recebem auto-reply do sistema.
    /// </summary>
    Task MarkHandedOffAsync(
        Guid threadId,
        CancellationToken ct);

    /// <summary>
    /// Atualiza o agente atualmente responsável pela thread.
    /// </summary>
    Task SetCurrentAgentAsync(
        Guid threadId,
        Guid? agentId,
        CancellationToken ct);

    /// <summary>
    /// Indica se a thread já está sob controle humano (handed_off_to_human_at != null).
    /// </summary>
    Task<bool> IsHandedOffAsync(Guid threadId, CancellationToken ct);
}

public record AiThreadDto(
    Guid Id,
    string ExternalConversationRef,
    string OpenAiThreadId,
    Guid? CurrentAgentId,
    DateTimeOffset? HandedOffToHumanAt);

public record ConversationMessage(
    string Role,           // "user" | "assistant" | "system"
    string Content,
    DateTimeOffset SentAt);

public record OutgoingMessage(
    string Content,
    string Source,         // "agent" | "system"
    Guid? OriginatedByAgentId);
```

---

## Implementação Spec 006 (`ChannelStubGateway`)

- `GetOrCreateThreadAsync`: cria row em `ai_threads` (Postgres) + thread na OpenAI (`threads.create`). Idempotente via `ux_ai_threads_external_ref`.
- `GetRecentMessagesAsync`: retorna lista vazia (a Spec 006 não tem onde buscar histórico). O `ContextBuilder` lida com lista vazia gerando contexto sintético "[Início da conversa]".
- `EnqueueOutgoingAsync`: enfileira em Hangfire `{slug}:outgoing_messages`. O `OutgoingMessageWorker` na Spec 006 apenas loga via Serilog (canais reais entram nas Specs 007/008).
- `MarkHandedOffAsync`: UPDATE `ai_threads SET handed_off_to_human_at = now()` + publica evento Redis `{slug}:ws:thread:{id}` payload `{type: "human_handoff"}` (consumido pelas Specs 007 quando WS estiver vivo).
- `SetCurrentAgentAsync`: UPDATE `ai_threads.current_agent_id`.
- `IsHandedOffAsync`: SELECT — usado pelo `IncomingMessageWorker` antes de qualquer chamada à IA.

---

## Substituição na Spec 007

Quando a Spec 007 for implementada:
1. A entidade `Conversation` substitui `AiThread`. `IConversationGateway` ganha implementação real (`ConversationGateway`) que:
   - Persiste `Conversation`, `Message`.
   - Carrega histórico real em `GetRecentMessagesAsync`.
   - Em `EnqueueOutgoingAsync` aciona o adapter WS / WhatsApp adequado.
2. Migration move dados de `ai_threads` para `conversations` (mantém `openai_thread_id`, `current_agent_id`, `handed_off_to_human_at`) e DROP da tabela `ai_threads`.
3. `ChannelStubGateway` é removido.

---

## Testes de contrato

- `ChannelStubGatewayTests.cs` (Testcontainers Postgres + Redis):
  - `GetOrCreateThreadAsync` idempotente.
  - `IsHandedOffAsync` reflete `MarkHandedOffAsync`.
  - `EnqueueOutgoingAsync` chega em Hangfire (verificado via job state).
- `IConversationGatewayBehaviorTests.cs` — testes que exercitam o contrato (não a impl) e devem **passar igualmente** quando a Spec 007 substituir a impl.
