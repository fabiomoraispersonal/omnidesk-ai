# Feature Specification: Tickets / CRM (Pipeline Kanban)

**Feature Branch**: `009-tickets-crm`
**Created**: 2026-05-11
**Status**: Draft
**Input**: User description: "Spec 09 — Tickets / CRM: módulo central do CRM que formaliza o atendimento em tickets, criados automaticamente quando a IA transfere para humano ou manualmente por atendente. Cada ticket concentra histórico da conversa, dados do cliente, anotações internas, SLA com pausa em `waiting_client` e ciclo de vida até resolução. Exibido em pipeline Kanban por departamento. Cobre: identificação por protocolo `TK-YYYYMMDD-XXXXX`, ciclo de status (new → in_progress → waiting_client → resolved/cancelled), atribuição round-robin com responsável único, transferência entre atendentes/departamentos com recálculo de SLA, deduplicação automática de contatos por e-mail/telefone, perfil de contato com histórico paginado, filtros e busca full-text, anotações internas privativas, configuração visual do pipeline (renomear/reordenar/colorir colunas)."

## User Scenarios & Testing *(mandatory)*

> Atores principais: **Cliente** (origem da conversa via Live Chat ou WhatsApp), **Agente Orchestrator** (Spec 006, decide transbordo), **Atendente** (`tenant_attendant`, atua nos tickets do seu departamento), **Supervisor** (`tenant_admin` ou role com visão ampliada, vê todos os tickets), **Sistema** (gera protocolo, calcula SLA, faz round-robin, emite eventos).

### User Story 1 — Abertura automática de ticket por transbordo da IA (Priority: P1) 🎯 MVP

Um cliente conversa pelo Live Chat ou WhatsApp. A IA decide transferir para humano (frustração, palavra-chave ou `transfer_to_human`). O sistema cria imediatamente um ticket: gera protocolo único do dia (`TK-YYYYMMDD-XXXXX`), associa à conversa de origem, vincula ao contato (se identificado), define `status = new`, calcula prazos de SLA com base nas metas do departamento, e tenta atribuir round-robin a um atendente disponível do departamento. Se houver atendente, o status vira `in_progress`; se não, o ticket fica `new` na fila. Em todos os casos, um evento é registrado no MongoDB para auditoria, o histórico completo da conversa (incluindo mensagens da IA) acompanha o ticket, e o atendente designado recebe notificação.

**Why this priority**: Sem este fluxo, o módulo não nasce — todo o resto (Kanban, SLA, transferências, anotações) depende de um ticket existir. É o gatilho que transforma "conversa com a IA" em "atendimento formal com responsável e prazo". Faz a ponte entre o canal (Live Chat/WhatsApp) e o CRM operacional.

**Independent Test**: Em um tenant com departamento Comercial configurado e um atendente online, simular conversa via widget; quando a IA chamar `transfer_to_human(department_id = comercial)`, verificar que: (a) ticket criado com protocolo no formato `TK-AAAAMMDD-NNNNN`, único no tenant; (b) `conversation_id` preenchido corretamente; (c) `status = in_progress`, `attendant_id` preenchido; (d) `sla_first_response_deadline` e `sla_resolution_deadline` calculados; (e) histórico completo da conversa acessível no detalhe do ticket; (f) evento `ticket_created` persistido em `{tenant}_ticket_events`; (g) atendente recebe notificação in-app.

**Acceptance Scenarios**:

1. **Given** conversa ativa no canal `live_chat` com contato identificado por e-mail, **When** a IA invoca tool `transfer_to_human(department_id, reason)`, **Then** o sistema cria ticket com `channel = live_chat`, `conversation_id` da conversa atual, `contact_id` populado, `subject` preenchido com as primeiras 100 chars da última mensagem da IA, `status = new` e prazos de SLA calculados a partir das metas do departamento.
2. **Given** o departamento tem atendentes online com capacidade disponível, **When** o ticket é criado, **Then** o sistema atribui via round-robin (ver Spec 005), preenche `attendant_id`, transita o status para `in_progress` automaticamente, e emite evento WebSocket `ticket.assigned` para o atendente designado.
3. **Given** o departamento não tem atendente online no momento, **When** o ticket é criado, **Then** `attendant_id` fica `null`, `status` permanece `new`, e o ticket aparece na coluna "Na Fila" do Kanban do departamento.
4. **Given** um atendente fica disponível depois (volta de pausa, faz login), **When** o sistema detecta capacidade liberada, **Then** o ticket mais antigo da fila `new` do seu departamento é atribuído automaticamente.
5. **Given** o transbordo veio do canal `whatsapp` com janela de 24h ativa, **When** o ticket é criado, **Then** o `channel` registra `whatsapp`, o ticket é vinculado à conversa WhatsApp e o atendente vê o estado da janela de sessão no painel lateral.
6. **Given** o ticket foi criado, **When** o atendente abre seu detalhe, **Then** o histórico completo da conversa é exibido em ordem cronológica — incluindo todas as mensagens da IA antes do transbordo — e nada do histórico foi truncado ou perdido (constituição Princípio II).

---

### User Story 2 — Atendente atua nos tickets pelo Kanban (Priority: P1) 🎯 MVP

O atendente abre o CRM e vê o pipeline Kanban do seu(s) departamento(s) com três colunas: **Na Fila**, **Em Andamento**, **Aguardando Cliente**. Cada card mostra protocolo, contato, canal, assunto, atendente, tempo desde a criação, badge de SLA (verde / amarelo / vermelho) e tags. O atendente clica em um card e abre a tela de detalhe com painel esquerdo (histórico + campo de resposta + anotações) e painel direito (dados do ticket editáveis + dados do contato + ações). Ele responde ao cliente pelo campo de mensagem (a resposta vai pelo canal correto: Live Chat WebSocket ou WhatsApp Graph API), pode mover o ticket para `waiting_client` clicando ou arrastando o card, e encerra clicando "Encerrar" (status → `resolved`, ticket some do Kanban). Drag-and-drop entre colunas também muda o status.

**Why this priority**: Sem a interface, o ticket criado pela US1 fica inacessível na prática. Esta história é o painel onde o atendente passa o dia. Junto com US1 forma o MVP funcional: cliente conversa → IA transfere → ticket aparece → atendente resolve.

**Independent Test**: Com 3 tickets criados (1 sem atendente, 2 atribuídos), atendente logado vê os 3 cards distribuídos corretamente nas colunas. Clica em um card, vê o histórico, digita "Posso ajudar com o agendamento", envia, confirma que o cliente recebe a mensagem pelo canal correto, arrasta o card para "Aguardando Cliente" (status muda para `waiting_client`), e depois clica "Encerrar" no painel direito — o ticket some do Kanban e fica acessível só por filtro/busca.

**Acceptance Scenarios**:

1. **Given** atendente autenticado em departamento Comercial, **When** acessa o CRM, **Then** vê pipeline Kanban com 3 colunas (Na Fila / Em Andamento / Aguardando Cliente) e apenas os tickets do(s) seu(s) departamento(s) — `tenant_attendant` nunca vê tickets de outros departamentos (Princípio I + visibilidade da seção 6.7).
2. **Given** o card do ticket está visível, **When** atendente arrasta o card de "Em Andamento" para "Aguardando Cliente", **Then** o status muda para `waiting_client`, `waiting_client_since` é preenchido, o SLA de resolução pausa e o evento `ticket.status_changed` é emitido via WebSocket.
3. **Given** ticket está em `waiting_client` e o cliente envia nova mensagem pelo canal, **When** a mensagem chega, **Then** o status volta automaticamente para `in_progress`, o tempo pausado é acumulado em `sla_paused_duration_minutes`, `waiting_client_since` zera, e o evento WebSocket é emitido para atualizar o Kanban em tempo real.
4. **Given** atendente abre o detalhe de um ticket `in_progress` e nunca respondeu, **When** envia a primeira mensagem pelo campo de resposta, **Then** `first_response_at` é preenchido e o SLA de primeira resposta é marcado como atendido (não pausa em `waiting_client`).
5. **Given** ticket aberto na tela de detalhe, **When** atendente clica "Encerrar", **Then** confirmação é solicitada; ao confirmar, `status → resolved`, `resolved_at` preenchido, conversa vinculada também marcada como resolvida, evento `status_changed` registrado, ticket sai do Kanban e (se opt-in ativo) cliente recebe notificação por Spec 010.
6. **Given** ticket arrastado para uma coluna que mapeia para status inválido (ex.: tentar mover diretamente para `resolved` que não tem coluna), **When** o drop ocorre, **Then** o sistema rejeita visualmente (card retorna à coluna de origem) e exibe mensagem orientativa "Use o botão Encerrar para concluir o atendimento".
7. **Given** tickets `resolved` e `cancelled`, **When** o Kanban é renderizado, **Then** esses tickets não aparecem em nenhuma coluna — acessíveis somente por filtro/busca.

---

### User Story 3 — SLA com pausa, badge visual e alerta de aproximação de prazo (Priority: P2)

Cada ticket nasce com dois prazos calculados a partir das metas do departamento: **primeira resposta** (não pausa) e **resolução** (pausa em `waiting_client`). O card do Kanban exibe um badge visual: 🟢 verde dentro do prazo, 🟡 amarelo quando >80% do tempo foi consumido, 🔴 vermelho quando expirado. Ao atingir 80% do prazo, o sistema emite evento WebSocket `ticket.sla_warning` para todos os atendentes/supervisores que enxergam o ticket — possibilitando notificação visual ou sonora no CRM. Ao expirar, emite `ticket.sla_breached` e registra evento de auditoria no MongoDB. O contador exibido na tela de detalhe e no card considera o tempo pausado (resolução = `sla_resolution_deadline + sla_paused_duration_minutes` efetivos).

**Why this priority**: SLA é a métrica que diferencia CRM operacional de "lista de e-mails". Mas o ticket funciona sem ele — por isso P2. Ativá-lo dá observabilidade do desempenho do atendimento e cria pressão saudável para resolver. Sem essa história, o tenant percebe atrasos só depois que o cliente reclama.

**Independent Test**: Configurar departamento com SLA de resolução = 1h. Criar ticket. Aguardar 48min (80%): verificar que o badge do card vira amarelo e o evento `ticket.sla_warning` é recebido no WebSocket. Mover o ticket para `waiting_client` antes de expirar. Esperar 30min nesse estado e voltar para `in_progress` (cliente responde). Confirmar que `sla_paused_duration_minutes` registra os 30min e o contador agora exibe um prazo efetivo expandido. Deixar o tempo restante esgotar: badge fica vermelho, `ticket.sla_breached` emitido, evento `sla_breached` persistido no MongoDB.

**Acceptance Scenarios**:

1. **Given** departamento com `sla_first_response_minutes = 15` e `sla_resolution_minutes = 60`, **When** o ticket é criado e atribuído, **Then** `sla_first_response_deadline = atribuição + 15min` e `sla_resolution_deadline = criação + 60min` são preenchidos.
2. **Given** ticket atingiu 80% do prazo de primeira resposta sem `first_response_at`, **When** o monitor de SLA detecta o limiar, **Then** o badge do card vira amarelo (no front, recalculado a cada tick) e o evento WebSocket `ticket.sla_warning` é emitido com `type = first_response`.
3. **Given** ticket em `in_progress` ultrapassou `sla_first_response_deadline` sem resposta humana, **When** o monitor detecta a expiração, **Then** o badge vira vermelho, o evento `ticket.sla_breached` é emitido, e um evento `sla_breached` é persistido em `{tenant}_ticket_events` para auditoria.
4. **Given** ticket entra em `waiting_client` às 10:00 e sai às 10:30 (cliente responde), **When** a transição ocorre, **Then** `sla_paused_duration_minutes` recebe `+30`, `waiting_client_since` volta a `null`, e o prazo efetivo de resolução exibido = `sla_resolution_deadline + sla_paused_duration_minutes`.
5. **Given** ticket está em `waiting_client` no momento do encerramento, **When** atendente clica "Encerrar", **Then** o sistema calcula a pausa final (de `waiting_client_since` até agora), soma a `sla_paused_duration_minutes`, registra `resolved_at` e gera evento — mesmo que o SLA total tenha sido cumprido ou não.
6. **Given** SLA de primeira resposta já cumprido (`first_response_at` preenchido), **When** o ticket continua aberto, **Then** o monitor de SLA ignora o prazo de primeira resposta e foca apenas no prazo de resolução até o encerramento.

---

### User Story 4 — Transferência de ticket entre atendentes ou departamentos (Priority: P2)

O atendente está atendendo um ticket mas percebe que o caso é de outro departamento (ex: Comercial → Financeiro) ou de outro colega especialista. Ele clica em "Transferir" no painel direito, escolhe o destino (outro atendente do mesmo ou outro departamento, ou um departamento sem atendente específico), opcionalmente escreve uma nota interna de contexto, e confirma. O histórico completo acompanha — incluindo mensagens da IA, mensagens entre atendente e cliente, e anotações internas. Se a transferência foi para outro departamento, o SLA é recalculado com base nas metas do novo departamento. O atendente anterior é notificado de que perdeu a responsabilidade; o novo atendente (ou todo o departamento se foi para a fila) recebe notificação de novo ticket.

**Why this priority**: Cobre o caso real de "esse não é o meu caso, preciso passar adiante". Sem transferência, o atendente que pegou o ticket precisaria resolver fora do sistema (chat interno, e-mail) — quebrando rastreabilidade. É P2 porque o caminho feliz (US1+US2) funciona sem isso, mas operacionalmente é necessário antes da produção real.

**Independent Test**: Atendente Maria (Comercial) recebe ticket de queixa financeira. Clica "Transferir → Departamento Financeiro". Verifica que: (a) `attendant_id` volta para `null` (ou é atribuído a um atendente do Financeiro via round-robin se houver capacidade); (b) `department_id` muda para Financeiro; (c) `sla_first_response_deadline` e `sla_resolution_deadline` recalculados com base nas metas do Financeiro; (d) o ticket some do Kanban do Comercial e aparece no Kanban do Financeiro; (e) atendente Maria recebe notificação de "ticket transferido"; (f) histórico completo (incluindo mensagens com Maria) está preservado; (g) evento `transferred` registrado no MongoDB.

**Acceptance Scenarios**:

1. **Given** atendente com ticket atribuído, **When** clica "Transferir" e escolhe outro atendente do mesmo departamento, **Then** `attendant_id` muda para o destino, evento `transferred` é registrado com `from_attendant_id` e `to_attendant_id`, o atendente anterior é notificado, e o novo atendente recebe notificação `ticket.assigned`.
2. **Given** transferência para outro departamento, **When** confirmada, **Then** `department_id` muda, `attendant_id` é zerado (entra na fila do novo depto ou recebe nova atribuição round-robin), `sla_first_response_deadline` e `sla_resolution_deadline` são recalculados com as metas do novo departamento (sem somar prazo anterior), e o evento `transferred` registra `from_department_id` + `to_department_id`.
3. **Given** transferência para "departamento sem atendente específico" (fila), **When** confirmada, **Then** `attendant_id` fica `null` e o ticket aparece na coluna "Na Fila" do novo departamento até que um atendente disponível seja encontrado.
4. **Given** atendente escreve nota de contexto durante a transferência, **When** confirma, **Then** uma `ticket_note` é criada automaticamente com `attendant_id` do remetente e `content` da nota, visível para o destinatário ao abrir o ticket.
5. **Given** ticket transferido para um departamento sem nenhum atendente online, **When** confirmado, **Then** o ticket fica `new` na fila com `attendant_id = null` e a transferência é registrada — o sistema **não** rejeita a operação por falta de atendente.
6. **Given** ticket já em `resolved` ou `cancelled`, **When** alguém tenta transferir, **Then** o sistema bloqueia com erro semântico (`TICKET_ALREADY_CLOSED`) e nenhuma mudança ocorre.

---

### User Story 5 — Criação manual de ticket por atendente (Priority: P2)

Nem todo atendimento começa pelo canal digital. Um cliente liga no telefone, e o atendente precisa abrir um ticket para registrar o contato — sem conversa de Live Chat ou WhatsApp associada. O atendente clica em "+ Novo Ticket", busca o contato (por nome, e-mail ou telefone) ou cria um novo, preenche departamento, assunto, prioridade e tags, e cria o ticket. O `channel` é registrado como `manual`. O ticket aparece imediatamente no Kanban do departamento escolhido e segue o mesmo ciclo de vida (`new` → `in_progress` ao atribuir, etc.).

**Why this priority**: Cobre canais offline (telefone, presencial) que não passam pelo Live Chat/WhatsApp. Sem isso, atendimentos por outros meios não entram no funil — o operador acaba mantendo planilhas paralelas. É P2 porque depende do MVP digital estar funcionando primeiro.

**Independent Test**: Atendente clica "+ Novo Ticket". No modal: busca contato "joão silva", encontra ou cria novo (preenchendo telefone (11)99999-9999). Seleciona departamento Comercial, assunto "Solicitação por telefone", prioridade Alta, tag "telefone". Clica "Criar". Confirma que o ticket aparece no Kanban Comercial com `channel = manual`, sem `conversation_id`, com os dados preenchidos.

**Acceptance Scenarios**:

1. **Given** atendente clica "+ Novo Ticket", **When** o modal abre, **Then** exibe campos: busca de contato (autocomplete por nome/e-mail/telefone), departamento (dropdown só com depts que o atendente pode ver), assunto, prioridade (low/normal/high/urgent, default normal), tags.
2. **Given** o atendente não encontra o contato, **When** clica "Criar novo contato", **Then** o modal expande com campos de contato (nome, e-mail, telefone, observações) e ao salvar cria o contato e o vincula ao ticket em uma única operação.
3. **Given** o atendente preenche e-mail de um contato existente, **When** o sistema busca por deduplicação, **Then** sugere o contato encontrado em vez de duplicar — confirmando vinculação ao registro existente.
4. **Given** ticket criado manualmente sem `conversation_id`, **When** salvo, **Then** `channel = manual`, `conversation_id = null`, todos os demais campos válidos, e o ticket aparece no Kanban do departamento escolhido com `status = new` (se sem atendente) ou `in_progress` (se atribuído na criação).
5. **Given** o atendente quer registrar atendimento já iniciado por telefone, **When** preenche e ativa "Atribuir a mim", **Then** `attendant_id` é o próprio atendente, `status = in_progress`, e o ticket entra direto na coluna "Em Andamento".
6. **Given** o atendente é `tenant_attendant`, **When** abre o dropdown de departamentos, **Then** vê apenas departamentos aos quais pertence (Princípio I + visibilidade); `tenant_admin` e `supervisor` veem todos.

---

### User Story 6 — Perfil do contato com deduplicação e histórico paginado (Priority: P2)

Quando um visitante se identifica (informa e-mail ou telefone), o sistema busca contatos existentes: e-mail é chave primária de deduplicação, telefone normalizado é a chave secundária. Se encontrar, vincula a conversa/ticket ao contato existente e atualiza campos vazios com os novos dados. Se não encontrar, cria contato novo. O atendente, na tela de detalhe do ticket, clica no nome do contato e abre o **perfil do contato**: dados editáveis (nome, e-mail, telefone, observações), lista paginada (20/página) de tickets anteriores (mais recente primeiro) e lista paginada de conversas anteriores. Pode abrir qualquer ticket ou conversa antiga com histórico completo.

**Why this priority**: O atendimento de qualidade exige contexto histórico — saber se o cliente já interagiu antes, sobre o quê, e quem o atendeu. Sem deduplicação automática, o sistema cria contatos duplicados a cada interação e o histórico fica fragmentado. É P2 porque o MVP funciona sem (cada ticket é autocontido), mas a experiência operacional sofre.

**Independent Test**: Cliente já tem contato cadastrado (`joao@email.com`, telefone `(11) 99999-9999`). Em um novo chat, ele se identifica como "João Souza" com mesmo e-mail. Confirmar que: (a) o sistema reutiliza o contato existente (não cria duplicata); (b) `name` é atualizado para "João Souza" (estava vazio); (c) o ticket criado por transbordo é vinculado a esse `contact_id`; (d) clicando no nome do contato no detalhe do ticket, abre perfil com tickets anteriores e conversas anteriores paginadas em ordem reversa cronológica.

**Acceptance Scenarios**:

1. **Given** existe contato com `email = joao@email.com`, **When** um visitante novo se identifica com o mesmo e-mail, **Then** o sistema vincula o contato existente, atualiza campos vazios com os novos valores, adiciona o canal atual em `source_channels` (se ainda não estiver), e **não** cria contato duplicado.
2. **Given** visitante fornece apenas telefone `(11) 99999-9999`, **When** o sistema normaliza para `11999999999` e busca, **Then** encontra contato com `phone_normalized = 11999999999`, vincula, e atualiza dados conforme regras de deduplicação.
3. **Given** visitante fornece e-mail e telefone, **When** o sistema busca, **Then** prioriza match por e-mail (P1); se não encontrar por e-mail, busca por telefone normalizado (P2).
4. **Given** atendente abre detalhe do ticket, **When** clica no nome do contato no painel direito, **Then** navega para `/contacts/{id}` que exibe dados editáveis, abas "Tickets" e "Conversas" com paginação 20/página, mais recentes primeiro.
5. **Given** contato tem 47 tickets históricos, **When** atendente abre a aba "Tickets", **Then** vê página 1 com 20 itens (mais recentes); paginação permite navegar até a página 3 (com 7 itens).
6. **Given** atendente clica em um ticket antigo no perfil do contato, **When** o ticket abre, **Then** todo o histórico da conversa associada (se houver) é exibido — não há truncamento de dados.

---

### User Story 7 — Filtros e busca full-text para localizar tickets (Priority: P3)

Em volume real, o pipeline Kanban fica com dezenas de cards e o atendente/supervisor precisa filtrar rapidamente: tickets do dia, com tag específica, com SLA estourado, atribuídos a alguém específico. Acima do Kanban, há um painel de filtros (departamento, atendente, canal, prioridade, tag, período de criação) e um campo de busca full-text que pesquisa em: protocolo, assunto, nome do contato e conteúdo das mensagens. Quando há busca ativa, o resultado é exibido em lista (não Kanban), porque o Kanban perderia sentido com filtros muito restritivos.

**Why this priority**: A partir de ~30 tickets ativos, o Kanban vira ruído. Mas, em volume baixo (que é onde tenants começam), a visão sem filtro é suficiente. Por isso P3 — o valor cresce com o tempo.

**Independent Test**: Em um tenant com 50 tickets distribuídos por estados e atendentes, supervisor abre o Kanban, aplica filtro "Período = Esta semana" + "Atendente = Maria" → vê apenas tickets de Maria desta semana. Limpa filtros, busca "TK-20260503" → vê o ticket exato em modo lista. Busca "agendamento sábado" → recupera todos os tickets cujo assunto, conteúdo de mensagem ou nome do contato contenha esses termos.

**Acceptance Scenarios**:

1. **Given** Kanban com filtros aplicados (departamento + canal), **When** o resultado tem 0 tickets, **Then** o sistema exibe estado vazio orientativo "Nenhum ticket encontrado com os filtros aplicados — limpe os filtros para ver todos".
2. **Given** atendente digita texto na busca full-text, **When** o sistema processa, **Then** pesquisa em `protocol`, `subject`, `contact.name` e conteúdo das mensagens da conversa, retornando resultados ranqueados por relevância e mais recentes primeiro.
3. **Given** atendente busca o protocolo exato `TK-20260503-00042`, **When** envia, **Then** o resultado retorna o ticket único correspondente (match exato tem prioridade).
4. **Given** o resultado da busca contém >20 tickets, **When** exibido, **Then** apresenta em formato lista paginada (não Kanban), 20 itens por página, mais recentes primeiro.
5. **Given** atendente `tenant_attendant` aplica filtros, **When** o sistema processa, **Then** os filtros operam apenas dentro do escopo dos tickets que o atendente tem permissão para ver (departamentos aos quais pertence).
6. **Given** filtro "Período = Personalizado" selecionado, **When** atendente escolhe intervalo de datas, **Then** o filtro aplica `created_at BETWEEN start AND end` no fuso do tenant.

---

### User Story 8 — Anotações internas privativas no ticket (Priority: P3)

Em casos complexos, o atendente precisa registrar contexto que o cliente **não** deve ver: "cliente já solicitou desconto antes — verificar com gerência", "histórico de chargeback em maio", "encaminhar para depto Jurídico se renovar contato". Essas anotações internas ficam em uma seção colapsável no painel esquerdo da tela de detalhe, separada visualmente do histórico da conversa, e **nunca** são enviadas ao cliente por nenhum canal. Múltiplos atendentes (em casos de transferência) podem ver e adicionar notas. Cada nota registra autor e timestamp.

**Why this priority**: É útil mas opcional. O atendente pode usar mensagens diretas ou chats internos como workaround. É P3 porque o MVP funciona sem; mas com volume, organização interna dentro do próprio ticket ganha valor.

**Independent Test**: Atendente abre ticket, expande a seção "Anotações internas", escreve "cliente é VIP — atender prioritariamente", clica "Adicionar nota". Confirma que: (a) a nota aparece com autor e timestamp; (b) outro atendente do mesmo departamento, ao abrir o ticket, vê a mesma nota; (c) o cliente, no widget de Live Chat ou no WhatsApp, **nunca** recebeu nenhuma indicação dessa nota (test: simular envio e verificar que nenhuma mensagem foi disparada pelos canais).

**Acceptance Scenarios**:

1. **Given** atendente abre detalhe do ticket, **When** clica para expandir "Anotações internas", **Then** vê seção separada visualmente do histórico (cor de fundo distinta, etiqueta clara "🔒 Anotações internas — não visíveis ao cliente"), com lista de notas existentes em ordem cronológica e campo para nova nota.
2. **Given** atendente escreve uma nota e clica "Adicionar", **When** salva, **Then** o `content` é persistido com `attendant_id` e `created_at`, evento `note_added` é registrado em `{tenant}_ticket_events`, e a nota aparece imediatamente na seção sem disparar nenhuma mensagem pelos canais do cliente.
3. **Given** ticket transferido entre atendentes, **When** o novo atendente abre o ticket, **Then** vê todas as notas internas anteriores (com autor e timestamp), incluindo a nota automática gerada na transferência (se aplicável).
4. **Given** cliente envia mensagem pelo canal, **When** processada, **Then** o sistema não inclui nenhuma anotação interna em nenhum prompt da IA nem em nenhuma resposta enviada ao cliente — anotações ficam estritamente no domínio interno do CRM.
5. **Given** `tenant_attendant` sem acesso ao departamento do ticket, **When** tenta acessar diretamente `/api/tickets/{id}/notes`, **Then** o sistema retorna `403 Forbidden`.

---

### User Story 9 — Configuração visual do pipeline Kanban (Priority: P3)

Cada departamento tem um pipeline com 3 colunas mapeadas para `new`, `in_progress`, `waiting_client`. O tenant admin pode personalizar a aparência do Kanban: renomear as colunas ("Na Fila" → "Aguardando atribuição"), reordenar (mas sem mudar o que cada coluna significa) e atribuir cor de destaque. O sistema **não permite** criar novas colunas, remover colunas existentes, nem mapear duas colunas para o mesmo status — preservando a consistência do ciclo de vida do ticket.

**Why this priority**: É uma personalização cosmética que ajuda o tenant a falar a língua interna da equipe. O MVP funciona com os nomes padrão. É P3.

**Independent Test**: `tenant_admin` acessa configuração do pipeline do departamento Comercial. Renomeia "Na Fila" para "Aguardando", reordena posicionando "Aguardando Cliente" como primeira, escolhe cor `#7A9E7E` para "Em Andamento", salva. Verifica que o Kanban exibe as colunas com novos nomes, na nova ordem, com a cor configurada. Tenta criar uma coluna duplicada com `status_mapping = in_progress` → sistema rejeita com erro `DUPLICATE_STATUS_MAPPING`.

**Acceptance Scenarios**:

1. **Given** tenant é provisionado, **When** um departamento é criado, **Then** o sistema cria automaticamente um `pipeline` 1:1 com o departamento, com 3 colunas: "Na Fila" (status `new`, order 1), "Em Andamento" (status `in_progress`, order 2), "Aguardando Cliente" (status `waiting_client`, order 3).
2. **Given** `tenant_admin` edita pipeline, **When** renomeia uma coluna, **Then** o nome muda no Kanban sem alterar o `status_mapping` (renomeação é puramente visual).
3. **Given** `tenant_admin` reordena colunas via drag-and-drop, **When** salva, **Then** os valores de `order` são atualizados e o Kanban renderiza na nova sequência.
4. **Given** `tenant_admin` tenta criar uma quarta coluna ou duplicar o `status_mapping`, **When** confirma, **Then** o sistema rejeita com erro semântico (`DUPLICATE_STATUS_MAPPING` ou `INVALID_COLUMN_COUNT`) e nenhuma mudança é persistida.
5. **Given** `tenant_admin` define cor `#7A9E7E` em uma coluna, **When** o Kanban é renderizado, **Then** a coluna recebe destaque visual com a cor escolhida (borda ou cabeçalho).
6. **Given** `tenant_attendant` autenticado, **When** tenta acessar a configuração do pipeline, **Then** o sistema retorna `403 Forbidden` — apenas `tenant_admin` ou `supervisor` configuram pipelines.

---

### Edge Cases

- **Conflito de protocolo concorrente**: dois tickets criados no mesmo milissegundo podem competir pelo mesmo sufixo `XXXXX` do dia. O gerador de protocolo MUST ser atômico/concorrente seguro (lock pessimista, sequência postgres, ou sorteio com retry) e nunca falhar silenciosamente.
- **Atendente fica offline com tickets atribuídos**: tickets não são re-atribuídos automaticamente para outro atendente — permanecem com o `attendant_id` original. O supervisor decide quando intervir (via transferência manual). Exibir indicador visual no card quando o atendente designado estiver offline há > X minutos.
- **Transferência para um departamento sem atendente disponível**: o ticket entra na fila do novo departamento (`attendant_id = null`, `status = new`); transferência sempre é aceita.
- **Cliente envia mensagem em ticket `cancelled`**: o ticket não é reaberto. O sistema cria nova conversa e novo ticket (mesma regra de tickets `resolved`).
- **Ticket sem `conversation_id` (manual)** entra em `waiting_client`: cliente não pode "responder por mensagem", então a transição volta a `in_progress` precisa ser manual pelo atendente. Permitir ação explícita "Retomar atendimento".
- **Encerramento de ticket com SLA já estourado**: o ticket pode ser encerrado normalmente; o evento `sla_breached` já registrado fica visível no histórico para análise.
- **Tag muito longa ou caracteres especiais**: o sistema deve sanitizar/normalizar tags (lowercase, sem espaços extremos, comprimento máximo) para evitar inconsistência visual.
- **Contato editado por dois atendentes simultaneamente**: prevenir lost-update com versionamento (`updated_at` ou `version`) e exibir conflito ao salvar.
- **Departamento removido com tickets ativos**: bloqueado (já regra da Spec 005); supervisor deve transferir os tickets antes.
- **Mudança de prioridade em ticket resolvido**: bloqueada — tickets `resolved` e `cancelled` ficam imutáveis exceto para visualização e adição de notas internas (audit-only).
- **Subject auto-gerado de uma mensagem com mídia (sem texto)**: usar fallback "Atendimento via {canal}" ou nome do tipo de mensagem.

## Requirements *(mandatory)*

### Functional Requirements — Identificação e Ciclo de Vida

- **FR-001**: O sistema MUST gerar protocolos de ticket no formato `TK-YYYYMMDD-XXXXX`, onde `YYYYMMDD` é a data UTC da criação e `XXXXX` é uma sequência incremental de 5 dígitos zero-padded única por tenant **e por dia**. O protocolo MUST ser imutável após criação.
- **FR-002**: O sistema MUST tratar geração de protocolo de forma concorrente-segura. Em criação simultânea de 2+ tickets no mesmo milissegundo, MUST garantir que nenhum protocolo seja duplicado.
- **FR-003**: O ticket MUST suportar exatamente 5 valores de status: `new`, `in_progress`, `waiting_client`, `resolved`, `cancelled`. Nenhum outro status é permitido.
- **FR-004**: As transições de status válidas MUST ser: `new → in_progress` (automática ao atribuir atendente), `in_progress ↔ waiting_client` (manual pelo atendente ou automática quando cliente responde), `in_progress|waiting_client → resolved` (manual), `new|in_progress|waiting_client → cancelled` (manual). Tickets em `resolved` ou `cancelled` MUST ser imutáveis e não podem retornar a estados anteriores.
- **FR-005**: Cada ticket MUST ter **exatamente um** `attendant_id` por vez. Colaboração entre atendentes MUST ocorrer via transferência (US4). `attendant_id = null` significa "na fila".

### Functional Requirements — Abertura

- **FR-006**: O sistema MUST criar ticket automaticamente quando a IA chamar `transfer_to_human`, populando `channel`, `conversation_id`, `contact_id` (se identificado), `department_id` da tool call, `subject` (primeiras 100 chars da última mensagem da IA), e `status = new`.
- **FR-007**: Após criar o ticket por transbordo, o sistema MUST tentar atribuição round-robin (Spec 005). Se um atendente for atribuído, status MUST transitar para `in_progress`; senão, permanece `new`.
- **FR-008**: O sistema MUST permitir criação manual de ticket por `tenant_attendant`, `tenant_admin` ou `supervisor`. Tickets manuais podem existir sem `conversation_id` e MUST ter `channel = manual` neste caso.
- **FR-009**: Quando um atendente fica disponível (volta de pausa, faz login), o sistema MUST verificar a fila `new` do(s) seu(s) departamento(s) e atribuir o ticket mais antigo automaticamente, respeitando `max_simultaneous_chats` (Spec 005).

### Functional Requirements — SLA

- **FR-010**: O sistema MUST calcular `sla_first_response_deadline` no momento da atribuição (`assignment_at + sla_first_response_minutes` do departamento), e `sla_resolution_deadline` no momento da criação do ticket (`created_at + sla_resolution_minutes` do departamento).
- **FR-011**: O SLA de **resolução** MUST pausar enquanto o ticket estiver em `waiting_client`. Ao entrar nesse estado, `waiting_client_since` MUST ser preenchido com timestamp atual. Ao sair (para `in_progress`), o intervalo MUST ser somado a `sla_paused_duration_minutes` e `waiting_client_since` zerado.
- **FR-012**: O SLA de **primeira resposta** MUST NOT pausar — é medido continuamente desde a atribuição até o primeiro `outgoing` enviado por atendente humano (`first_response_at`).
- **FR-013**: O prazo efetivo de resolução exibido em UI MUST ser `sla_resolution_deadline + sla_paused_duration_minutes` (com pausa em andamento, somar o tempo desde `waiting_client_since` até agora).
- **FR-014**: O sistema MUST emitir evento WebSocket `ticket.sla_warning` quando ≥80% do prazo for consumido (uma vez por tipo de SLA por ticket).
- **FR-015**: O sistema MUST emitir evento WebSocket `ticket.sla_breached` quando o prazo expirar e registrar evento `sla_breached` em `{tenant}_ticket_events` para auditoria.
- **FR-016**: Em transferência entre departamentos, o sistema MUST recalcular **ambos** os prazos de SLA (primeira resposta e resolução) com base nas metas do **novo** departamento, a partir do momento da transferência. `first_response_at`, se já preenchido, MUST ser preservado (não retroage). `sla_paused_duration_minutes` MUST ser zerado (recomeço limpo).

### Functional Requirements — Transferência e Encerramento

- **FR-017**: O sistema MUST permitir transferência para: (a) outro atendente do mesmo departamento, (b) outro atendente de outro departamento, (c) um departamento sem atendente específico (volta para fila).
- **FR-018**: A transferência MUST preservar histórico completo (mensagens da IA, mensagens com atendente, anotações internas) e registrar evento `transferred` em `{tenant}_ticket_events` com `from_*` e `to_*`.
- **FR-019**: O encerramento (`resolve`) MUST: preencher `resolved_at`, calcular pausa final de SLA se em `waiting_client`, marcar a conversa vinculada como `resolved`, e registrar evento `status_changed`.
- **FR-020**: O cancelamento (`cancel`) MUST: preencher `cancelled_at`, NÃO recalcular SLA, registrar evento `status_changed`, e impedir reabertura.
- **FR-021**: Tickets em `resolved` ou `cancelled` MUST não aparecer no Kanban; MUST ser acessíveis via filtro de período ou busca full-text. MUST não permitir transferência, mudança de status, nem mudança de prioridade — apenas leitura e adição de anotações internas (audit-only).

### Functional Requirements — Visibilidade e Permissões

- **FR-022**: `tenant_attendant` MUST ver apenas tickets dos departamentos aos quais pertence. Tentativa de acesso fora do escopo MUST retornar `403 Forbidden`.
- **FR-023**: `supervisor` e `tenant_admin` MUST ver todos os tickets do tenant.
- **FR-024**: Configuração de pipelines (renomear/reordenar colunas) MUST ser restrita a `tenant_admin`. `supervisor` pode ver, mas não editar.
- **FR-025**: Anotações internas (`ticket_notes`) MUST nunca ser enviadas ao cliente por nenhum canal. MUST nunca compor prompts da IA. MUST ser acessíveis apenas a atendentes/supervisores com acesso ao ticket.

### Functional Requirements — Contatos e Deduplicação

- **FR-026**: O sistema MUST deduplicar contatos automaticamente: prioridade 1 por `email` (match exato, case-insensitive), prioridade 2 por `phone_normalized` (dígitos apenas).
- **FR-027**: Ao identificar contato existente durante criação de visitor/ticket, o sistema MUST vincular ao contato existente, atualizar campos vazios com os novos valores não-nulos, e adicionar o canal corrente a `source_channels` se ausente.
- **FR-028**: O sistema MUST persistir `phone` no formato fornecido E `phone_normalized` (apenas dígitos) para busca; índice MUST ser criado sobre `phone_normalized`.
- **FR-029**: A tela de perfil do contato MUST listar tickets e conversas anteriores em ordem reversa cronológica, paginadas em 20 itens/página, com link para abrir cada registro com histórico completo.
- **FR-030**: O atendente MUST poder editar dados do contato (nome, e-mail, telefone, observações) na tela de perfil; alterações MUST atualizar `updated_at`.

### Functional Requirements — Pipeline e Kanban

- **FR-031**: Cada departamento MUST ter um `pipeline` 1:1 criado automaticamente no provisionamento, com 3 colunas pré-definidas: `new` → "Na Fila" (order 1), `in_progress` → "Em Andamento" (order 2), `waiting_client` → "Aguardando Cliente" (order 3).
- **FR-032**: O `status_mapping` de cada coluna MUST ser único por pipeline. Não é permitido criar/editar colunas que resultem em duas colunas com o mesmo `status_mapping`.
- **FR-033**: O sistema MUST suportar renomeação (`name`), reordenação (`order`) e atribuição de cor (`color` hex) das colunas existentes. NÃO MUST suportar criação de novas colunas nem remoção das 3 colunas-base.
- **FR-034**: Drag-and-drop de card no Kanban MUST mudar `status` do ticket conforme o `status_mapping` da coluna destino, gerando evento `status_changed`. Tentativa de drop em coluna inválida (sem mapeamento) MUST ser rejeitada visualmente sem alterar dados.

### Functional Requirements — Eventos e Auditoria

- **FR-035**: O sistema MUST registrar evento em `{tenant}_ticket_events` (MongoDB) para cada operação relevante: criação, atribuição, mudança de status, transferência, mudança de prioridade, adição de tag, adição de nota, SLA breach. Evento MUST ser imutável.
- **FR-036**: Eventos WebSocket MUST ser emitidos para: `ticket.created`, `ticket.assigned`, `ticket.status_changed`, `ticket.transferred`, `ticket.sla_warning`, `ticket.sla_breached`. Escopo de entrega: atendentes/supervisores do departamento corrente do ticket; supervisores/admins recebem para todos os tickets do tenant.

### Functional Requirements — Filtros e Busca

- **FR-037**: O Kanban MUST suportar filtros por: departamento, atendente (incluindo "sem atendente"), canal (`live_chat` / `whatsapp` / `manual`), prioridade, tag e período (hoje / esta semana / este mês / personalizado).
- **FR-038**: A busca full-text MUST pesquisar em: `protocol` (match exato prioritário), `subject`, `contact.name`, e conteúdo das mensagens da conversa vinculada. Resultados em formato lista paginada 20/página, mais recentes primeiro.
- **FR-039**: Filtros e busca MUST respeitar permissões — `tenant_attendant` busca apenas no escopo dos seus departamentos.

### Functional Requirements — Notas e Subject

- **FR-040**: O `subject` MUST ser preenchido automaticamente ao criar por transbordo com as primeiras 100 chars da última mensagem da IA. Em transbordo de mensagem sem texto (mídia pura), usar fallback "Atendimento via {canal}".
- **FR-041**: O atendente MUST poder editar `subject` a qualquer momento; alteração registra evento `subject_changed` (audit).
- **FR-042**: `ticket_notes` MUST registrar `attendant_id`, `content`, `created_at`. Notas MUST ser append-only — não há edição nem exclusão após criação.

### Key Entities

- **Ticket** (`tickets`, schema tenant): unidade de atendimento formal. Atributos principais: `id`, `protocol` (TK-YYYYMMDD-XXXXX único e imutável), `channel` (live_chat / whatsapp / manual), `status` (new / in_progress / waiting_client / resolved / cancelled), `priority` (low / normal / high / urgent), `conversation_id` (FK conversations, opcional para manual), `contact_id`, `department_id`, `attendant_id`, `tags[]`, `subject` (≤255 chars), `resolved_at`, `cancelled_at`, `first_response_at`, `sla_first_response_deadline`, `sla_resolution_deadline`, `sla_paused_duration_minutes` (default 0), `waiting_client_since`, `has_reminder_alert` (boolean, badge ⚠️ para falha de lembrete de agendamento — depende Spec 011), `created_at`, `updated_at`.
- **Ticket Note** (`ticket_notes`, schema tenant): anotação interna privativa, append-only. Atributos: `id`, `ticket_id`, `attendant_id`, `content`, `created_at`.
- **Ticket Event** (`{tenant}_ticket_events`, MongoDB): log imutável de mudanças relevantes. Atributos: `tenant_slug`, `ticket_id`, `protocol`, `event_type` (status_changed / attendant_assigned / transferred / priority_changed / tag_added / note_added / sla_breached / subject_changed), `actor_type` (attendant / system), `actor_id`, `actor_name`, `from`, `to`, `reason` (opcional), `timestamp`.
- **Pipeline** (`pipelines`, schema tenant): agrupador de colunas para um departamento. 1:1 com departamento. Atributos: `id`, `department_id`, `name` (≤100 chars), `created_at`.
- **Pipeline Column** (`pipeline_columns`, schema tenant): coluna do Kanban. Atributos: `id`, `pipeline_id`, `name` (≤100 chars), `status_mapping` (enum `new` / `in_progress` / `waiting_client`, **único por pipeline**), `order`, `color` (hex opcional).
- **Contact** (`contacts`, schema tenant): cliente identificado. Compartilhado entre Live Chat, WhatsApp e tickets manuais. Atributos: `id`, `name`, `email` (chave dedup P1, case-insensitive), `phone`, `phone_normalized` (chave dedup P2, indexado), `notes`, `source_channels[]` (live_chat / whatsapp / manual), `created_at`, `updated_at`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Atendentes conseguem visualizar e atuar em **todos os tickets do seu departamento** em < 2 cliques a partir do CRM (login → Kanban renderizado → clique no card → detalhe).
- **SC-002**: 100% dos transbordos da IA resultam em ticket criado em < 2 segundos a partir da `transfer_to_human`, com histórico completo preservado e atendente notificado (quando houver capacidade).
- **SC-003**: 0% de tickets perdidos: nenhum transbordo da IA pode ficar sem ticket criado (mesmo com indisponibilidade temporária de atendentes — ticket fica `new` na fila).
- **SC-004**: 0% de protocolos duplicados, mesmo sob criação concorrente — sequência única por tenant por dia.
- **SC-005**: 0% de tickets `resolved` ou `cancelled` aparecem no Kanban; 100% acessíveis via busca/filtro de período.
- **SC-006**: Anotações internas nunca chegam ao cliente em **0% dos casos** observados em testes E2E (envio por Live Chat, WhatsApp, e prompt da IA).
- **SC-007**: Deduplicação automática de contatos reduz duplicatas a **≤ 1%** no banco em casos de e-mail/telefone idênticos repetidos por canais diferentes.
- **SC-008**: Tempo de carregamento do Kanban com ≤ 100 tickets ativos: P95 < 1.5 segundos.
- **SC-009**: Busca full-text retorna resultados em P95 < 1 segundo para corpus de até 10.000 mensagens.
- **SC-010**: Evento WebSocket `ticket.sla_warning` chega ao CRM em ≤ 3 segundos após atingir 80% do prazo.
- **SC-011**: Atendente identifica o estado de SLA de um ticket à primeira vista (badge verde/amarelo/vermelho) com taxa de acerto ≥ 95% em testes de usabilidade.
- **SC-012**: Transferências entre departamentos preservam **100%** do histórico — nenhuma mensagem ou nota perdida em validação E2E.
- **SC-013**: 100% dos atendentes da role `tenant_attendant` MUST receber `403 Forbidden` ao tentar acessar tickets de departamentos fora do seu escopo (testes de segurança/permissão).

## Assumptions

- **Dependência de Spec 005 (Departamentos / Atendentes)**: este módulo consome `department_id`, `attendant_id`, e metas de SLA (`sla_first_response_minutes`, `sla_resolution_minutes`) definidas no departamento. Round-robin e disponibilidade do atendente (online/offline, `max_simultaneous_chats`) também vêm da Spec 005.
- **Dependência de Spec 006 (Agentes de IA)**: o transbordo automático (US1) é disparado pela tool call `transfer_to_human(department_id, reason)` do Orchestrator.
- **Dependência de Spec 007 (Live Chat)**: o `channel = live_chat` consome `conversations` e `conversation_messages` desta spec. Mensagens enviadas pelo atendente do ticket são entregues via WebSocket do Live Chat.
- **Dependência de Spec 008 (WhatsApp)**: o `channel = whatsapp` consome o pipeline desta spec. O envio de mensagens respeita a janela de 24h (atendente bloqueado de texto livre quando janela expira; deve usar template aprovado).
- **Dependência de Spec 010 (Notificações)**: notificações in-app e por e-mail (atribuição, transferência, SLA warning) são enviadas pelo subsistema de notificações.
- **Dependência de Spec 011 (Agenda)**: o campo `has_reminder_alert` é ligado/desligado pelo subsistema de agendamentos quando um lembrete automático falha.
- **`tenant_attendant` pode criar tickets manualmente** apenas em departamentos aos quais pertence (assumindo necessidade operacional — caso contrário, restringir só a `supervisor`/`tenant_admin` é trivial via FR).
- **Normalização de telefone**: `phone_normalized` armazena apenas dígitos. Formato presumido brasileiro (DDD + número). Para tenants com clientes internacionais, recomenda-se incluir DDI (55) explicitamente. Decisão delegada à implementação (sufixar com extensão se necessário em release futuro).
- **Concorrência de protocolo**: implementação usa sequência PostgreSQL por tenant por dia (`tenant_{slug}.ticket_protocol_seq_YYYYMMDD`) ou lock pessimista. A escolha é decisão de implementação.
- **Pipeline imutável em quantidade de colunas**: tenant não pode adicionar ou remover colunas. As 3 colunas-base são criadas no provisionamento do departamento. Apenas renomear, reordenar e colorir são permitidos. Razão: preservar consistência do ciclo de vida e simplicidade da experiência.
- **Notes append-only**: anotações internas não podem ser editadas nem excluídas após criação (auditoria + integridade do histórico). Para "corrigir" uma nota, adiciona-se outra com a correção.
- **Tickets `resolved` e `cancelled` são imutáveis** para mudanças de dado (exceto notas internas, que podem ser adicionadas para registrar contexto pós-encerramento — útil para auditoria interna).
- **Reabertura proibida** (`resolved` e `cancelled`): se cliente reentrar em contato, nova conversa + novo ticket são criados; o histórico permanece acessível via perfil do contato.
- **Eventos WebSocket são entregues em **best-effort**: caso o cliente esteja offline, o ticket ainda é atualizado no banco — o evento perdido não é re-emitido. O CRM ao se conectar carrega o estado atual via REST.
- **Modo offline do atendente**: tickets atribuídos a um atendente offline permanecem com ele; supervisor pode transferir manualmente. Auto-reatribuição em offline NÃO é objetivo desta spec.
- **Tags são livres** (texto), com normalização sugerida: lowercase, trim, sem caracteres especiais além de hífen e underscore. Limite recomendado: 30 chars por tag, 10 tags por ticket.
- **Subject** suporta até 255 chars; o auto-preenchimento truncará em 100 chars para deixar espaço de edição.
- **Compatibilidade temporal**: `sla_first_response_deadline`, `sla_resolution_deadline`, `resolved_at`, `cancelled_at`, `waiting_client_since`, `created_at`, `updated_at` são `timestamptz` (UTC).
