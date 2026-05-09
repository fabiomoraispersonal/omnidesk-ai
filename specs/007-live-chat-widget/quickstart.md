# Quickstart — Spec 007 Live Chat (Widget)

Fluxos de validação manual após implementação. Espelha as User Stories 1–6 do `spec.md` e os Critérios de Aceite §13 da spec original (`docs/specs/07-live-chat.spec.md`).

> **Pré-requisitos**: API rodando local (`docker compose up`); tenant de teste `clinica-test` provisionado (Spec 003); ao menos 1 atendente cadastrado no depto padrão (Spec 005); Orchestrator com prompt mínimo válido (Spec 006).

---

## QS-1 — Visitante conversa com IA via widget (User Story 1)

**Setup**:

1. Crie um arquivo HTML local `/tmp/teste-widget.html`:
   ```html
   <!doctype html>
   <html><head><title>Site Teste</title></head><body>
     <h1>Página do tenant</h1>
     <script>window.OmniDeskConfig = { token: "<COLE_WIDGET_TOKEN>" };</script>
     <script src="https://cdn.omnicare.ia.br/widget/v1/loader.js" async></script>
   </body></html>
   ```
2. Pegue o `widget_token` do tenant: `SELECT widget_token FROM public.tenants WHERE slug='clinica-test';`
3. (Em dev) Sirva via `python -m http.server 8000 --directory /tmp` para ter origem `http://localhost:8000`.
4. Em `widget_config`, deixe `allowed_domains = NULL` ou inclua `localhost:8000`.

**Passos**:

1. Abrir `http://localhost:8000/teste-widget.html` em navegador anônimo.
2. **Esperado**: launcher (botão flutuante) visível em `bottom_right`, com cor padrão `#2563EB` e ícone de chat.
3. Clicar no launcher → painel abre com slide-up. **Esperado**: cabeçalho com nome da empresa, área de mensagens vazia, mensagem de boas-vindas, checkbox "Li e aceito os Termos de Privacidade", botão de envio **desabilitado**.
4. Tentar digitar e clicar enviar → **Esperado**: nada acontece, botão segue desabilitado.
5. Marcar o checkbox → botão habilita.
6. Digitar "Olá" e enviar.
7. **Esperado**: mensagem aparece na lista (alinhada à direita) em < 200 ms; resposta do Orchestrator chega via WebSocket em < 5 s; indicador "digitando…" aparece no momento em que o agente compõe.

**Verificação SQL**:

```sql
SET search_path TO tenant_clinica_test;
SELECT id, anonymous_id FROM visitors;             -- 1 linha, anonymous_id == localStorage do browser
SELECT id, status, lgpd_consent_at, channel        -- status='open', lgpd_consent_at preenchido, channel='live_chat'
  FROM conversations
  ORDER BY created_at DESC LIMIT 1;
SELECT sender_type, content_type, content
  FROM messages
  ORDER BY created_at ASC;                         -- visitor + ai_agent (ao menos)
```

**Critério OK**: SC-002 (LGPD enforcement), SC-003 (primeira resposta < 5s), FR-001/003/018/019.

---

## QS-2 — Tenant configura aparência e LGPD (User Story 2)

**Passos**:

1. Logar no CRM como `tenant_admin` do `clinica-test`.
2. Ir em **Configurações → Live Chat**.
3. Aba "Aparência":
   - Cor primária: `#7A9E7E`
   - Ícone: `support`
   - Posição: `bottom_left`
   - Nome empresa: "Clínica Teste"
   - Mensagem boas-vindas: "Olá! Bem-vindo à Clínica Teste."
   - **Esperado**: preview ao vivo (à direita) reflete cada mudança em < 200 ms (SC-013).
4. Aba "Privacidade / LGPD":
   - Inicialmente vazio → **Esperado**: alerta "⚠️ Configure os termos de privacidade. O widget exibirá um texto genérico..." visível.
   - Preencher textarea com texto de teste; URL: `https://www.clinica-teste.com.br/privacidade`.
5. Aba "Comportamento": deixar defaults (8h e 24h).
6. Aba "Segurança": adicionar `localhost:8000` em `allowed_domains`.
7. Aba "Instalação": clicar "Copiar código" → conferir clipboard.
8. Salvar.
9. Recarregar `/tmp/teste-widget.html`.
10. **Esperado**: launcher na cor verde-oliva, ícone "support", posição `bottom_left`, nome "Clínica Teste".
11. Tentar abrir o widget de outra origem (`http://outrodominio.test:8000`) → **Esperado**: WebSocket falha em handshake; widget exibe "Atendimento indisponível neste site"; backend log mostra `403 Forbidden` (SC-004).

**Critério OK**: FR-005, FR-027–FR-031, SC-004, SC-008, SC-013.

---

## QS-3 — Atendente recebe transbordo e gerencia conversas no CRM (User Story 3)

**Setup**:

1. Sub-agente "Comercial" do tenant `clinica-test` está ativo, vinculado ao depto Comercial.
2. Atendente `joao@clinica-test.com.br` está no depto Comercial.

**Passos**:

1. Atendente loga no CRM e minimiza a janela.
2. **Esperado** (primeira sessão): permissão de notificação solicitada; atendente aceita.
3. Visitante (na página do widget) envia: "Quero falar com um atendente".
4. **Esperado**:
   - Backend detecta keyword "atendente" e força `transfer_to_human` (Spec 006).
   - CRM em background recebe browser notification "Nova conversa de Anônimo" em < 2 s (SC-009).
5. Atendente maximiza CRM → vai em **Conversas** (live chat inbox).
6. **Esperado**: conversa visível na lista esquerda com badge **vermelho** (nova). Histórico da IA visível ao clicar.
7. Atendente envia "Olá, em que posso ajudar?".
8. **Esperado**: visitante vê a mensagem no widget em < 200 ms via WS; badge da conversa muda para amarelo (em andamento).
9. Atendente clica em "Encerrar conversa".
10. **Esperado**:
    - Visitante vê mensagem "O atendimento foi encerrado." e o painel exibe botão "Iniciar nova conversa".
    - Conversa some da lista de ativas do atendente.
    - `conversations.status = 'resolved'`, `ended_by = 'attendant'`, `ended_at` preenchido.

**Critério OK**: FR-011, FR-032–FR-039, SC-009, SC-012.

---

## QS-4 — Visitante retorna e retoma conversa (User Story 4)

**Setup**:

1. Visitante já tem `omnidesk_visitor_id` e `omnidesk_conversation_id` em `localStorage` apontando para uma conversa `open` com IA (cenário pós-QS-1).

**Passos — caso `open`**:

1. Visitante fecha o navegador.
2. Reabre `http://localhost:8000/teste-widget.html` → clica no launcher.
3. **Esperado**: painel abre exibindo histórico completo das mensagens anteriores em < 1 s (SC-010); WebSocket conecta; visitante envia nova mensagem; IA responde mantendo contexto (lembra dados já fornecidos).

**Passos — caso `resolved`**:

1. Atendente encerra conversa (QS-3 passo 9).
2. Visitante reabre o widget.
3. **Esperado**: painel exibe histórico em modo somente-leitura + botão "Iniciar nova conversa".
4. Visitante clica no botão → envia "Tenho outra dúvida".
5. **Esperado**:
   - Nova `conversation` criada (`status='open'`, `lgpd_consent_at` herdado se ainda válido).
   - IA responde com contexto das últimas 50 mensagens da anterior (verificável em `agent_activity_logs.input_messages_count` ≤ 50 + nova mensagem).

**Passos — caso `abandoned`**:

1. Forçar timeout: `UPDATE tenant_clinica_test.conversations SET last_message_at = NOW() - INTERVAL '9 hours' WHERE id = '...';`
2. Disparar `AbandonmentSweepJob` manualmente (Hangfire dashboard) → verificar `status='abandoned'`.
3. Visitante reabre widget.
4. **Esperado**: nova conversa iniciada automaticamente (fluxo inicial completo: welcome, LGPD se necessário).

**Critério OK**: FR-014–FR-017, SC-010.

---

## QS-5 — Sistema gerencia ciclo de vida automaticamente (User Story 5)

**Cenário A — abandonment IA**:

1. Conversa criada com IA (status `open`, sem `attendant_id`).
2. `UPDATE conversations SET last_message_at = NOW() - INTERVAL '9 hours' WHERE id = '...';`
3. Aguardar a hora cheia ou disparar `AbandonmentSweepJob`.
4. **Esperado**: `status='abandoned'`, `ended_by IS NULL`, conversa some das listas.

**Cenário B — inactivity humano**:

1. Conversa em atendimento humano.
2. `UPDATE conversations SET last_message_at = NOW() - INTERVAL '25 hours' WHERE id = '...';`
3. Disparar `InactivitySweepJob`.
4. **Esperado**: `status='resolved'`, `ended_by='system_inactivity'`, widget recebe `conversation.resolved` via WS.

**Cenário C — encerramento por desabilitação**:

1. Tenant admin altera toggle geral para `is_enabled=false` no CRM.
2. **Esperado**:
   - Todas as `open` recebem mensagem automática "O atendimento foi encerrado pelo sistema." (sender_type='system').
   - `status='resolved'`, `ended_by='system_disable'`.
   - Próxima visita ao site exibe "No momento o atendimento está indisponível." sem campo de envio.

**Cenário D — encerramento natural pela IA**:

1. Configurar prompt do Orchestrator com instrução: "Após confirmar agendamento, finalize com 'Foi um prazer atender.'".
2. Visitante completa fluxo até a IA enviar mensagem de despedida.
3. Orchestrator chama tool de encerramento → backend marca `status='resolved'`, `ended_by='ai_agent'`.
4. **Esperado**: widget exibe "Iniciar nova conversa".

**Critério OK**: FR-008–FR-013, SC-005, SC-006.

---

## QS-6 — Visitante envia anexos (User Story 6)

**Cenário A — JPG válido (2 MB)**:

1. Visitante clica no ícone de anexo.
2. Seleciona `foto.jpg` (2 MB).
3. **Esperado**: upload bem-sucedido; mensagem aparece com preview inline; CRM mostra a mesma mensagem.
4. Verificar MinIO: `mc ls myminio/tenant-clinica-test/widget-uploads/<conversation_id>/` mostra o arquivo.

**Cenário B — PDF (5 MB)**:

1. Visitante anexa `documento.pdf` (5 MB).
2. **Esperado**: mensagem com nome, tamanho e link de download.

**Cenário C — arquivo > 10 MB**:

1. Visitante tenta anexar `video.mp4` (15 MB).
2. **Esperado**: widget rejeita imediatamente (validação client-side) com mensagem "Arquivo excede o tamanho máximo de 10 MB"; backend NÃO recebe requisição (verificar logs).

**Cenário D — MIME spoofed**:

1. Renomear `malware.exe` para `documento.pdf`.
2. Tentar enviar.
3. **Esperado**: backend detecta MIME real (não-PDF), rejeita com `415 Unsupported Media Type`; widget mostra erro genérico.

**Critério OK**: FR-040–FR-042, SC-011.

---

## QS-7 — Reconexão WebSocket sem perda

**Passos**:

1. Visitante abre o widget e conversa normalmente.
2. (Dev) Pausar o backend: `docker pause omnidesk-api`.
3. Visitante digita "Mensagem durante a queda" e clica enviar → **Esperado**: mensagem aparece com indicador "enviando…" / na fila.
4. (Dev) Despausar o backend: `docker unpause omnidesk-api`.
5. **Esperado**:
   - Widget reconecta WS via backoff em < 30 s (SC-007).
   - Mensagem da fila é enviada automaticamente.
   - IA responde normalmente.
   - Banner discreto "Reconectando…" desaparece.

**Critério OK**: FR-022–FR-025, SC-007.

---

## QS-8 — Smoke E2E (Playwright)

Roteiro automatizado em `src/omniDesk.Widget/tests/e2e/visitor-flow.spec.ts`:

```
test('visitante completa primeira conversa', async ({ page }) => {
  await page.goto('http://localhost:8000/teste-widget.html');
  await page.locator('omnidesk-widget').waitFor();
  await page.locator('button[aria-label="Abrir chat"]').click();
  await page.locator('input[type="checkbox"][name="lgpd-consent"]').check();
  await page.locator('textarea[name="message"]').fill('Olá');
  await page.locator('button[aria-label="Enviar mensagem"]').click();
  await expect(page.locator('.message[data-sender="ai_agent"]').first()).toBeVisible({ timeout: 10000 });
});
```

Rodar com `pnpm --filter omniDesk.Widget test:e2e` (CI separado, não bloqueia build principal).

---

## QS-9 — Verificação de `widget_token` no provisionamento

**Passos**:

1. Provisionar novo tenant via SaaS Admin (Spec 003).
2. Verificar:
   ```sql
   SELECT slug, widget_token FROM public.tenants WHERE slug='novo-tenant';        -- widget_token preenchido
   SELECT * FROM tenant_novo_tenant.widget_config;                                  -- 1 linha com defaults
   ```

**Critério OK**: FR-002, FR-027.

---

## Resumo de cobertura

| User Story | Quickstart | FRs principais | SCs principais |
|---|---|---|---|
| US1 — IA via widget (MVP) | QS-1, QS-9 | FR-001/003/018/019/022/043 | SC-001/002/003 |
| US2 — Configuração no CRM | QS-2 | FR-005/027–031 | SC-004/008/013 |
| US3 — Atendente multi-conv | QS-3 | FR-011/032–039 | SC-009/012 |
| US4 — Retomada | QS-4 | FR-014–017 | SC-010 |
| US5 — Ciclo de vida | QS-5 | FR-008–013 | SC-005/006 |
| US6 — Anexos | QS-6 | FR-040–042 | SC-011 |
| Resilência WS | QS-7 | FR-024 | SC-007 |
