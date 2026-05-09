# ADR-006-002 — Detecção de frustração 100% via prompt (sem heurística "3+ trocas")

**Status**: Aceito (com emenda PATCH à constituição pendente)
**Data**: 2026-05-08
**Spec**: 006-ai-agents
**Princípio constitucional impactado**: II. AI-First, Human-Assisted

## Contexto

A constituição §II determina:
> _"The system MUST detect handoff triggers: explicit keywords ('atendente', 'humano', 'gerente') AND frustration signals (3+ unresolved exchanges)."_

A Spec 006 §11 P3 (decisão registrada da spec aprovada pelo product owner em 2026-05) **derruba a heurística "3+ unresolved exchanges"** e delega a detecção de frustração 100% ao **prompt do agente**. Mantém apenas o gatilho hardcoded de palavras-chave.

## Motivação para a mudança

1. **Falsos positivos**: a heurística "3+ trocas sem resolução" contava `unresolved` como qualquer turno em que a IA não chamou `transfer_to_human`. Em conversas legítimas — ex.: cliente comparando 3 planos antes de escolher — isso forçaria transbordo prematuro, desperdiçando tempo do atendente humano e quebrando a SLA "AI resolution rate without handoff > 40%" (Constituição §VI).
2. **Definição ambígua**: "unresolved" não tinha critério algorítmico claro. Marcar tudo como unresolved é conservador demais; tentar inferir resolução via NLP no backend duplica responsabilidade já delegada ao LLM.
3. **Capacidade do LLM**: `gpt-4o` é capaz de identificar frustração com **muito mais precisão** que uma heurística de contagem — analisa tom, repetição de pergunta, pedidos explícitos. O prompt do Orchestrator pode incluir regra "Se o cliente expressar frustração, irritação ou insistir 2+ vezes na mesma pergunta, acione transfer_to_human."
4. **Customização por tenant**: tenants podem ter critérios diferentes — clínica de saúde mental quer transbordo rápido; e-commerce de varejo aceita mais paciência. Deixar isso no prompt permite ajuste por tenant sem mudança de código.

## Decisão

A Spec 006 implementa:

1. **Gatilho hardcoded**: apenas palavras-chave em PT-BR ("atendente", "humano", "gerente", "responsável", "quero falar com alguém"). Detectado no backend (`HandoffKeywordDetector`), injeta system message no thread instruindo a IA a chamar `transfer_to_human`. **Não há contagem de turnos.**
2. **Detecção semântica de frustração**: 100% delegada ao prompt do agente. Os templates globais do operador SaaS (Spec 003) DEVEM incluir orientação explícita ao Orchestrator sobre quando acionar `transfer_to_human` — exemplo:
   > "Acione transfer_to_human se o cliente: (a) pedir explicitamente um humano; (b) demonstrar frustração ou insistir mais de uma vez na mesma pergunta sem evolução; (c) trazer assunto fora do seu escopo (reclamação formal, reembolso, jurídico)."
3. **Dead end protection**: o prompt-base obriga o Orchestrator a, em qualquer dúvida sobre como prosseguir, acionar `transfer_to_human` em vez de deixar o cliente sem resposta (FR-017 da Spec 006).

## Justificativa frente à constituição §II

A constituição é violada **apenas no mecanismo** de detecção de frustração; os **objetivos** do princípio são integralmente preservados:
- ✅ Orchestrator avalia sub-agentes ativos antes de responder (não muda).
- ✅ Sub-agentes são tenant-configurados (não muda).
- ✅ Handoff carrega contexto completo (FR-014).
- ✅ Detecção de palavras-chave hardcoded (mantida).
- ✅ Toda resposta tem fallback — sem dead ends (FR-017).
- ✅ Atendentes veem tudo que a IA disse (FR-014).
- ⚠️ **Único item alterado**: heurística "3+ unresolved exchanges" → prompt-driven.

## Emenda à constituição

Será proposto um PR de **PATCH** ao Principle II:

> Texto atual:
> _"The system MUST detect handoff triggers: explicit keywords ('atendente', 'humano', 'gerente') AND frustration signals (3+ unresolved exchanges)."_
>
> Texto proposto:
> _"The system MUST detect handoff triggers: (a) explicit keywords detected in client messages ('atendente', 'humano', 'gerente'), enforced by the backend regardless of agent prompt; (b) semantic frustration signals — delegated to the agent prompt, with mandatory guidance in the global Orchestrator template recommending transfer when the client shows frustration, repeats unanswered questions, or raises out-of-scope topics."_

A emenda mantém o **espírito** do princípio (detecção obrigatória de transbordo) e atualiza o **mecanismo**, refletindo o aprendizado da Spec 006.

## Alternativas consideradas

| Alternativa | Por que rejeitada |
|---|---|
| Manter heurística "3+ trocas" inalterada | Falsos positivos prejudicariam SC-AI-resolution-rate (>40%) |
| Híbrido: heurística + prompt | Comportamento duplicado e difícil de explicar a tenants. Quem ganha em conflito? |
| Heurística mais sofisticada (ex.: detectar negação repetida com NLP no backend) | Reimplementa em código o que o LLM já faz nativamente — viola V (Simplicity) |
| Tornar a heurística configurável por tenant | Adiciona superfície de configuração sem motivo — overengineering |

## Consequências

**Positivas**:
- Maior precisão na detecção de frustração.
- Sem código duplicado (LLM faz o trabalho).
- Tenants conseguem ajustar comportamento sem mudança de código.

**Negativas (mitigadas)**:
- **Risco**: tenant edita o prompt e remove a orientação de transbordo, deixando clientes presos. **Mitigação**: backend mantém a palavra-chave como rede de segurança; ainda assim FR-017 obriga o Orchestrator a acionar `transfer_to_human` em casos não-cobertos. Na prática, é difícil "quebrar" o transbordo via prompt apenas.
- **Risco**: contratos da Spec 006 e seus testes assumem prompt bem-formado. **Mitigação**: smoke tests verificam transbordo em cenário de frustração explícita; documentação aos tenants enfatiza não remover a orientação.

## Validação

- Após implementação, monitorar `agent_activity_logs` por 1 mês:
  - Métrica: % de transbordos via palavra-chave vs. via prompt-decision.
  - Métrica: taxa de "dead end" (turno sem resposta) — deve ser zero.
- Se taxa de dead end > 0, reabrir esta ADR.
