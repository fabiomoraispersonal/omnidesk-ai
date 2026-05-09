# ADR-006-001 — Mock de OpenAI nos testes de integração

**Status**: Aceito
**Data**: 2026-05-08
**Spec**: 006-ai-agents
**Princípio constitucional impactado**: VII. Test Discipline

## Contexto

A constituição §VII determina que testes de integração usem **Testcontainers com instâncias reais** dos bancos. Mockar o banco é vetado — incidente prévio confirmou que mocks mascaram falhas de migração.

Para a integração com OpenAI, a situação é diferente:
- **Custo**: cada `runs.create` em Assistants v2 com `gpt-4o` custa entre $0.005 e $0.05 por run; um suite com ~200 testes custaria ~$4 por execução de CI.
- **Flakiness**: Assistants v2 retorna respostas não-determinísticas; testar "agente respondeu sobre planos" exige asserts frouxos que não detectam regressões reais.
- **Rate limits**: a quota da chave de testes seria saturada por 1 PR ativo.
- **Ausência de sandbox oficial**: a OpenAI não fornece endpoint de replay/sandbox para Assistants v2 (existe para Chat Completions clássico via `mode: replay`, mas não cobre threads/runs/tool calls).

## Decisão

1. **Testes de integração** (Testcontainers) usam `MockHttpMessageHandler` injetado no `HttpClient` do `OpenAI.Assistants.AssistantClient`. Cada cenário define respostas canned que correspondem ao contrato esperado da Spec 006:
   - `assistants.create` retorna `{id: "asst_test_<n>", …}`.
   - `threads.create` retorna `{id: "thread_test_<n>"}`.
   - `runs.create` retorna `{id: "run_test", status: "queued"}`.
   - `runs.retrieve` retorna progressão `queued → in_progress → completed | requires_action | failed` conforme cenário.
   - `runs.submit_tool_outputs` aceita os outputs.
   - `threads.messages.list` retorna a assistant message canned.
2. **Smoke tests live** vivem em `tests/Smoke/OpenAiLiveSmoke.cs` com `[Trait("openai-live", "true")]` e rodam:
   - **Não** no CI principal (`dotnet test --filter "openai-live!=true"`).
   - **Sim** em pipeline noturno opcional ou disparados manualmente antes de merge para `main`.
3. Os contratos testados pelo mock são **derivados da documentação oficial da OpenAI Assistants v2** (commit-tagged em `docs/specs/06-ai-agents.spec.md` ou similar). Mudanças na API da OpenAI são detectadas pelo smoke noturno.

## Justificativa

- Princípio VII protege contra mocks que **mascaram** comportamento real do sistema sob teste. O sistema sob teste aqui é **o orquestrador (Spec 006)**, não a OpenAI. Mockar o transport HTTP da OpenAI testa que **nosso código** envia request certo e reage certo aos status retornados — exatamente o que queremos.
- O smoke live garante que o contrato mock não diverge do real.
- Custo e velocidade do CI permanecem aceitáveis (constituição V — Simplicity).

## Alternativas consideradas

| Alternativa | Por que rejeitada |
|---|---|
| Rodar tudo contra OpenAI live | Custo $4+/PR, flakiness em CI, rate limit |
| Skip de testes de integração | Quebra cobertura de orquestrador (workers + tool dispatch) |
| Sandbox do OpenAI (Chat Completions replay) | Não cobre Assistants v2 — não aplicável |
| Provider third-party (Helicone/Langfuse replay) | Adiciona dependência externa não-justificada (constituição V) |
| Implementação caseira de "fake OpenAI server" | Equivalente em complexidade ao MockHttpMessageHandler — mas mais código nosso para manter |

## Consequências

**Positivas**:
- CI rápido e barato.
- Testes determinísticos.
- Cenários de erro (5xx, 401, 429, timeout) facilmente reproduzíveis.

**Negativas (mitigadas)**:
- Risco de drift contrato mock vs real → mitigado pelo smoke noturno.
- Mock pode passar enquanto produção falha → constituições §VI exige Mongo activity log; falhas reais aparecem lá rapidamente.

## Validação

- Rodar smoke live mensalmente (mínimo).
- Adicionar nota no PR template: "se você tocou em `Infrastructure/OpenAi/`, rode o smoke local antes de pedir review".
