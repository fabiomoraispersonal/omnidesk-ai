# Research — Spec 007 Live Chat (Widget)

Decisões técnicas tomadas antes de Phase 1. Cada item: **Decision** (o que ficou), **Rationale** (por quê), **Alternatives considered** (o que rejeitamos).

---

## R1 — Tecnologia do widget bundle

**Decision**: Vanilla TypeScript + Web Components nativos (Custom Elements + Shadow DOM `closed`) bundlados via esbuild ESM minificado. Zero runtime dependency.

**Rationale**:
- Princípio V (Simplicity) — sem framework runtime adicional.
- Bundle alvo ≤ 30 KB gzipped — Lit (~5 KB) ou Preact (~4 KB) cabem mas o ganho de DX é marginal para uma UI de 6 telas (launcher, painel, lista mensagens, input, pré-chat, LGPD).
- Web Components encapsulam estilo via Shadow DOM, evitando colisão com CSS do site host.
- ESM moderno: drop suporte a IE11 (quem instala widget moderno em CRM usa browser 2020+).

**Alternatives considered**:
- **Angular Elements**: ~140 KB runtime mesmo com tree-shaking — inviável para widget injetado em sites de terceiros.
- **Lit**: ~5 KB e DX excelente para Web Components, mas adiciona dependência. Caso a complexidade da UI cresça (V2 com mais telas), reavaliar — preço seria razoável.
- **Preact + signals**: 4 KB, signals confortável, mas adiciona React-like JSX e alguns risks de SSR (não necessário aqui).
- **Svelte compilado**: bundle ~3 KB sem runtime, ótimo para widgets. Rejeitado por trazer outro toolchain (svelte-compiler) e divergir do TS puro do resto do projeto.

---

## R2 — Onde fica `widget_token`

**Decision**: Coluna `widget_token UUID NOT NULL UNIQUE` em `public.tenants`, populada no provisionamento via `gen_random_uuid()`. Imutável.

**Rationale**:
- Requisição pública chega sem subdomínio (widget injetado em `clinica.com.br`, não em `clinica.omnicare.ia.br`). Lookup `token → tenant_slug` é O(1) com índice único em `public.tenants.widget_token`.
- 1:1 com tenant — zero motivo para tabela separada.
- `public` é exatamente o lugar correto: a Constituição §I reserva `public` para "system-level tables" (tenants, tenant_configs); aqui o token é metadado do próprio tenant.

**Alternatives considered**:
- **Em `tenant_{slug}.widget_config.widget_token`**: requer scan de N schemas para lookup — inviável.
- **Tabela `public.widget_tokens(token PK, tenant_id FK)`**: adiciona join sem ganho.
- **JWT em vez de UUID**: complexidade não justifica — o token é público, não secreto, não expira.

---

## R3 — Autenticação do widget (REST e WebSocket)

**Decision**:
- REST: header `X-Widget-Token: <uuid>` ou query `?token=<uuid>` (fallback). Custom auth handler `WidgetTokenAuthHandler` resolve o tenant e popula `ITenantContext`.
- WebSocket: query `?token=<uuid>&conversation_id=<uuid>` no handshake HTTP. Validação no upgrade.
- Origem: header `Origin` validado contra `widget_config.allowed_domains` em **ambos** REST e WS handshake. Lista vazia = sem restrição.
- Rate limit: 30 mensagens/min por `anonymous_id` (Redis `INCR` + TTL 60s).

**Rationale**:
- Browser WebSocket não permite headers customizados (só cookies e protocolos), então query string é o único caminho. Token público não-secreto torna isso aceitável.
- `Origin` no handshake é a única defesa contra uso do token em domínios não autorizados (cookies httpOnly não se aplicam — origem cruzada).
- Rate limit por `anonymous_id` é defesa primária contra abuso (token vazado): mesmo um invasor com o token e Origin spoofado bate no teto rapidamente.

**Alternatives considered**:
- **HMAC do token**: complica deploy (cliente precisa de secret) sem benefício concreto sobre o rate limit + Origin.
- **Cloudflare Turnstile no widget**: viola UX (captcha no chat); o token público + allowed_domains + rate limit já cobrem o vetor de abuso de origem; a Constituição §IV exige Turnstile em "public-facing forms (login, password recovery)" — chat não é form.

---

## R4 — Substituição da tabela transitória `ai_threads` (Spec 006)

**Decision**: Esta spec cria `tenant_{slug}.conversations` e `tenant_{slug}.messages` (canal-agnósticas via `channel`) e migra a referência do Orchestrator de `ai_threads` para `conversations`. Mapeamento:

| Coluna `ai_threads` (transitional) | Equivalente em `conversations` (real) |
|---|---|
| `id` | `id` |
| `tenant_id` | implícito pelo schema |
| `openai_thread_id` | `openai_thread_id` (NULL para conversas humano-only futuras) |
| `current_agent_id` | `agent_id` |
| `handed_off_to_human_at` | `attendant_id IS NOT NULL` ou `ticket_id IS NOT NULL` |

A migration SQL desta spec **não dropa `ai_threads`** — apenas adiciona as novas tabelas. A view/copy de dados (caso já tenha rodado em ambiente real) é tratada por uma migration de data-fixup específica em produção; em dev/test o `ai_threads` permanece como tabela órfã ignorada.

**Rationale**:
- Drop em mesma migration aumenta risco; deixar duas tabelas em paralelo permite rollback fácil em V1.
- A Spec 006 marcou `ai_threads` como **transitional**; este é exatamente o ponto de transição.
- `IConversationGateway` (interface da Spec 006) absorve a mudança: implementação `LiveChatConversationGateway` opera sobre `conversations`/`messages` em vez de `ai_threads`.

**Alternatives considered**:
- **Renomear `ai_threads` para `conversations`**: rejeitado — perde o campo `channel` e força um ALTER agressivo. Migration SQL com ADD novas tabelas é mais segura.
- **Manter `ai_threads` como cache do `openai_thread_id`**: rejeitado por duplicar dado; `openai_thread_id` cabe em `conversations`.

---

## R5 — Validação de MIME real (anexos)

**Decision**: Backend valida MIME via magic bytes (primeiros 12 bytes) usando uma allowlist hardcoded em `MimeTypeDetector.cs`. Sem dependência externa em V1 (lib `MimeDetective` pode entrar em V2 se a allowlist cresce).

Allowlist V1:

| MIME real | Magic bytes (hex) | Extensão exposta |
|---|---|---|
| `image/jpeg` | `FFD8FF` | `.jpg`, `.jpeg` |
| `image/png` | `89504E47 0D0A1A0A` | `.png` |
| `image/gif` | `474946383761` ou `474946383961` | `.gif` |
| `image/webp` | `52494646 ???? 57454250` | `.webp` |
| `application/pdf` | `25504446 2D` | `.pdf` |
| `application/vnd.openxmlformats-officedocument.wordprocessingml.document` (DOCX) | `504B0304` (ZIP) + verificar `[Content_Types].xml` com `wordprocessingml` | `.docx` |
| `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` (XLSX) | `504B0304` + `[Content_Types].xml` com `spreadsheetml` | `.xlsx` |

DOCX/XLSX exigem inspeção do entry `[Content_Types].xml` do ZIP — implementação via `System.IO.Compression.ZipArchive` (zero dep nova).

**Rationale**:
- Princípio V — zero pacote NuGet novo.
- Constituição IV exige rejeitar arquivos cujo Content-Type não bate com o real.
- Allowlist pequena e estável — ZIP+manifesto cobre Office Open XML.

**Alternatives considered**:
- **`MimeDetective` (NuGet)**: 1 MB, allowlist enorme — overkill para 7 tipos.
- **Confiar no `Content-Type` enviado pelo browser**: viola Constituição IV; magic bytes é o controle correto.

---

## R6 — Reconexão WebSocket no widget

**Decision**: Backoff exponencial com jitter, em milissegundos: `1000, 2000, 4000, 8000, 16000, 30000` (cap 30s). Após reconectar, widget envia `messages.replay since=<last_message_id>` e backend retorna mensagens criadas após esse ID. Mensagens digitadas durante a queda ficam em fila local (em memória do widget, não persistida) e são enviadas ao reconectar.

**Rationale**:
- Padrão de mercado para chat widgets (Intercom, Drift, Crisp seguem similares).
- Replay por `last_message_id` é mais robusto que timestamp (sem ambiguidade de relógio).
- Fila in-memory (não localStorage) evita complicar UX se o usuário fechar a aba durante a queda.

**Alternatives considered**:
- **Reconnect imediato sem backoff**: causa thundering herd no servidor caso a queda seja do backend.
- **Polling REST de fallback**: complexidade dupla (WS + polling) sem ganho; SC-007 já comporta o backoff.
- **Persistir fila em localStorage**: aumenta superfície de PII no cliente; mensagens que o usuário não enviou não devem ficar persistidas.

---

## R7 — Detecção de "atendente focado vs em background" (browser notification)

**Decision**: CRM usa `document.visibilityState === 'hidden'` + foco da `conversation` selecionada. Se a aba está visible E a conversation focada é a que recebeu mensagem → apenas atualiza badge; senão → emite `Notification`. Permissão solicitada na primeira sessão (`Notification.requestPermission()`) e gerenciável em **CRM → Configurações → Notificações**.

**Rationale**:
- API nativa, zero dep.
- `visibilityState` cobre minimização e troca de aba.
- Permissão em sessão (não imediata) reduz negativas — usuário entende o porquê quando vê a primeira conversa.

**Alternatives considered**:
- **Service worker push**: overkill — atendente está logado; não precisa receber push offline.
- **Toast PrimeNG sempre, sem notificação nativa**: perde alcance quando aba minimizada.

---

## R8 — `anonymous_id` e privacidade (sem fingerprinting)

**Decision**: `anonymous_id = crypto.randomUUID()` gerado no widget e salvo em `localStorage` chave `omnidesk_visitor_id`. Backend nunca infere identidade — armazena somente o que o widget envia. IP parcial (3 octetos IPv4 ou prefixo /48 IPv6) capturado em `conversations.metadata` para fins de rate-limit/analytics agregada.

**Rationale**:
- Atende FR-003, FR-021 e a explicitação da Constituição §IV (LGPD).
- `localStorage` é o único storage disponível para origem cruzada que não precisa de cookies (cookies third-party são bloqueados em Safari/Brave por default).
- IP parcial mantém utilidade (geo-aproximada, defesa contra abuso) sem coletar IP completo.

**Alternatives considered**:
- **Fingerprinting (canvas, fonts, AudioContext)**: explicitamente rejeitado pela spec do produto.
- **First-party cookie no domínio do tenant**: requer cooperação de cada site — complica deploy.
- **IndexedDB**: mais robusto que localStorage mas API mais complexa; localStorage é suficiente para 2 chaves curtas.

---

## R9 — Encerramento automático: cron vs polling assistido

**Decision**: 2 jobs Hangfire scheduled (cron `0 * * * *` — a cada hora):

- `AbandonmentSweepJob`: `UPDATE conversations SET status='abandoned' WHERE status='open' AND attendant_id IS NULL AND last_message_at < NOW() - widget_config.abandonment_timeout_hours::interval`.
- `InactivitySweepJob`: similar para `attendant_id IS NOT NULL` com `inactivity_close_hours` e `ended_by='system_inactivity'`. Após UPDATE, publica `conversation.resolved` no canal Redis para o widget reagir.

`last_message_at` é coluna materializada em `conversations`, atualizada por trigger ou pelo próprio repositório a cada `INSERT INTO messages`.

**Rationale**:
- Hangfire já está em uso (zero pacote novo).
- Cron @hourly atende SC-005/SC-006 (granularidade de 1h).
- Coluna materializada evita `MAX(messages.created_at)` em cada sweep — barato.

**Alternatives considered**:
- **Per-conversation timer agendado**: estouro de timers em cenários com 10k conversas ativas; sweep batch é mais simples e escala melhor.
- **Trigger Postgres**: viola Princípio V (lógica de negócio em SQL é mais difícil de testar e migrar); mantemos em código C# testável.

---

## R10 — Preview ao vivo no painel de configuração

**Decision**: `<iframe>` apontando para uma página HTML servida pelo CRM (`/crm/widget-preview.html`) que carrega o widget via `loader.js` usando o `widget_token` do tenant logado. O CRM injeta os campos editados via `postMessage` ao iframe; uma camada `WidgetPreviewBridge` no widget aplica overrides locais (cor, ícone, mensagens, posição) **sem persistir** — o preview reflete o que seria salvo. Quando o admin clica "Salvar", a configuração é persistida e o iframe pode recarregar (ou continuar com overrides até refresh).

**Rationale**:
- Reusa o widget real (zero código duplicado de UI no CRM) — atende FR-029 ("preview reflete configuração real").
- `postMessage` é a API padrão para comunicação cross-frame (widget está em Shadow DOM no iframe; `postMessage` funciona em ambos lados).
- A API `GET /api/public/widget/init` é chamada com o token do próprio tenant — não precisa relaxar `allowed_domains`, basta o backend liberar quando `Origin = ${admin_or_crm_origin}` (lista de origens internas adicionada em config global do backend).

**Alternatives considered**:
- **Mock de preview em Angular puro**: duplica UI do widget — risco de divergência visual.
- **Renderizar o widget como Custom Element direto no DOM do CRM**: o Shadow DOM do widget herda fontes/estilos do PrimeNG mesmo com `closed`, criando inconsistência (Shadow DOM isola CSS, não fonts inheritance via root). Iframe garante isolamento total do contexto do navegador.

---

## R11 — Roles com permissão para alterar widget config

**Decision**: V1 → `tenant_admin` apenas. Diferida para Spec 002 a definição de role granular (FR-046).

**Rationale**: A Spec 002 (Auth) é dona da matriz de permissões. Adicionar `widget.config.read|write` agora antecipa decisões dela.

**Alternatives considered**: Adicionar `tenant_attendant` à leitura (read-only) — diferido para V1.1 com pedido formal do produto.

---

## R12 — Limite de 50 mensagens de contexto na reabertura

**Decision**: Variável de ambiente `WIDGET_RESUMED_CONTEXT_MESSAGE_LIMIT=50`. Quando o visitante inicia nova conversa após `resolved`, `LiveChatConversationGateway.GetRecentMessagesAsync(thread, limit)` retorna até esse limite das mensagens da conversa anterior do mesmo `visitor_id`. O Orchestrator (Spec 006) já consome via interface — sem mudança lá.

**Rationale**:
- A Spec 006 já tem `ai_settings.context_window_messages` (default 20) para conversa em andamento. Aqui é diferente: contexto **pré-conversa** (último resolved). Manter variável separada permite afinar custo sem mexer em `ai_settings`.
- 50 mensagens cobrem a maioria dos fluxos sem inflar prompt além de ~3k tokens.

**Alternatives considered**:
- **Reusar `ai_settings.context_window_messages`**: viola separação semântica.
- **Sem limite**: estoura prompt em conversas longas (40+ mensagens) — custo proibitivo.
