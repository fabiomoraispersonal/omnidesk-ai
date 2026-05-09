# Feature Specification: Agentes de IA

**Feature Branch**: `006-ai-agents`
**Created**: 2026-05-08
**Status**: Draft
**Input**: User description: "Spec 06 — Agentes de IA: camada de IA do OmniDesk com orquestração dinâmica em dois níveis (Orchestrator obrigatório por tenant + Sub-agentes especializados criados pelo tenant), tool calls para handoff entre agentes e transbordo para humano, integração via OpenAI Assistants API, configuração no CRM com playground e gestão de credenciais."

## User Scenarios & Testing *(mandatory)*

> Atores principais: **Cliente** (envia mensagens via Live Chat ou WhatsApp), **Tenant Admin** (configura agentes e prompts no CRM), **Orchestrator** (agente principal de IA, único por tenant), **Sub-agente** (agente especializado criado pelo tenant), **Atendente humano** (recebe transbordo da IA), **Sistema** (orquestra contexto, threads e fallback).

### User Story 1 — Cliente é atendido pelo Orchestrator desde a primeira mensagem (Priority: P1) 🎯 MVP

Todo cliente que entra em contato — pelo widget de Live Chat no site do tenant ou pelo WhatsApp — recebe resposta imediata do Agente Principal (Orchestrator) configurado para aquele tenant. O Orchestrator entende o pedido, responde dúvidas genéricas, faz saudação e, se a conversa for simples, resolve sem qualquer interação humana. O tenant admin precisa apenas editar o prompt do Orchestrator (já criado no provisionamento do tenant) com o tom, regras e limitações da empresa.

**Why this priority**: Sem o Orchestrator funcionando, nenhuma mensagem de cliente recebe resposta automatizada — todo o valor do produto (atendimento omnichannel com IA como primeiro contato) depende disso. Esta história entrega o atendimento mínimo viável.

**Independent Test**: Em um tenant recém-provisionado, o admin abre **CRM → Configurações → Agentes de IA**, encontra o Orchestrator já criado (não pode ser excluído nem duplicado), edita o prompt para refletir o negócio, salva e ativa. Cliente envia "Olá" pelo Live Chat → recebe resposta coerente do Orchestrator em até alguns segundos. Tudo isso funciona sem nenhum sub-agente cadastrado.

**Acceptance Scenarios**:

1. **Given** um tenant recém-provisionado, **When** o admin abre a tela de Agentes de IA, **Then** já existe exatamente um agente do tipo `orchestrator` com prompt-base — gerado a partir do template global do SaaS Admin (Spec 003) — e nenhum sub-agente.
2. **Given** o Orchestrator ativo com prompt válido, **When** o cliente envia a primeira mensagem da conversa, **Then** o sistema cria um thread persistente vinculado à conversa, processa via Orchestrator e responde no mesmo canal de origem.
3. **Given** o admin tenta excluir o Orchestrator, **When** confirma a operação, **Then** o sistema impede a exclusão com mensagem clara de que o Orchestrator é obrigatório e não-removível.
4. **Given** o admin tenta criar outro agente do tipo `orchestrator`, **When** envia o formulário, **Then** o sistema rejeita com mensagem indicando que cada tenant tem apenas um Orchestrator.
5. **Given** a conversa está em andamento, **When** o cliente envia uma segunda mensagem no mesmo canal, **Then** o sistema reusa o thread já existente e o Orchestrator responde mantendo todo o contexto da conversa.

---

### User Story 2 — Transbordo para humano quando a IA não deve resolver (Priority: P1)

A IA precisa saber a hora de "passar a bola". Quando o cliente pede explicitamente um humano ("quero falar com um atendente"), reclama formalmente, pede reembolso, ou quando o agente decide via prompt que o assunto é sensível, o agente transfere a conversa para um atendente humano. Isso abre um ticket no departamento correto, encerra o processamento por IA naquela conversa e notifica o atendente. A partir desse momento, qualquer mensagem do cliente vai para o humano — não é mais processada por IA.

**Why this priority**: Sem transbordo seguro, clientes ficam presos a uma IA limitada em situações sensíveis (reclamações, suporte avançado, assuntos legais). É uma rede de segurança obrigatória para qualquer operação real e também a integração crítica com o módulo de Tickets/Atendentes.

**Independent Test**: Em uma conversa ativa com o Orchestrator, o cliente envia "quero falar com um atendente". Esperado: o backend detecta a palavra-chave, injeta instrução de transbordo no contexto, a IA chama a tool `transfer_to_human`, o sistema cria um ticket no departamento padrão do tenant, marca a conversa como "aguardando atendente" e envia uma mensagem ao cliente informando o repasse. A próxima mensagem do cliente NÃO é processada pela IA — recebe resposta automática "Sua mensagem foi recebida. Um atendente responderá em breve." e fica visível para o atendente.

**Acceptance Scenarios**:

1. **Given** uma conversa ativa com o Orchestrator, **When** o cliente usa palavra-chave de transbordo (ex.: "atendente", "humano", "gerente"), **Then** o sistema injeta instrução de transbordo no contexto antes de chamar a IA, garantindo a execução da tool de transbordo mesmo se o prompt não previr.
2. **Given** o agente decide via prompt que o assunto é sensível, **When** chama `transfer_to_human(department_id, reason)`, **Then** o sistema cria um ticket no departamento informado com status `queued`, anexa todo o histórico da conversa e registra o motivo no ticket.
2a. **Given** o agente acionou `transfer_to_human`, **When** a tool é executada, **Then** o agente envia ao cliente, no mesmo turno, mensagem no formato _"Vou transferir você para nossa equipe de [Nome do Departamento]. Aguarde um momento."_ — antes de a IA parar de processar a conversa.
3. **Given** o transbordo foi acionado a partir do Orchestrator (que não tem departamento vinculado), **When** o ticket é criado, **Then** ele é roteado para o **departamento padrão do tenant** (campo `default_department_id` em Tenant Settings).
4. **Given** o ticket foi criado, **When** o cliente envia uma nova mensagem antes da atribuição do atendente, **Then** o sistema responde automaticamente "Sua mensagem foi recebida. Um atendente responderá em breve." e NÃO chama a IA; a mensagem fica preservada no histórico do ticket.
5. **Given** o atendente humano assumiu o ticket, **When** o cliente continua respondendo, **Then** as mensagens são entregues ao atendente sem qualquer processamento por IA na mesma conversa.

---

### User Story 3 — Tenant cria sub-agentes especializados e o Orchestrator faz handoff (Priority: P2)

O tenant admin cria sub-agentes especializados (ex.: "Agente Comercial", "Agente Financeiro", "Agendamento de Consultas"), cada um com seu próprio prompt, modelo, departamento vinculado e descritivo curto. O Orchestrator recebe a lista de sub-agentes ativos e decide, a cada turno, se responde diretamente ou faz `handoff_to_agent` para o especialista apropriado. Sub-agentes podem fazer handoff entre si ou devolver ao Orchestrator. Todos os agentes da mesma conversa compartilham o mesmo contexto (mesmo thread).

**Why this priority**: Multiplicar a qualidade do atendimento com especialização. O MVP funciona apenas com Orchestrator (História 1), mas a operação real ganha muito quando o admin pode separar fluxos por intenção (vendas, suporte, agenda).

**Independent Test**: Tenant cria 2 sub-agentes ativos: "Comercial" (descritivo: "vendas, planos e preços") vinculado ao depto Comercial; "Suporte" (descritivo: "dúvidas técnicas e problemas") vinculado ao depto Suporte. Cliente envia "quero saber preços" → Orchestrator faz handoff para Comercial, contexto preservado. Em seguida, cliente diz "minha conta não acessa" → o agente Comercial faz handoff para o Suporte sem reiniciar o thread.

**Acceptance Scenarios**:

1. **Given** o tenant admin cadastra um sub-agente com nome, descritivo curto, prompt, modelo e departamento vinculado, **When** salva, **Then** o sub-agente fica disponível na lista enviada ao Orchestrator a cada nova mensagem (apenas se `is_active = true`).
2. **Given** o Orchestrator processa uma mensagem que casa com o descritivo de um sub-agente, **When** chama `handoff_to_agent(agent_id, reason)`, **Then** o sistema passa o controle ao sub-agente no **mesmo thread**, sem reenviar histórico, e o sub-agente responde dentro do mesmo contexto.
3. **Given** um sub-agente A está ativo na conversa, **When** chama `handoff_to_agent` para sub-agente B (ou para o Orchestrator), **Then** o controle muda de agente e o contexto é integralmente preservado.
4. **Given** um sub-agente está marcado como inativo, **When** o Orchestrator monta a lista de sub-agentes para o próximo turno, **Then** o sub-agente inativo NÃO aparece e o Orchestrator não pode mais rotear para ele.
4a. **Given** uma conversa em andamento sob controle do sub-agente A, **When** o admin desativa o sub-agente A, **Then** a execução de IA atual termina sem ser interrompida e a próxima mensagem do cliente cai no Orchestrator (que decide se responde, faz handoff a outro sub-agente ativo ou transbordo).
5. **Given** um sub-agente possui histórico de conversas vinculadas, **When** o admin tenta excluir, **Then** o sistema executa apenas desativação lógica (`is_active = false` + `deleted_at`) — nunca exclusão física.
6. **Given** o sub-agente vinculado ao depto X chama `transfer_to_human`, **When** o ticket é criado, **Then** ele vai para o departamento X (não para o departamento padrão).

---

### User Story 4 — Tenant testa agentes em playground antes de ativar (Priority: P2)

Antes de ativar um agente em produção, o admin precisa validar o comportamento do prompt. A tela de edição oferece um playground com campo de mensagem de teste e área de resposta. O teste usa um thread temporário que é descartado ao final — não cria conversa real, não polui o histórico do cliente, não consome contadores de uso reais.

**Why this priority**: Reduz dramaticamente o risco de subir um prompt mal calibrado em produção e impactar clientes reais. Pode ser preterido se houver pressão extrema de tempo, mas é fundamental para qualidade.

**Independent Test**: Admin abre a edição de um sub-agente, digita "quero remarcar minha consulta" no playground, clica em "Testar". A resposta aparece em segundos abaixo. Ao fechar a tela, nenhuma conversa é criada no histórico de conversas do tenant nem no painel de tickets.

**Acceptance Scenarios**:

1. **Given** o admin está na edição de qualquer agente (Orchestrator ou sub-agente), **When** envia mensagem de teste, **Then** o sistema retorna a resposta do agente sem criar conversa real, ticket ou registro permanente em histórico de cliente.
2. **Given** o teste foi executado, **When** o admin sai da tela, **Then** o thread temporário é descartado e nenhum estado persistente do teste sobrevive.
3. **Given** o playground está ativo, **When** ocorrem múltiplos testes na mesma sessão, **Then** as mensagens fluem dentro do mesmo thread temporário (mantém contexto durante o teste) sem se misturar com conversas reais.

---

### User Story 5 — Configurações avançadas de IA por tenant (janela de contexto, modelos, chave OpenAI) (Priority: P3)

Em **CRM → Configurações → Agentes de IA → Configurações Avançadas**, o admin ajusta parâmetros que impactam custo, qualidade e isolamento da operação:
- Quantas mensagens recentes da conversa são enviadas como contexto à IA (default 20, mín. 5, máx. 100).
- Quais modelos OpenAI estão habilitados para os agentes deste tenant (ou usar a lista global do sistema).
- Chave própria da OpenAI (`tenants.openai_api_key`), com prioridade sobre a chave global do sistema.

**Why this priority**: Importante para tenants que querem isolar custos, usar modelos específicos por questões contratuais, ou impor limites de janela para reduzir gastos. Não bloqueia o MVP — defaults são suficientes para a maioria.

**Independent Test**: Admin altera `context_window_messages` para 5 → próxima mensagem do cliente é processada com apenas as últimas 5 mensagens enviadas como contexto. Admin cadastra chave própria da OpenAI → próximas chamadas de qualquer agente do tenant usam essa chave; ao remover, volta para a chave global automaticamente.

**Acceptance Scenarios**:

1. **Given** `context_window_messages` configurado em N para o tenant, **When** o sistema monta o contexto da próxima mensagem, **Then** envia exatamente as últimas N mensagens da conversa à IA.
2. **Given** o tenant tem chave própria configurada, **When** qualquer agente do tenant é executado, **Then** a chave do tenant é usada (prioridade sobre a chave global).
3. **Given** o tenant remove sua chave própria, **When** novas execuções ocorrem, **Then** o sistema cai automaticamente para a chave global do sistema sem interrupção.
4. **Given** `available_models` está preenchido para o tenant, **When** o admin edita um agente, **Then** apenas os modelos dessa lista aparecem no seletor; se vazio, aparece a lista global do sistema.
5. **Given** o admin tenta definir `context_window_messages` fora do intervalo `[5, 100]`, **When** salva, **Then** o sistema rejeita com mensagem de validação.

---

### User Story 6 — Resiliência: falha na OpenAI cai para humano automaticamente (Priority: P3)

Se a chamada à API da OpenAI falhar (timeout, erro 5xx, rate limit), o sistema tenta novamente uma única vez após 3 segundos. Se persistir, aciona automaticamente o transbordo para humano com motivo "Falha técnica no agente de IA", informa o cliente que houve instabilidade e abre o ticket no departamento correto. Erros de autenticação (401/403) NÃO retentam — transbordo imediato. Toda falha é logada para análise.

**Why this priority**: Evita que clientes fiquem sem resposta diante de instabilidade externa. Não é o caminho principal, mas é a diferença entre uma operação confiável e uma frágil.

**Independent Test**: Simular falha 503 da OpenAI durante a chamada → o sistema tenta novamente após 3s → se a segunda tentativa também falhar, o cliente recebe "Estamos com uma instabilidade técnica no momento. Vou transferir você para um de nossos atendentes." e o ticket é criado no departamento padrão do tenant. Em log de atividade, dois registros existem: o erro original e o de transbordo.

**Acceptance Scenarios**:

1. **Given** a primeira chamada à OpenAI retorna timeout/5xx/rate-limit, **When** o sistema processa o erro, **Then** registra o erro em log de atividade e tenta novamente uma vez após 3 segundos.
2. **Given** a segunda tentativa também falha, **When** o tratamento conclui, **Then** o sistema chama automaticamente `transfer_to_human` com motivo "Falha técnica no agente de IA" e envia mensagem padrão de instabilidade ao cliente.
3. **Given** a chamada à OpenAI retorna 401/403, **When** o erro é detectado, **Then** o sistema NÃO retenta e aciona transbordo imediato, registrando o erro em log.
4. **Given** o agente que falhou foi o Orchestrator, **When** o transbordo automático ocorre, **Then** o ticket é criado no departamento padrão do tenant.
5. **Given** o agente que falhou foi um sub-agente, **When** o transbordo automático ocorre, **Then** o ticket é criado no departamento vinculado a esse sub-agente.

---

### Edge Cases

- **Sub-agente vinculado a departamento desativado/excluído**: o sistema impede o uso do sub-agente em handoff (filtro a montar a lista enviada ao Orchestrator) ou cria o ticket no departamento padrão do tenant como fallback.
- **Cliente envia mensagem na exata janela em que o atendente está sendo atribuído**: a mensagem segue para o ticket; a IA não a processa porque a conversa já está marcada como aguardando humano.
- **Admin altera o prompt do Orchestrator com conversas em andamento**: o novo prompt vale para a próxima execução; conversas em curso seguem com o contexto do thread, mas a próxima resposta refletirá o prompt atualizado.
- **Admin desativa o único sub-agente disponível**: o Orchestrator volta a responder diretamente ou aciona `transfer_to_human` quando não tem alternativa.
- **Conversa fica `open` indefinidamente**: o thread persiste enquanto a conversa estiver aberta; se o módulo de Live Chat/WhatsApp marcar a conversa como `closed`, o thread é arquivado e uma futura mensagem do mesmo cliente cria nova conversa e novo thread.
- **Cliente repete palavra-chave de transbordo após o transbordo já ter ocorrido**: a mensagem permanece no ticket; a resposta automática "atendente responderá em breve" é enviada apenas se o atendente ainda não estiver disponível.
- **Variáveis de prompt referenciam dados ausentes (ex.: agente sem departamento vinculado)**: o sistema substitui pela string vazia ou um placeholder neutro e segue — não bloqueia a execução.
- **Ativar um sub-agente cujo registro na OpenAI foi apagado externamente**: o sistema detecta a ausência ao executar e recria o Assistant antes de prosseguir.
- **Tenant atinge rate limit da própria chave OpenAI**: trata como falha 429 → 1 retry → transbordo se persistir. Não cai para a chave global.

## Requirements *(mandatory)*

### Functional Requirements

#### Modelo de orquestração

- **FR-001**: Cada tenant DEVE ter exatamente um agente do tipo `orchestrator` criado automaticamente no provisionamento e não pode ser excluído nem duplicado.
- **FR-002**: O agente `orchestrator` DEVE ser o único a receber a primeira mensagem de qualquer conversa.
- **FR-003**: Qualquer agente (Orchestrator ou sub-agente) DEVE poder transferir o turno para outro agente ativo do mesmo tenant via `handoff_to_agent`.
- **FR-004**: Apenas sub-agentes com `is_active = true` DEVEM aparecer na lista enviada ao Orchestrator a cada turno.
- **FR-005**: O sistema DEVE manter um único thread persistente por conversa, compartilhado por todos os agentes que atuarem nela.
- **FR-006**: O handoff entre agentes NÃO DEVE reiniciar o thread nem reenviar o histórico — o contexto é preservado integralmente.

#### Configuração e identidade dos agentes

- **FR-007**: O tenant admin DEVE poder editar o nome, prompt e modelo do Orchestrator; nunca seu `type`.
- **FR-008**: O tenant admin DEVE poder criar, editar, ativar/desativar sub-agentes definindo: nome, descritivo curto (≤ 300 caracteres), prompt completo, modelo OpenAI, departamento vinculado e status.
- **FR-009**: Sub-agente DEVE obrigatoriamente referenciar um departamento existente do tenant; Orchestrator NUNCA tem departamento vinculado.
- **FR-010**: Exclusão de sub-agente DEVE ser sempre lógica (`is_active = false` + `deleted_at` preenchido) quando houver histórico de conversas vinculado.
- **FR-011**: O nome de exibição do agente é decisão do tenant — o sistema NÃO obriga a revelar a natureza de IA ao cliente; o tenant assume responsabilidade legal/regulatória pela decisão.
- **FR-012**: O sistema DEVE substituir variáveis no prompt (`{{company_name}}`, `{{department_name}}`, `{{attendant_name}}`) antes de enviar à IA, mesmo no playground.
- **FR-031**: O prompt-base do Orchestrator DEVE ser gerado no provisionamento do tenant a partir do template global definido pelo SaaS Admin (ver Spec 003); o tenant edita sobre esse template, sem possibilidade de reverter ao template global após edição.
- **FR-032**: Ao desativar um sub-agente, o sistema DEVE concluir as execuções de IA já em andamento naquele agente sem interrompê-las e DEVE rotear todas as mensagens subsequentes da mesma conversa para o Orchestrator (que decidirá em seguida o próximo passo).

#### Transbordo para humano

- **FR-013**: O sistema DEVE detectar palavras-chave explícitas de transbordo do cliente (lista pré-definida em PT-BR cobrindo "atendente", "humano", "gerente", "responsável", "quero falar com alguém") e injetar instrução de transbordo no contexto antes de chamar a IA.
- **FR-014**: Quando um agente chama `transfer_to_human(department_id, reason)`, o sistema DEVE criar um ticket no departamento informado com todo o histórico da conversa e o motivo registrado.
- **FR-015**: Após o transbordo, a IA NÃO DEVE processar nenhuma nova mensagem da conversa; mensagens recebidas antes da atribuição recebem resposta automática padrão e ficam preservadas no histórico do ticket.
- **FR-016**: Se o agente que aciona transbordo não tiver departamento vinculado (Orchestrator), o sistema DEVE direcionar o ticket para o departamento padrão do tenant.
- **FR-017**: Se nenhum agente conseguir resolver, o Orchestrator DEVE acionar `transfer_to_human` em vez de deixar o cliente sem resposta.
- **FR-033**: Ao executar `transfer_to_human`, o agente DEVE enviar ao cliente, no mesmo turno, mensagem de transferência no formato _"Vou transferir você para nossa equipe de [Nome do Departamento]. Aguarde um momento."_ — o nome do departamento é resolvido pelo backend a partir do `department_id` informado na tool call.

#### Resiliência e falha de API

- **FR-018**: Em falha de timeout/5xx/rate-limit da OpenAI, o sistema DEVE tentar novamente uma única vez após 3 segundos.
- **FR-019**: Em falha de autenticação (401/403) da OpenAI, o sistema NÃO DEVE retentar; deve acionar transbordo imediato.
- **FR-020**: Após o esgotamento da retentativa, o sistema DEVE acionar `transfer_to_human` com motivo "Falha técnica no agente de IA" e enviar ao cliente a mensagem padrão de instabilidade no formato _"Estamos com uma instabilidade técnica no momento. Vou transferir você para um de nossos atendentes."_ — emitida pelo sistema (não pelo agente, que falhou).
- **FR-021**: Toda falha de API DEVE ser registrada em log de atividade do agente com tipo de erro, status, mensagem e identificadores da conversa/agente.

#### Configurações avançadas

- **FR-022**: O tenant admin DEVE poder configurar `context_window_messages` no intervalo `[5, 100]` (default 20); fora do intervalo o sistema rejeita.
- **FR-023**: O sistema DEVE respeitar o `context_window_messages` do tenant ao montar o contexto enviado à IA, enviando exatamente as últimas N mensagens da conversa.
- **FR-024**: O tenant admin DEVE poder cadastrar uma lista própria de modelos disponíveis (`available_models`); se vazia, o sistema usa a lista global.
- **FR-025**: O tenant admin DEVE poder cadastrar/remover uma chave OpenAI própria; quando presente, ela tem prioridade sobre a chave global do sistema; quando ausente, o sistema usa a chave global.

#### Playground

- **FR-026**: A tela de edição de qualquer agente DEVE oferecer um playground que envia mensagens de teste sem criar conversa real, ticket ou histórico permanente do cliente.
- **FR-027**: O thread do playground DEVE ser temporário — descartado ao sair da tela ou ao recarregar a sessão de teste.

#### Integração com OpenAI Assistants

- **FR-028**: Cada agente DEVE corresponder a um Assistant na OpenAI; o identificador externo DEVE ser persistido no registro do agente.
- **FR-029**: Ao criar/editar prompt ou modelo de um agente, o sistema DEVE atualizar o Assistant correspondente na OpenAI; ao ativar um agente sem Assistant cadastrado, o sistema DEVE criar o Assistant antes de prosseguir.

#### Observabilidade

- **FR-030**: Cada execução de agente DEVE produzir um documento em log de atividade com: tenant, conversa, agente, ação realizada (responder, handoff, transbordo, erro), tokens consumidos (input/output), modelo, latência e identificadores de destino quando aplicável.

### Key Entities

- **Agente de IA (`ai_agents`)**: Representa um agente (Orchestrator ou sub-agente) configurável por tenant. Atributos: id, type (`orchestrator` | `sub_agent`), name (nome de exibição), short_description (texto que orienta o Orchestrator a rotear), prompt (instruções completas), model (modelo OpenAI), department_id (obrigatório para sub-agentes; nulo para Orchestrator), openai_assistant_id (id externo do Assistant), is_active, created_by, created_at, updated_at, deleted_at. Restrição: exatamente 1 agente do tipo `orchestrator` por tenant.
- **Configuração de IA do Tenant (`ai_settings`)**: 1:1 com tenant. Atributos: tenant_id, context_window_messages (5..100, default 20), available_models (lista opcional). Criada no provisionamento.
- **Conversa (`conversations`)** *(extensão de entidade pré-existente em Live Chat/WhatsApp)*: Recebe novos campos `openai_thread_id` (id do thread persistente) e `current_agent_id` (FK opcional para o agente ativo no momento; nulo quando o controle é humano).
- **Tenant** *(extensão da Spec 003)*: Recebe `default_department_id` (departamento padrão para tickets criados via transbordo de agentes sem departamento) e `openai_api_key` (credencial própria, opcional, com prioridade sobre a chave global).
- **Log de Atividade do Agente (`agent_activity_logs`)**: Documento por execução com tenant_slug, conversation_id, agent_id, agent_name, agent_type, action, input_tokens, output_tokens, model, latency_ms, handoff_target_agent_id, handoff_target_department_id, error_details (quando aplicável), timestamp. Armazenado em coleção segregada por tenant.
- **Tool calls disponíveis aos agentes**: `handoff_to_agent(agent_id, reason)`, `transfer_to_human(department_id, reason)`, `check_availability(professional_id, date)` *(detalhamento na Spec de Agenda)*, `create_appointment(professional_id, datetime, client_name, client_phone)` *(detalhamento na Spec de Agenda)*.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% dos tenants recém-provisionados possuem exatamente um Orchestrator funcional na primeira mensagem recebida — verificável imediatamente após o provisionamento.
- **SC-002**: 95% das mensagens de cliente recebem a primeira resposta da IA em até 5 segundos sob carga nominal.
- **SC-003**: 100% dos transbordos por palavra-chave ("atendente", "humano", etc.) resultam em criação de ticket no departamento correto, mensuráveis em testes de aceitação.
- **SC-004**: 0 mensagens são processadas pela IA após o transbordo na mesma conversa — verificável via log de atividade (zero registros de execução de agente após o evento de transbordo).
- **SC-005**: Em falha simulada da OpenAI (5xx/timeout), 100% das conversas são transbordadas para humano em até 10 segundos (3s do retry + janela de criação do ticket).
- **SC-006**: O thread da conversa é reutilizado em 100% das mensagens subsequentes da mesma conversa — sem reenvio de histórico — comprovado por contagem de tokens enviados na 2ª e 3ª mensagens significativamente menor que na 1ª.
- **SC-007**: Em handoff entre agentes, o agente destino tem acesso integral ao histórico da conversa — validável pedindo ao agente destino para resumir conteúdo trocado antes do handoff.
- **SC-008**: Tenant admin consegue criar, ativar, testar e publicar um sub-agente novo em menos de 5 minutos.
- **SC-009**: Em uso real, taxa de transbordos automáticos por falha técnica fica abaixo de 1% das conversas em janelas de 24h estáveis.
- **SC-010**: A configuração de chave OpenAI própria do tenant é detectada e aplicada em 100% das execuções subsequentes ao cadastro — sem necessidade de reinicialização do sistema.
- **SC-011**: Sub-agentes com histórico vinculado retornam erro de validação ao tentativa de exclusão física e oferecem desativação lógica como caminho alternativo (verificável em testes).
- **SC-012**: O playground não produz nenhum registro persistente de conversa, ticket ou histórico de cliente — auditável buscando o conteúdo da mensagem de teste em qualquer entidade persistente.

## Assumptions

- O módulo de Live Chat (Spec futura) e WhatsApp (Spec futura) entregam mensagens à fila do tenant no formato esperado pelo orquestrador.
- O módulo de Tickets (Spec futura) expõe contrato para criação de ticket com histórico anexado via chamada interna; este spec assume esse contrato e detalhará integração na fase de plano.
- O módulo de Departamentos/Atendentes (Spec 005) já está implementado: departamentos existem, têm `id` válido e a entidade `tenants` ganhará o campo `default_department_id` referenciando um departamento existente do próprio tenant.
- A entidade `tenants` (Spec 003) será estendida com `default_department_id` e `openai_api_key`; a alteração será feita como complemento desta spec ou como ADR vinculada ao plano.
- Modelo padrão é `gpt-4o`; tenants podem optar por modelos compatíveis com a OpenAI Assistants API (decisão do operador SaaS sobre quais modelos liberar globalmente).
- Janela de contexto default de 20 mensagens é suficiente para a maioria dos casos do MVP; tenants com necessidades específicas ajustam em Configurações Avançadas.
- A lista de palavras-chave de transbordo é estática em PT-BR no MVP, com possibilidade futura de internacionalização e/ou customização por tenant.
- Não há limite global rígido de número de sub-agentes por tenant no MVP; será observado em produção e regulamentado se necessário.
- A entrega de mensagens da IA ao cliente é feita pelos canais já existentes (WebSocket para Live Chat, API da Meta para WhatsApp); este spec não redefine esse caminho.
- Logs de atividade são gravados em armazenamento de eventos segregado por tenant (escolha de tecnologia detalhada no plano).
- Threads e Assistants criados na OpenAI permanecem ativos enquanto a conversa estiver `open`; políticas de retenção/limpeza de longo prazo serão tratadas em spec futura de observabilidade/custos.
