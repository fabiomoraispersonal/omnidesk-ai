# Phase 0 — Research: Departamentos e Atendentes

Decisões técnicas que ancoram o plan.md. Cada item resolve um ponto de incerteza ou risco identificado durante o levantamento da spec.

---

## R1 — Algoritmo de distribuição: round-robin com cursor Redis

**Decision**: Cursor incremental por departamento em Redis: chave `{slug}:rr:{department_id}` armazena um inteiro; cada atribuição faz `INCR` e usa `cursor mod len(eligible)` para escolher o próximo. TTL de 1 hora — após inatividade, recomeça de 0. Comportamento "memoryless" entre reinícios é aceito pela premissa A10.

**Rationale**:

- `INCR` é atômico no Redis — não precisa de lock para o cursor.
- A lista de elegíveis pode mudar a cada chamada (atendentes que viram online/offline); por isso não cacheamos a lista, recalculamos sob demanda e aplicamos `mod`.
- Distribuição justa em rajadas (SC-003: máximo 1 ticket de diferença em 100 tickets) é matematicamente garantida pelo `mod` quando a lista é estável; perturbações reorganizam levemente a sequência sem comprometer a justiça agregada.

**Alternativas avaliadas**:

- **Lista FIFO no Redis** (`LPUSH`/`RPOP`): descartado — não tolera variação dinâmica da lista de elegíveis (precisaria reconstruir a lista a cada mudança).
- **Algoritmo "menos atribuído"** (mínimo de tickets ativos): descartado — exige consulta a Postgres em cada atribuição, fere o orçamento de p95 ≤ 150 ms e adiciona complexidade que V1 não precisa.
- **Persistir cursor em Postgres**: descartado — round-trip e contention desnecessários.

---

## R2 — Lock de atribuição: SET NX EX

**Decision**: Antes de marcar um ticket como atribuído, executar `SET {slug}:ticket_lock:{ticket_id} {attendant_id} NX EX 10`. Se o resultado for `OK`, o caller continua; se `nil`, outro atendente já pegou — abortar e (no caso de "Assumir") devolver mensagem clara; (no caso de round-robin) tentar o próximo elegível.

**Rationale**:

- Pattern canônico do Redis para lock distribuído leve. Não precisa de Redlock dado o volume e a tolerância (uma atribuição perdida cai para o próximo elegível na mesma chamada — sem efeito visível para o usuário).
- TTL de 10 s é suficiente para o handler completar a transação Postgres + emissão WebSocket; protege contra crashes deixando lock órfão.
- O valor armazenado (`{attendant_id}`) permite detectar reentrância (mesmo atendente clicando duas vezes) e diagnosticar conflitos no log.

**Alternativas avaliadas**:

- **Redlock multi-node**: descartado — overkill para um único Redis (o projeto opera com instância única gerenciada).
- **Postgres advisory lock**: descartado — acopla o concurrency control à transação SQL; mais difícil de medir/observar e adiciona round-trip extra para o caso comum (quando o lock falha).
- **Coluna `assignment_attempt_count` com optimistic concurrency**: descartado — dois atendentes verificariam o estado e ambos acreditariam que podem assumir; o lock atômico é mais direto.

---

## R3 — Presença em tempo real: Redis + Hangfire fallback

**Decision**: Status atual em `{slug}:attendant_status:{attendant_id}` (JSON: `{ status, changed_at, changed_by }`). Heartbeat: o CRM faz `PATCH /api/attendants/{id}/heartbeat` a cada 60 s enquanto o atendente estiver no app; o backend renova o TTL da chave (5 min) e atualiza `last_seen`. Se o TTL expirar **e** a chave permanecer inalterada por mais 15/30 min, um job Hangfire recurring (a cada 1 min) varre os atendentes em `online` que não tiveram heartbeat nos últimos 15 min e marca como `away`; depois marca `away` há 30 min como `offline`.

**Rationale**:

- O TTL Redis dá invalidation passiva imediata (status some sozinho se a aba do CRM fecha sem logout); o Hangfire dá garantia ativa para gravar a transição com `changed_by=system` e disparar evento WebSocket.
- A tabela Postgres `attendant_status` é sincronizada a cada mudança para uso em relatórios — leituras de tempo real **sempre** vão ao Redis para evitar P/L em hot path.
- Heartbeat HTTP sobre o intervalo padrão é simples, não exige WebSocket, e o backend usa-o para confirmar atividade real (FR-008).

**Alternativas avaliadas**:

- **WebSocket ping/pong como heartbeat**: descartado — o atendente pode ter o CRM aberto em outra aba sem usar; o ping seguiria mesmo sem interação real do humano. O heartbeat HTTP por interação no CRM (mouse/keyboard ou navegação) é mais fiel ao requisito.
- **Apenas Redis TTL (sem Hangfire)**: descartado — não dispara o evento WebSocket de transição, e o log Mongo não seria gravado.
- **Apenas Hangfire (sem Redis)**: descartado — leitura de presença em hot path (distribuição) bateria no Postgres a cada ticket.

---

## R4 — WebSocket: pub/sub Redis com canais por escopo

**Decision**: Cada conexão WebSocket assina canais Redis específicos:

- `{slug}:ws:tenant` — broadcast geral (tenant_admin/supervisor)
- `{slug}:ws:dept:{department_id}` — atualizações por departamento (atendentes do dept + supervisor)
- `{slug}:ws:attendant:{attendant_id}` — eventos diretos ao atendente (ticket atribuído, transferido para ele)

Eventos publicados com payload JSON contendo `type`, `payload`, `timestamp`. O frontend deserializa e decide UI updates via signals.

**Rationale**:

- Canal por escopo elimina filtragem cliente-side; reduz tráfego e CPU do browser.
- Reusa o `Microsoft.AspNetCore.WebSockets` nativo já habilitado no projeto (sem SignalR — ADR-005).
- Pub/sub Redis permite escalar API horizontalmente — qualquer instância pode publicar; todas as instâncias com clientes conectados recebem.

**Alternativas avaliadas**:

- **Server-Sent Events (SSE)**: descartado — o CRM já assume WebSocket bidirecional para chat em tempo real; misturar transports prejudica a coerência.
- **SignalR**: descartado pela ADR-005.
- **Canal único por tenant com filtragem cliente**: descartado — clientes recebem 100 % dos eventos do tenant, mesmo os irrelevantes; degrada UX com muitas conexões.

---

## R5 — SLA com pausa por horário comercial: cálculo puro em memória

**Decision**: Não há job que pause/retome contadores. O contador é **calculado on-the-fly** quando o card do ticket é renderizado: dado `start_at`, `target_minutes` e o `business_hours` do departamento, computa-se quantos minutos úteis se passaram (somando intervalos dentro do horário comercial entre `start_at` e `now`). O resultado vira percentage. Frontend renderiza badge: amarelo ≥ 80 %, vermelho ≥ 100 %.

**Rationale**:

- Volume baixo (V1: até ~500 tickets ativos por tenant, ~10 SLAs por viewer). Cálculo é O(N_dias_uteis_entre_start_e_now), trivial.
- Evita estado paralelo (timers persistidos) que podem dessincronizar.
- Permite mudança retroativa do horário comercial sem migração — próxima renderização recalcula.

**Alternativas avaliadas**:

- **Job recurring que recalcula e armazena**: descartado — adiciona complexidade e latência à mudança de horário comercial.
- **Computar no banco** (função SQL): descartado — duplica lógica em duas linguagens; difícil de testar sem Postgres.

---

## R6 — Sugestão de resposta com IA: contrato com Spec 002

**Decision**: O `SuggestReplyService` consome o sub-agente vinculado ao departamento via interface pública da Spec 002 (Agentes de IA). Payload enviado à OpenAI:

```text
[system] {sub_agent_prompt do departamento, ou prompt genérico "atendente humano"}
[system] Você está sugerindo uma resposta para um atendente humano enviar manualmente.
         Não inclua despedidas se a conversa não está terminando.
         Não invente dados que não estão no contexto.
[user]   Últimas N mensagens da conversa em ordem cronológica
         (N = MAX_SUGGESTION_CONTEXT_MESSAGES, default 20)
[user]   Sugira uma resposta adequada que o atendente humano possa enviar agora.
```

A resposta da OpenAI é truncada a 1000 caracteres (sanity guard) e devolvida ao atendente sem efeito colateral (não cria mensagem, não notifica cliente). O atendente decide aprovar/editar/descartar.

**Rationale**:

- Reusa toda a infraestrutura da Spec 002 (key, retries, telemetria de tokens, fallback). Esta spec **não** instancia cliente OpenAI próprio.
- N=20 cobre 99 % das necessidades de contexto de uma conversa de WhatsApp/Live Chat (média de 8 mensagens por sessão); configurável via env para tenants com cenários atípicos.
- Sanity cap em 1000 caracteres protege contra prompt injection trazendo respostas absurdamente longas.

**Alternativas avaliadas**:

- **Cliente OpenAI próprio nesta spec**: descartado — duplica concerns (rate limit, retry, key rotation) que a Spec 002 já resolve.
- **Sugerir templates fixos via lookup**: descartado — não atende o requisito de contexto da conversa.
- **Streaming token-by-token ao atendente**: descartado para V1 — adiciona complexidade de UI; a sugestão chegar em ≤ 3 s atende a expectativa.

---

## R7 — Substituição de variáveis em respostas pré-formadas: regex pura

**Decision**: Função pura `VariableSubstitution.Apply(template, context)` que aplica `Regex("\\{\\{(\\w+)\\}\\}")` e substitui cada match pelo valor de `context[group1]`, com fallback explícito quando a chave está ausente: `client_name → "cliente"`, `attendant_name → "atendente"`, `ticket_number → "—"`, `department_name → "atendimento"`. Variáveis desconhecidas são preservadas como literal `{{X}}` e logadas em nível `Warning` (sinal de canned response mal cadastrada).

**Rationale**:

- Sem dependência externa (Liquid, Scriban, Razor) — mantém princípio V (Simplicity).
- Função pura é trivialmente testável; não precisa de mock.
- Fallbacks explícitos atendem FR-034 (sem placeholder literal nem string vazia em variáveis conhecidas).

**Alternativas avaliadas**:

- **Liquid (DotLiquid)**: descartado — pacote novo só para `{{var}}`. Liquid é overkill.
- **Scriban**: idem — abre superfície de execução de código a partir de strings vindas do banco; risco de segurança.
- **String.Format estilo `{0}`**: descartado — perde nomes de variáveis, frágil contra mudanças de ordem.

---

## R8 — Tabela `user_departments` (Spec 004 referenciava, esta spec define)

**Decision**: A tabela física é **`tenant_{slug}.attendant_departments`** com colunas `attendant_id`, `department_id`, `is_primary`. A Spec 004 referenciou `public.user_departments` como dependência futura; esta spec entrega a versão final e correta dentro do tenant schema, com `attendant_id` em vez de `user_id` (atendente é o conceito de domínio; usuário é o conceito de auth).

A `DepartmentScopeFilter` (Spec 004) leu `dept_ids` da claim do JWT, populada pela `ClaimsTransformer` que **agora** consulta `tenant_{slug}.attendant_departments` JOIN `tenant_{slug}.attendants` ON `attendants.user_id = users.id`. A `ClaimsTransformer` da Spec 004 já tem fallback para tabela ausente; aqui essa fallback se torna o caminho feliz.

**Rationale**:

- Atendente é uma entidade de tenant; `user_id` é só o vínculo com auth (Spec 002).
- Manter no schema do tenant respeita o princípio I (multi-tenant isolation) — outros tenants não enxergam essa tabela.
- A coluna `is_primary` (apenas 1 por atendente) ajuda relatórios da Spec 011 sem complicar queries de scoping.

**Alternativas avaliadas**:

- **Manter `public.user_departments`**: descartado — viola o princípio I (dados do tenant em schema público).
- **Reusar `public.users` com array de departments**: descartado — dificulta query, mistura concerns de auth e operação.

---

## R9 — Avatares: reuso direto do MinIO existente

**Decision**: O bucket `tenant-{slug}` (já provisionado pela Spec 003) recebe os avatares dos atendentes em `avatars/attendants/{attendant_id}/{filename}`. Endpoint `POST /api/attendants/{id}/avatar` aceita imagem (multipart/form-data, ≤ 2 MB, JPG/PNG/WebP), redimensiona para 256×256 (System.Drawing.Common no backend), persiste no MinIO e retorna URL assinada de 7 dias.

**Rationale**:

- Sem novo bucket, sem nova convenção de keys.
- Tamanho fixo evita problemas de UI e tráfego.
- URL assinada protege a imagem (não é pública); o CRM passa pelo proxy e renova quando expira.

**Alternativas avaliadas**:

- **Bucket público dedicado**: descartado — quebra o isolamento de tenant.
- **Base64 no Postgres**: descartado — payload de listagem de atendentes ficaria absurdo.
- **CDN externa**: descartado — V1 não tem orçamento para CDN extra; CloudFlare Pages é só para frontend estático.

---

## Resumo das decisões

| ID | Tema | Escolha |
|---|---|---|
| R1 | Algoritmo de distribuição | Cursor Redis com `INCR` + `mod` sobre lista dinâmica |
| R2 | Lock de atribuição | `SET NX EX 10` em `{slug}:ticket_lock:{ticket_id}` |
| R3 | Presença | Redis (status atual + TTL 5 min) + Hangfire recurring 1 min para transições por timeout |
| R4 | WebSocket | Pub/sub Redis com 3 níveis de canal (tenant / dept / attendant) |
| R5 | SLA com horário comercial | Cálculo puro em memória ao renderizar — sem job |
| R6 | Sugestão IA | Reusa Spec 002 (Agentes); prompt do sub-agente do dept + 20 últimas mensagens |
| R7 | Variáveis em canned response | Regex puro `\{\{(\w+)\}\}` + fallback explícito |
| R8 | `attendant_departments` | Tabela em `tenant_{slug}.*` com `attendant_id` (não `user_id`) |
| R9 | Avatares | Bucket `tenant-{slug}` existente, key `avatars/attendants/{id}/...` |

Todas as decisões respeitam a Constituição v1.0.0; nenhum ADR adicional é necessário.
