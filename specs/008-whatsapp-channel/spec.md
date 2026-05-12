# Feature Specification: Canal WhatsApp Business

**Feature Branch**: `008-whatsapp-channel`
**Created**: 2026-05-10
**Status**: Draft
**Input**: User description: "Spec 08 — WhatsApp: integração com a API Oficial do WhatsApp Business (Meta) para receber e enviar mensagens dentro do mesmo pipeline de conversas dos demais canais. Cobre configuração do canal por tenant, webhook de recepção, envio (texto livre e templates), gestão de templates pré-definidos com aprovação Meta e janela de sessão de 24 horas."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Recepção de mensagem WhatsApp e atendimento pela IA (Priority: P1)

Um cliente envia uma mensagem de WhatsApp para o número da clínica. O sistema recebe via webhook da Meta, cria/reutiliza a conversa, persiste a mensagem, e encaminha para o orquestrador de IA — que responde dentro do mesmo pipeline já usado por outros canais. Se a IA decidir transferir para humano, o ticket é aberto e o atendente assume no CRM.

**Why this priority**: Sem este fluxo, o canal WhatsApp não existe — é o motivo central da spec. Toda a infraestrutura de webhook, persistência de mensagem e janela de 24h gira em torno desta jornada. Sem ela, nada do resto agrega valor.

**Independent Test**: Configurar um tenant com canal WhatsApp ativo, simular um POST de webhook da Meta com mensagem de texto válida (assinatura HMAC válida), e verificar que (a) o backend respondeu 200 OK em < 5s, (b) a conversa foi criada no schema do tenant, (c) a mensagem foi persistida, (d) `wa_session_expires_at` foi setado para `now() + 24h`, (e) a IA processou e respondeu via Meta API.

**Acceptance Scenarios**:

1. **Given** tenant `clinica-abc` com canal WhatsApp ativado e cliente novo `+5511999998888`, **When** Meta envia webhook POST com mensagem `text` assinada via HMAC-SHA256 válido, **Then** o sistema retorna `200 OK` em < 5s, cria visitor + conversation (channel = `whatsapp`), persiste a mensagem, atualiza `wa_session_expires_at` para `now() + 24h` e enfileira para a IA processar.
2. **Given** webhook recebido com assinatura HMAC inválida, **When** a Meta tenta entregar, **Then** o backend responde `403 Forbidden` e nenhum dado é persistido.
3. **Given** janela de 24h ativa para uma conversa, **When** a IA gera resposta, **Then** a mensagem é enviada ao cliente como texto livre via Meta Graph API e o `wa_message_id` é registrado para rastreamento de status.
4. **Given** webhook inicial de verificação Meta (`GET ...?hub.mode=subscribe&hub.verify_token=X&hub.challenge=Y`), **When** `X` confere com `whatsapp_config.webhook_verify_token`, **Then** o backend responde com `Y` em texto plano e status `200`.
5. **Given** webhook com tipo de mensagem não suportado (ex.: `sticker`, `location`), **When** processado, **Then** o sistema ignora silenciosamente, registra log de auditoria e nenhuma mensagem é criada na conversa.

---

### User Story 2 — Configuração do canal WhatsApp pelo tenant (Priority: P1)

Operador SaaS gera credenciais Meta (Phone Number ID, WABA ID, Access Token) fora do sistema. O `tenant_admin` insere essas credenciais na tela **CRM → Configurações → WhatsApp**, copia a Webhook URL + Verify Token gerados pelo sistema para configurar na Meta, ativa o canal e passa a receber mensagens. `supervisor` consegue ver o status do canal mas não edita o Access Token; `tenant_attendant` não acessa essa tela.

**Why this priority**: É o gate operacional para a US1 funcionar — sem credenciais salvas, o webhook não pode validar assinaturas nem enviar mensagens. Sem esta tela o tenant não consegue colocar o canal em produção.

**Independent Test**: Logado como `tenant_admin`, abrir a tela de configuração, preencher os 4 campos (Phone Number ID, WABA ID, Access Token, Display Name), clicar em "Salvar" e depois em "Ativar canal". Confirmar que o badge muda para "Ativo", a Webhook URL está visível e copiável, e que um GET subsequente em `/api/whatsapp/config` não retorna o Access Token em texto plano.

**Acceptance Scenarios**:

1. **Given** `tenant_admin` autenticado e canal não configurado, **When** acessa a tela de WhatsApp, **Then** vê badge "🔴 Não configurado", os campos editáveis vazios, e a Webhook URL + Verify Token (gerados no provisionamento) já populados em modo somente-leitura.
2. **Given** os 4 campos preenchidos com credenciais válidas, **When** clica "Salvar", **Then** o sistema criptografa o Access Token com AES-256, persiste, e o badge muda para "🟡 Configurado / Inativo".
3. **Given** canal configurado mas inativo, **When** `tenant_admin` clica no toggle "Ativar canal", **Then** `is_enabled` vira `true`, o badge muda para "🟢 Ativo" e o canal passa a aceitar webhooks.
4. **Given** `supervisor` autenticado, **When** abre a mesma tela, **Then** vê o badge de status e demais campos em modo somente-leitura — o campo Access Token aparece como mascarado (`••••••••`) e não é editável.
5. **Given** chamada `GET /api/whatsapp/config` por qualquer role autorizado, **When** processada, **Then** o response contém o status, phone_number, display_name e a indicação "access_token configurado: true/false" — **nunca** o token em texto plano.

---

### User Story 3 — Atendente envia mensagem dentro da janela de 24h (Priority: P1)

A IA encerrou seu turno e transferiu para humano. O atendente abre a conversa no CRM, vê o histórico, digita uma resposta livre e envia. Como o cliente respondeu há menos de 24h, a mensagem sai como texto livre (sem template). O CRM exibe os ícones de status conforme a Meta envia atualizações: ✓ enviado → ✓✓ entregue → ✓✓ azul lido.

**Why this priority**: É o caminho feliz da operação humana e cobre 90%+ dos envios. Sem ele o canal só serve para a IA — toda a justificativa de transbordo humano desaparece.

**Independent Test**: Em uma conversa com `wa_session_expires_at` no futuro, atendente envia "Olá!" via CRM. Verificar que (a) o backend chama Meta `POST /{phone_number_id}/messages`, (b) o `wa_message_id` retornado é salvo na mensagem, (c) ao chegar o webhook de status `delivered`, o evento WS `wa.message_status` é emitido e o CRM atualiza o ícone.

**Acceptance Scenarios**:

1. **Given** conversa com janela de 24h ativa, **When** atendente envia mensagem de texto via CRM, **Then** o backend valida a janela, envia via Meta API, persiste o `wa_message_id` e exibe ícone ✓ no CRM.
2. **Given** mensagem enviada e webhook Meta entrega status `delivered`, **When** processado pelo backend, **Then** o registro em MongoDB `wa_message_statuses` é criado e o evento WebSocket `wa.message_status` é emitido para o tenant — atualizando o ícone para ✓✓ no CRM.
3. **Given** webhook Meta entrega status `read`, **When** processado, **Then** o ícone vira ✓✓ azul no CRM, **sem** alterar status do ticket, sem disparar SLA, sem ações automáticas (uso visual apenas).
4. **Given** Meta retorna `failed` para uma mensagem, **When** o status chega via webhook, **Then** o ícone vira ✗ com tooltip exibindo o motivo do erro retornado pela Meta.

---

### User Story 4 — Janela de 24h expirada → atendente usa template aprovado (Priority: P2)

A última mensagem do cliente foi há mais de 24h. O atendente abre a conversa, tenta digitar texto livre e o CRM bloqueia o envio com a mensagem "A janela de 24h expirou. Selecione um template para enviar." O atendente escolhe um template `approved`, preenche as variáveis (nome, data etc.) e envia. Se o cliente responder, a janela reinicia e mensagens livres voltam a ser permitidas.

**Why this priority**: Conformidade obrigatória com a política da Meta. Sem isso o sistema gera erros da Graph API e o canal pode ser sancionado. Mas só ativa quando a janela expira — por isso P2, não P1.

**Independent Test**: Em uma conversa com `wa_session_expires_at` no passado, tentar enviar texto livre via CRM e confirmar bloqueio + mensagem orientativa. Selecionar template aprovado, preencher variáveis, enviar e confirmar que a Meta API recebe payload de template (não de texto). Simular nova mensagem do cliente e confirmar que o input de texto livre volta a ser habilitado.

**Acceptance Scenarios**:

1. **Given** conversa com `wa_session_expires_at` no passado, **When** atendente tenta enviar texto livre via CRM, **Then** o backend bloqueia com erro semântico e o CRM exibe seletor de template.
2. **Given** atendente seleciona template `approved` e preenche todas as variáveis obrigatórias, **When** envia, **Then** o backend chama Meta API com payload de template, persiste a mensagem na conversa e registra `wa_message_id`.
3. **Given** janela expirada e nenhuma resposta nova do cliente, **When** a IA tenta gerar resposta, **Then** o orquestrador é bloqueado pelo backend (a IA nunca envia template) e o sistema notifica o atendente humano para assumir.
4. **Given** janela vai expirar em < 1 hora, **When** o tempo cruza esse limiar, **Then** o evento WebSocket `wa.session_expiring` é emitido para o CRM, alertando o atendente.
5. **Given** cliente envia nova mensagem com janela expirada, **When** webhook é processado, **Then** `wa_session_expires_at` é atualizado para `now() + 24h` e o CRM emite `wa.session_resumed` (ou equivalente) reabilitando o input livre.

---

### User Story 5 — Tenant cria, edita e submete templates à Meta (Priority: P2)

`tenant_admin` ou `supervisor` acessa **CRM → Configurações → WhatsApp → Templates**. Escolhe um tipo pré-definido (ex.: `appointment_reminder`) — o corpo é pré-preenchido com placeholders fixos `{{1}} {{2}} {{3}}`. Edita só o texto ao redor das variáveis (ou cria do zero com tipo `custom`). Clica "Submeter à Meta", o status vai para `pending_meta` e, quando a Meta aprovar/rejeitar via webhook, a lista atualiza com badge `approved` ou `rejected` (com motivo).

**Why this priority**: Templates aprovados são o único caminho para envio fora da janela (US4). Sem eles, US4 não funciona. P2 porque o tenant pode operar inicialmente sem templates próprios — usando os pré-definidos do sistema enquanto aguarda aprovações.

**Independent Test**: Criar template tipo `appointment_reminder`, editar o texto, salvar como rascunho, submeter à Meta. Confirmar que (a) o nome foi gerado como `lembrete_consulta_{slug}`, (b) o status é `pending_meta`, (c) o número e ordem das variáveis é fixo (3 variáveis), (d) ao receber webhook Meta `approved`, o status vira `approved` e o template aparece como selecionável no envio. Simular `rejected` e confirmar que o motivo da Meta é exibido.

**Acceptance Scenarios**:

1. **Given** `tenant_admin` na tela de Templates, **When** seleciona tipo `appointment_reminder`, **Then** o sistema pré-preenche o body com texto padrão e 3 variáveis fixas (`{{1}}` nome, `{{2}}` data, `{{3}}` horário) que não podem ser removidas/reordenadas.
2. **Given** template salvo com status `draft`, **When** clica "Submeter à Meta", **Then** o backend chama Meta `POST /message_templates` com category `utility`, persiste `submitted_at = now()`, e o status vira `pending_meta`.
3. **Given** Meta envia webhook de aprovação, **When** processado, **Then** o status do template vira `approved`, `meta_template_id` é populado, e o template aparece na lista de seleção da US4.
4. **Given** Meta rejeita o template, **When** webhook chega, **Then** o status vira `rejected` e o motivo (string da Meta) fica visível no card do template no CRM.
5. **Given** template com status `pending_meta` ou `approved`, **When** usuário tenta editar, **Then** o backend bloqueia (apenas `draft` é editável) e o CRM esconde o botão "Editar".
6. **Given** template tipo `custom`, **When** criado, **Then** o body é livre, o tenant define quantas variáveis quiser e suas labels — sem estrutura fixa.
7. **Given** `tenant_attendant` autenticado, **When** tenta acessar a tela de Templates, **Then** o acesso é negado (apenas `tenant_admin` e `supervisor` podem gerenciar templates).

---

### User Story 6 — Recepção de mídia (imagem, documento, áudio) (Priority: P3)

Cliente envia uma foto, PDF ou áudio. O backend baixa o binário pela Meta (URL temporária + Access Token), faz upload para o bucket MinIO do tenant, persiste a mensagem com `attachment_url`, e o CRM exibe a mídia inline. Áudios aparecem como player; sem transcrição automática no MVP.

**Why this priority**: Importante para casos clínicos (foto de exame, áudio com sintomas) mas não bloqueia o fluxo de texto. P3 — entrega em wave separada após o fluxo de texto estabilizar.

**Independent Test**: Simular webhook Meta com mensagem `image`, `document` e `audio`. Verificar que cada uma (a) tem o binário baixado da Meta, (b) é persistida no bucket `tenant-{slug}/`, (c) gera `messages.attachment_url` válida, (d) é exibida corretamente no CRM (img preview, link de download, audio player).

**Acceptance Scenarios**:

1. **Given** webhook com mensagem `image`, **When** processado, **Then** o backend baixa o binário, sobe para MinIO no bucket do tenant, persiste a mensagem com `attachment_url` apontando para o objeto, e o CRM exibe preview da imagem.
2. **Given** webhook com mensagem `document` (PDF), **When** processado, **Then** o arquivo é salvo no MinIO e o CRM exibe link de download com o nome original do arquivo.
3. **Given** webhook com mensagem `audio`, **When** processado, **Then** o arquivo é salvo e o CRM exibe um player de áudio funcional — sem transcrição automática.
4. **Given** webhook com tipo `video`, `sticker`, `location`, `contacts`, `reaction` ou `interactive`, **When** processado, **Then** o sistema ignora silenciosamente, registra log de auditoria, e nenhuma mensagem é exibida ao atendente.

---

### Edge Cases

- **Tenant sem canal configurado recebe webhook**: caso a Meta envie webhook para um `tenant_slug` cujo `whatsapp_config.is_enabled = false`, o backend responde `200 OK` (exigido pela Meta para não acionar retries) mas descarta a mensagem com log de auditoria.
- **Mensagem duplicada**: Meta pode reenviar webhook do mesmo evento. O sistema deduplica usando o `wa_message_id` da Meta — segunda chegada é ignorada.
- **Access Token expirado / revogado**: envio para Meta retorna `401`. O sistema desativa o canal automaticamente (`is_enabled = false`), notifica `tenant_admin` por e-mail/in-app e marca a mensagem como `failed`.
- **Falha de upload no MinIO** (mídia recebida): a mensagem é persistida com `attachment_url = null` e flag de erro; o CRM exibe placeholder "Falha ao carregar mídia — clique para tentar novamente".
- **Meta API fora do ar durante envio**: a fila do Hangfire faz retry exponencial até 3 tentativas; após esgotar, marca a mensagem como `failed` e notifica o atendente.
- **Janela expira durante uma sessão de IA ativa**: o orquestrador é interrompido no próximo turno; a IA não envia template; um ticket é aberto/atualizado para escalar ao humano com aviso visual.
- **Mais de uma tentativa de configuração simultânea**: dois `tenant_admin` salvando configuração ao mesmo tempo — o backend usa `updated_at` como optimistic lock, segundo write recebe erro `409 Conflict`.
- **Webhook recebido com payload malformado** (JSON inválido): backend retorna `200 OK` mesmo assim (Meta exige), mas registra erro detalhado em log e descarta o evento.

## Requirements *(mandatory)*

### Functional Requirements

#### Configuração do canal

- **FR-001**: O sistema MUST criar uma linha em `whatsapp_config` automaticamente no provisionamento de cada tenant, com `is_enabled = false` e `webhook_verify_token` único gerado aleatoriamente (≥ 32 caracteres).
- **FR-002**: O sistema MUST permitir que `tenant_admin` salve e edite os campos `phone_number_id`, `waba_id`, `access_token`, `phone_number` e `display_name`; `supervisor` MUST ter apenas leitura.
- **FR-003**: O sistema MUST criptografar o `access_token` em repouso com AES-256 antes da persistência e MUST NUNCA retornar o token em texto plano em qualquer endpoint da API.
- **FR-004**: O sistema MUST tornar o `webhook_verify_token` imutável após o provisionamento — nenhuma API permite alterá-lo.
- **FR-005**: O sistema MUST permitir ativar/desativar o canal via `PATCH /api/whatsapp/config/toggle`; quando `is_enabled = false`, webhooks recebidos são descartados após responder `200 OK`.

#### Webhook (recepção)

- **FR-006**: O sistema MUST validar a assinatura HMAC-SHA256 do header `X-Hub-Signature-256` em toda requisição POST de webhook; assinaturas inválidas MUST receber `403 Forbidden` sem persistência.
- **FR-007**: O sistema MUST responder `200 OK` ao webhook em até 5 segundos, antes de processar o conteúdo (timeout Meta: 20s); o processamento real MUST ocorrer assíncrono via fila Redis.
- **FR-008**: O sistema MUST suportar a verificação de webhook GET (`hub.mode=subscribe`) comparando `hub.verify_token` com `whatsapp_config.webhook_verify_token` e retornando `hub.challenge` em texto plano em caso de sucesso.
- **FR-009**: O sistema MUST deduplicar mensagens recebidas por `wa_message_id` (campo da Meta) — a segunda ocorrência é ignorada silenciosamente.
- **FR-010**: O sistema MUST processar tipos `text`, `image`, `document` e `audio`; tipos `video`, `sticker`, `location`, `contacts`, `reaction` e `interactive` MUST ser ignorados silenciosamente sem responder ao cliente.
- **FR-011**: O sistema MUST criar/reutilizar visitor pelo `wa_contact_phone` e criar/reutilizar conversation com `channel = whatsapp` no schema `tenant_{slug}`.
- **FR-012**: O sistema MUST atualizar `conversations.wa_session_expires_at = now() + 24h` a cada mensagem recebida do cliente.
- **FR-013**: Para mensagens de mídia (`image`, `document`, `audio`), o sistema MUST baixar o binário da Meta usando o Access Token, fazer upload para o bucket `tenant-{slug}` do MinIO e persistir o `attachment_url` na mensagem.

#### Envio

- **FR-014**: Antes de cada envio, o sistema MUST verificar `wa_session_expires_at`; se no passado ou nulo, mensagens de texto livre MUST ser bloqueadas e apenas templates `approved` MUST ser permitidos.
- **FR-015**: O sistema MUST chamar Meta Graph API `POST /v19.0/{phone_number_id}/messages` com o Access Token decifrado em memória, persistir o `wa_message_id` retornado e expor a mensagem como enviada no CRM.
- **FR-016**: A IA MUST NUNCA enviar templates — o backend MUST bloquear qualquer chamada do AgentOrchestrator que tente enviar payload de template.
- **FR-017**: Quando a janela de 24h expirar durante uma conversa atendida pela IA, o sistema MUST interromper a IA, MUST abrir/atualizar ticket e MUST notificar atendente humano com aviso explícito.
- **FR-018**: Para envios falhos retornados pela Meta, o sistema MUST persistir o status `failed` com `error_code` e `error_message` da Meta e MUST exibir o motivo no CRM via tooltip.

#### Status updates

- **FR-019**: O sistema MUST registrar cada status update da Meta (sent/delivered/read/failed) em MongoDB `wa_message_statuses` para auditoria.
- **FR-020**: O sistema MUST emitir evento WebSocket `wa.message_status` com `{ message_id, status, timestamp }` para todas as conexões abertas do tenant na conversa correspondente.
- **FR-021**: O status `read` MUST ser somente visual — sem alterar status do ticket, sem afetar SLA, sem disparar ações automáticas.

#### Eventos de janela

- **FR-022**: O sistema MUST emitir evento WebSocket `wa.session_expiring` quando `wa_session_expires_at` cruzar o limiar de 1 hora restante para a expiração.
- **FR-023**: O sistema MUST emitir evento WebSocket `wa.session_expired` no momento exato em que a janela expira em uma conversa ativa no CRM.

#### Templates

- **FR-024**: O sistema MUST oferecer 5 tipos pré-definidos: `appointment_reminder`, `appointment_confirmation`, `appointment_cancellation`, `follow_up`, `custom`.
- **FR-025**: Tipos pré-definidos MUST ter estrutura de variáveis fixa (quantidade e ordem) — o tenant edita apenas o texto ao redor; tipo `custom` MUST permitir corpo e variáveis livres.
- **FR-026**: O sistema MUST gerar automaticamente o `name` do template em snake_case a partir de `type + slug do tenant` (ex.: `lembrete_consulta_clinicaabc`); este nome MUST ser único por tenant.
- **FR-027**: Apenas `tenant_admin` e `supervisor` MUST poder criar, editar e submeter templates. `tenant_attendant` MUST ter acesso negado a `/api/whatsapp/templates*`.
- **FR-028**: Apenas templates com status `draft` MUST ser editáveis ou deletáveis; templates `pending_meta`, `approved` MUST ser imutáveis; `rejected` MUST poder ser deletado e recriado.
- **FR-029**: Submissão à Meta MUST chamar `POST /message_templates` com `category = utility` (V1 fixo), language `pt_BR`, body do template e variable_labels; o sistema MUST persistir `submitted_at` e mover status para `pending_meta`.
- **FR-030**: O webhook de status de template da Meta MUST atualizar `status` para `approved` ou `rejected`, populando `meta_template_id` e — em caso de `rejected` — o motivo retornado pela Meta.
- **FR-031**: Apenas templates com status `approved` MUST aparecer no seletor de templates do CRM ao enviar mensagem fora da janela.

#### Multi-tenant e segurança

- **FR-032**: O sistema MUST limitar cada tenant a no máximo 1 número de WhatsApp na V1; tentativas de configurar segundo número MUST ser rejeitadas com erro semântico.
- **FR-033**: Toda escrita em entidades WhatsApp MUST ser escopada ao schema `tenant_{slug}` — falha de isolamento entre tenants é blocker.
- **FR-034**: O `access_token` MUST nunca ser logado — Serilog MUST mascarar este campo em todos os sinks (MongoDB, console).

### Key Entities

- **whatsapp_config** (tenant schema): configuração 1:1 com tenant. Atributos-chave: `is_enabled`, `phone_number`, `display_name`, `waba_id`, `phone_number_id`, `access_token` (encrypted), `webhook_verify_token` (imutável), `business_hours_enabled`, `updated_at`.
- **whatsapp_templates** (tenant schema): templates de envio fora da janela. Atributos-chave: `meta_template_id`, `type` (enum), `name` (snake_case auto), `category` (sempre `utility` na V1), `language`, `status` (`draft|pending_meta|approved|rejected`), `body_template`, `variable_labels`, `rejection_reason`, `submitted_at`.
- **conversations** (extensão): ganha campos `wa_contact_phone` (E.164) e `wa_session_expires_at` (timestamptz). Sem isolamento adicional — segue o tenant da conversa.
- **wa_message_statuses** (MongoDB collection `{tenant_slug}_wa_message_statuses`): registro de auditoria de status updates. Atributos-chave: `message_id`, `wa_message_id`, `status`, `error_code`, `error_message`, `timestamp`.
- **Mensagem WhatsApp** (extensão de `messages` existente): herda da spec base. Acrescenta `wa_message_id` (referência Meta) e usa `attachment_url` para mídia.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% dos webhooks com assinatura HMAC válida recebem resposta `200 OK` em ≤ 5 segundos (medido no p95 em produção); webhooks com assinatura inválida recebem `403` em ≤ 1 segundo.
- **SC-002**: 99% das mensagens de texto recebidas são entregues a uma conversa visível no CRM em ≤ 10 segundos a partir do timestamp do webhook (latência de pipeline IA → exibição).
- **SC-003**: 100% das tentativas de envio fora da janela de 24h sem template selecionado são bloqueadas pelo backend, sem nenhuma chamada à Meta API.
- **SC-004**: Em auditoria de configurações de canal, 100% dos `access_token` aparecem cifrados em repouso (DB) e 0% retornam em texto plano em qualquer response da API.
- **SC-005**: Atendente consegue enviar uma mensagem de texto livre via CRM em ≤ 3 cliques a partir da inbox, e ≤ 2s entre o clique de "Enviar" e a confirmação visual de envio.
- **SC-006**: Status updates da Meta (`sent`/`delivered`/`read`) aparecem no CRM em ≤ 3 segundos a partir do recebimento do webhook (medido p95).
- **SC-007**: Templates pré-definidos cobrem 100% dos casos de uso da Spec 11 (Agenda) sem que o tenant precise criar templates `custom`.
- **SC-008**: Tenant `tenant_admin` consegue ativar o canal pela primeira vez (do zero ao envio funcional) em ≤ 10 minutos com as credenciais Meta em mãos.
- **SC-009**: 0% de mensagens recebidas de tipos não suportados geram resposta automática ao cliente (silêncio total).
- **SC-010**: 100% das mensagens enviadas com sucesso pela Meta têm o `wa_message_id` registrado e seus status updates rastreados até `delivered` ou `failed`.

## Assumptions

- O registro do número de WhatsApp na Meta (criação do app Business, verificação de negócio, geração do Access Token permanente) é feito **manualmente pelo Operador SaaS fora do sistema**. O CRM apenas armazena as credenciais geradas. **Decisão registrada — P1 do user input.**
- **Exatamente 1 número por tenant** na V1; suporte a múltiplos números é V2 sem ETA. **Decisão registrada — P3.**
- O status `read` é **apenas visual** — sem impacto em SLA, status de ticket ou ações automáticas. **Decisão registrada — P4.**
- Templates são gerenciados inteiramente no CRM (criação + submissão à Meta + recebimento de status). **Decisão registrada — P2.**
- Categoria de template fixa em `utility` na V1. Categorias `marketing` e `authentication` são V2.
- Idioma de templates fixo em `pt_BR` na V1.
- WhatsApp depende das specs já completas: **002** (Auth), **003** (Tenants — provisioning gera `whatsapp_config` vazio + `webhook_verify_token`), **004** (Roles — RBAC para tenant_admin/supervisor/tenant_attendant), **006** (AI Agents — orquestrador que consome mensagens recebidas).
- O orquestrador de IA (Spec 006) **já existe** e expõe interface `AgentOrchestrator.ProcessAsync(message, context)` — esta spec apenas alimenta mensagens via fila `{tenant}:incoming_messages` consumida pelo `IncomingMessageWorker` (Hangfire).
- A versão da Graph API usada é `v19.0`; upgrades são tratados como tarefas de manutenção fora desta spec.
- O sistema assume que o relógio do servidor está sincronizado via NTP — o cálculo de `wa_session_expires_at` depende disso.
- Áudios são armazenados sem transcrição automática no MVP; integração com Whisper/equivalente é V2.
- Hangfire + Redis (workers de mensagem) e MongoDB (`wa_message_statuses`, logs) já estão provisionados pela infra base e configurados via `IConfiguration` (sem `.env`).
- O bucket MinIO `tenant-{slug}` é criado no provisionamento do tenant (Spec 003) — esta spec apenas escreve nele.
