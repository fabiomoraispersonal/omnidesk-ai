# Feature Specification: Departamentos e Atendentes

**Feature Branch**: `005-departments-attendants`
**Created**: 2026-05-07
**Status**: Draft
**Input**: User description: "Spec 05 — Departamentos e Atendentes (estrutura de times humanos por tenant: departamentos como filas de tickets, atendentes como operadores, presença em tempo real, distribuição automática de tickets, transferências, respostas pré-formadas, sugestão de resposta via IA, SLA simples)."

## User Scenarios & Testing *(mandatory)*

> Atores principais: **Tenant Admin** (configura times), **Supervisor** (acompanha operação), **Atendente** (atende clientes), **Cliente** (recebe atendimento), **Sistema** (distribui tickets, IA sugere respostas).

### User Story 1 — Tenant admin organiza o time por departamentos (Priority: P1) 🎯 MVP

O tenant admin cria os departamentos da operação (ex.: Comercial, Suporte, Pós-Venda), define horário de atendimento e metas de SLA opcionais, cadastra os atendentes humanos e os vincula a um ou mais departamentos. Sem essa estrutura nada do CRM funciona — é a base para tudo o que vem depois.

**Why this priority**: Sem departamentos e atendentes, não há para quem transferir tickets vindos da IA. Esta história entrega o cadastro mínimo viável que destrava todas as demais.

**Independent Test**: Tenant admin entra no Painel CRM, cria 2 departamentos (Comercial, Suporte), cadastra 3 atendentes e vincula cada um a pelo menos um departamento. A listagem e a edição funcionam fim-a-fim, sem dependência de IA, status de presença ou tickets.

**Acceptance Scenarios**:

1. **Given** o tenant admin no Painel CRM, **When** cria um departamento informando nome e dias úteis sem horário, **Then** o sistema persiste o departamento como ativo e disponível 24/7 do ponto de vista do cliente.
2. **Given** um atendente já vinculado a tickets ativos, **When** o tenant admin tenta excluir o departamento principal dele, **Then** o sistema impede a exclusão física e oferece a desativação lógica como alternativa.
3. **Given** um atendente convidado e aceito (Spec 002/004), **When** o tenant admin o vincula a 2 departamentos marcando um como principal, **Then** o atendente passa a aparecer nas listas dos dois departamentos com a marcação correta.

---

### User Story 2 — Atendente recebe ticket distribuído automaticamente ao ficar online (Priority: P1)

Quando um cliente é transferido pela IA para o departamento, o sistema escolhe automaticamente um atendente disponível usando distribuição equilibrada (round-robin) e respeita o limite de atendimentos simultâneos. O atendente é notificado e o histórico da conversa o acompanha.

**Why this priority**: É o coração da operação humana — sem distribuição automática, o time precisa monitorar fila manualmente o tempo todo.

**Independent Test**: Com 3 atendentes online no Comercial, criar 6 tickets em sequência. Esperado: cada atendente recebe 2 tickets em sequência alternada; nenhum atendente excede o limite configurado.

**Acceptance Scenarios**:

1. **Given** 3 atendentes `online` no Comercial e nenhum no limite, **When** 3 tickets entram em sequência, **Then** cada atendente recebe exatamente um ticket.
2. **Given** 1 atendente `online` no limite e outro `online` com vagas, **When** entra um novo ticket, **Then** apenas o segundo atendente recebe o ticket.
3. **Given** nenhum atendente elegível (todos `offline` ou no limite), **When** entra um ticket, **Then** o ticket vai para a fila do departamento com status `queued` e é atribuído assim que um atendente se libera.

---

### User Story 3 — Sistema impede dois atendentes pegarem o mesmo ticket (Priority: P1)

Quando dois atendentes clicam em "Assumir" no mesmo ticket da fila quase ao mesmo tempo, apenas um sucede. O outro recebe uma mensagem clara de que o ticket já foi assumido e a UI dele atualiza imediatamente.

**Why this priority**: Conflito de atribuição é uma falha de integridade que confunde clientes (dois atendentes respondendo) e gera ruído operacional. Tem que ser resolvido por design, não por convenção.

**Independent Test**: Em ambiente de carga, simular 50 pares concorrentes de atendentes clicando "Assumir" no mesmo ticket. Esperado: 100 % dos casos resolvem com exatamente um vencedor; perdedores recebem notificação clara em ≤ 200 ms.

**Acceptance Scenarios**:

1. **Given** um ticket na fila e dois atendentes clicando "Assumir" simultaneamente, **When** o sistema processa as requisições, **Then** apenas uma é aceita e a outra retorna mensagem "Este ticket já foi assumido por [Nome]".
2. **Given** um ticket atribuído ao atendente A, **When** o atendente B abre o mesmo ticket e clica em "Assumir", **Then** o sistema exibe confirmação "Este ticket está com [A]. Deseja assumir?" antes de prosseguir.
3. **Given** o atendente B confirma a tomada do ticket, **When** o ticket é reatribuído, **Then** o atendente A recebe a notificação "O ticket #XXXX foi assumido por [B]" no CRM.

---

### User Story 4 — Atendente transfere ticket para outro atendente ou departamento (Priority: P2)

Durante o atendimento, o atendente percebe que o assunto pertence a outro time ou colega e transfere o ticket. O histórico completo da conversa segue junto, o destinatário é notificado e, se o destino for outro departamento, o SLA é recalculado com as metas do novo departamento.

**Why this priority**: Aumenta produtividade e melhora a experiência do cliente, mas o time pode operar (de forma menos eficiente) sem isso até a versão seguinte se necessário.

**Independent Test**: Atendente A no Comercial transfere ticket #1234 para o departamento Suporte com motivo "cliente quer suporte técnico". Esperado: o ticket aparece na fila do Suporte (ou é atribuído pelo round-robin), o histórico íntegro é preservado, o motivo é registrado e o SLA é recalculado a partir do momento da transferência usando as metas do Suporte.

**Acceptance Scenarios**:

1. **Given** um ticket atribuído ao atendente A, **When** A transfere para o atendente B (mesmo ou outro departamento), **Then** o ticket muda de dono, B é notificado e o histórico é preservado integralmente.
2. **Given** um ticket no departamento X com SLA de 60 min de primeira resposta, **When** transferido para o departamento Y com SLA de 30 min, **Then** o contador de SLA é recalculado considerando a meta do destino a partir do momento da transferência.
3. **Given** um atendente transferindo um ticket, **When** preenche um motivo opcional, **Then** o motivo é salvo no histórico do ticket e visível ao atendente destino.

---

### User Story 5 — Status de presença reflete a realidade da operação (Priority: P2)

O atendente alterna seu status entre `online`, `away` e `offline` via toggle. Se ficar inativo no CRM por 15 minutos, o sistema o move para `away` automaticamente; após mais 30 minutos, para `offline`. Toda mudança é auditada.

**Why this priority**: Sem isso, a distribuição automática considera atendentes "fantasmas" (que esqueceram de sair) e clientes esperam por quem não está ali.

**Independent Test**: Atendente entra `online`, fica 15 minutos sem interagir com o CRM. Esperado: o status muda para `away` automaticamente com `changed_by=system`, o evento aparece em tempo real no Painel do supervisor e um documento é gravado no histórico de auditoria.

**Acceptance Scenarios**:

1. **Given** um atendente `online` sem nenhuma interação no CRM, **When** decorrem 15 minutos, **Then** o sistema o move para `away` automaticamente e a mudança é registrada como originada pelo sistema.
2. **Given** um atendente `away` há 30 minutos sem retorno, **When** o tempo limite é atingido, **Then** o sistema o move para `offline` automaticamente.
3. **Given** qualquer transição de status, **When** ocorre, **Then** um registro de auditoria é gravado contendo `de`, `para`, `motivo` (manual/sistema) e timestamp.
4. **Given** um supervisor acompanhando o painel de operação, **When** outro atendente muda de status, **Then** o painel atualiza em tempo real sem refresh.

---

### User Story 6 — Respostas pré-formadas aceleram o atendimento (Priority: P2)

O atendente digita `/` no campo de mensagem, busca uma resposta pré-formada (ex.: "Saudação inicial"), e o sistema substitui as variáveis (`{{client_name}}`, `{{attendant_name}}`, `{{ticket_number}}`, `{{department_name}}`) com os dados do contexto antes do envio. Atendentes criam suas próprias respostas; tenant admin gerencia todas.

**Why this priority**: Ganho de produtividade significativo no dia-a-dia, mas não bloqueia o produto inicial.

**Independent Test**: Atendente abre conversa de cliente "Maria Silva" no ticket #4321 do Comercial, digita `/saudacao` e seleciona a resposta com variáveis. Esperado: o texto enviado contém o nome real da cliente, o nome do atendente, o número do ticket e o nome do departamento sem placeholders.

**Acceptance Scenarios**:

1. **Given** uma resposta pré-formada com `{{client_name}}` e `{{ticket_number}}`, **When** o atendente a usa em uma conversa, **Then** as variáveis são substituídas pelos valores reais antes do envio.
2. **Given** uma resposta criada com escopo do departamento Comercial, **When** um atendente do Suporte tenta listar respostas, **Then** essa resposta não aparece para ele (mas globais aparecem).
3. **Given** uma resposta criada por outro atendente, **When** o tenant admin a edita ou exclui, **Then** a operação é permitida; quando o autor original tenta, só pode editar/excluir a própria.

---

### User Story 7 — Comportamento de transbordo respeita horário e disponibilidade (Priority: P2)

Quando a IA decide transferir para humano, o sistema combina horário comercial e atendentes online para escolher entre transferir, abrir ticket na fila com mensagem "todos ocupados" ou abrir ticket informando o próximo horário comercial.

**Why this priority**: Sem essa lógica, clientes recebem mensagens incoerentes ("transferindo para atendente" quando ninguém vai responder até segunda).

**Independent Test**: Em ambiente de teste, com Comercial sem atendente `online` e fora do horário comercial, simular transferência. Esperado: o cliente recebe mensagem informando o próximo horário de atendimento e o ticket entra na fila com status `queued`.

**Acceptance Scenarios**:

1. **Given** dentro do horário comercial e ≥1 atendente `online`, **When** a IA decide transferir, **Then** o ticket é atribuído normalmente.
2. **Given** dentro do horário comercial e nenhum atendente `online`, **When** a IA decide transferir, **Then** o ticket entra na fila com mensagem "todos ocupados, retornaremos em breve".
3. **Given** fora do horário comercial e ≥1 atendente `online`, **When** a IA decide transferir, **Then** o ticket é atribuído normalmente (atendente online tem prioridade).
4. **Given** fora do horário comercial e nenhum atendente `online`, **When** a IA decide transferir, **Then** o cliente recebe o próximo horário de atendimento e o ticket fica `queued`.

---

### User Story 8 — Sugestão de resposta com IA acelera atendentes (Priority: P3)

O atendente clica em "Sugerir resposta com IA" e recebe uma sugestão pré-visualizada considerando o contexto da conversa e o prompt do sub-agente do departamento. Ele aprova, edita ou descarta. **Nada é enviado sem aprovação humana explícita.**

**Why this priority**: Acelera atendentes experientes mas é dispensável para o lançamento mínimo. Requer integração estável com Spec 002 (Agentes de IA).

**Independent Test**: Atendente clica em "Sugerir resposta" em uma conversa com 5 mensagens. Esperado: surge campo de pré-visualização editável; o atendente edita, clica em "Enviar" e a mensagem chega ao cliente exatamente como editada. Nenhuma mensagem foi enviada antes da aprovação.

**Acceptance Scenarios**:

1. **Given** uma conversa em andamento, **When** o atendente solicita sugestão de IA, **Then** o sistema retorna o texto sugerido em uma área de pré-visualização sem enviar ao cliente.
2. **Given** uma sugestão exibida, **When** o atendente edita o texto e clica em "Enviar", **Then** a versão editada é enviada ao cliente.
3. **Given** uma sugestão exibida, **When** o atendente clica em "Descartar", **Then** nenhuma mensagem é enviada e a sugestão some.
4. **Given** o uso da feature, **When** uma sugestão é gerada, aprovada, editada ou descartada, **Then** o evento é registrado para análise futura.

---

### User Story 9 — SLA visual ajuda a priorizar tickets em risco (Priority: P3)

O atendente vê no card do ticket um contador regressivo do SLA. Em 80 % do tempo consumido o badge fica amarelo (atenção); ao expirar fica vermelho (atrasado). Períodos fora do horário comercial não contam.

**Why this priority**: Adiciona valor à priorização visual mas é puramente UX — a operação funciona sem isso.

**Independent Test**: Departamento com SLA de 60 min de primeira resposta; criar ticket às 17h50 (encerra 18h). Esperado: contador pausa às 18h, retoma às 8h do próximo dia útil; badge fica amarelo aos 48 min úteis e vermelho aos 60 min úteis.

**Acceptance Scenarios**:

1. **Given** um departamento com SLA configurado, **When** um ticket é atribuído, **Then** o card exibe contador regressivo correto.
2. **Given** um ticket com 80 % do SLA consumido, **When** o card é renderizado, **Then** o badge aparece em amarelo.
3. **Given** um ticket com SLA expirado, **When** o card é renderizado, **Then** o badge aparece em vermelho.
4. **Given** um departamento sem SLA configurado, **When** um ticket pertence a ele, **Then** nenhum contador é exibido.
5. **Given** um ticket criado às 17h50 com SLA de 60 min em departamento que encerra às 18h, **When** o relógio avança para 18h e depois para 08h do próximo dia útil, **Then** o tempo entre 18h e 08h não conta no contador.

---

### Edge Cases

- **Atendente com 0 departamentos vinculados**: não recebe nenhum ticket automaticamente e não aparece como elegível em nenhuma fila; supervisor recebe alerta.
- **Departamento desativado durante o expediente**: tickets em andamento permanecem com seus atendentes; novos tickets do canal IA caem na regra de transbordo com mensagem "departamento indisponível".
- **Atendente único do departamento entra em `away` por timeout**: novos tickets passam a `queued`; o atendente é notificado ao retornar.
- **Tenant admin tenta excluir o último atendente ativo de um departamento que tem fila**: bloquear; sugerir transferir os tickets antes.
- **Variável de resposta pré-formada não tem valor no contexto** (ex.: cliente anônimo sem nome): substituição usa fallback explícito ("cliente") em vez de string vazia ou `{{client_name}}` literal.
- **Atendente clica em "Assumir" em ticket que acabou de ser fechado por outro atendente**: receber mensagem "Este ticket já foi resolvido" sem reabrir.
- **Sugestão de IA falha (timeout ou erro do provedor)**: exibir mensagem clara "não foi possível gerar sugestão agora" sem afetar a conversa em andamento.
- **Transferência para departamento desativado**: bloquear no front e no back, com mensagem explícita.
- **Round-robin com lista de elegíveis vazia entre dois ticks**: ticket entra em fila sem ficar em loop.
- **Relógio do servidor avança após mudança de horário de verão**: contagem de SLA usa horário do servidor em UTC; não recalcula retroativamente.

---

## Requirements *(mandatory)*

> Convenção: cada requisito é numerado FR-NNN e deve ser testável de forma binária.

### Cadastro e ciclo de vida

- **FR-001** O sistema **deve** permitir que o tenant admin crie departamentos com nome obrigatório, descrição opcional, horário comercial opcional (início, fim, dias da semana) e metas de SLA opcionais (primeira resposta e resolução).
- **FR-002** O sistema **deve** tratar departamento sem horário comercial configurado como disponível 24/7 (a IA não menciona horários ao cliente nesse caso).
- **FR-003** O sistema **deve** suportar desativação lógica (`is_active=false`) de departamentos. **Não deve** permitir exclusão física quando houver tickets ou histórico vinculados.
- **FR-004** O sistema **deve** permitir cadastrar atendentes vinculados a usuários da plataforma (Spec 002), com nome, avatar opcional e limite de atendimentos simultâneos (default 5).
- **FR-005** O sistema **deve** permitir vincular um atendente a um ou mais departamentos, marcando exatamente um como **principal** (para fins de relatório).
- **FR-006** O sistema **deve** suportar desativação lógica de atendentes; atendentes desativados **não devem** receber novos tickets nem aparecer como disponíveis.

### Status de presença

- **FR-007** O atendente **deve** poder alternar manualmente entre `online`, `away` e `offline` no CRM.
- **FR-008** O sistema **deve** mover automaticamente para `away` qualquer atendente `online` que não interaja com o CRM por 15 minutos consecutivos.
- **FR-009** O sistema **deve** mover automaticamente para `offline` qualquer atendente `away` que não retorne em 30 minutos.
- **FR-010** O status atual **deve** estar disponível para leitura em tempo real (sub-segundo) por outros componentes do sistema (distribuição, painel do supervisor).
- **FR-011** Toda transição de status **deve** ser persistida em registro auditável contendo de/para, origem (`manual` ou `system`) e timestamp.
- **FR-012** A tabela de status atual **deve** ser sincronizada a cada mudança para uso em relatórios.

### Distribuição de tickets

- **FR-013** Quando um ticket entra em um departamento, o sistema **deve** identificar atendentes elegíveis (vinculados ao departamento, com status `online`, abaixo do limite de simultâneos e ativos).
- **FR-014** Havendo elegíveis, o sistema **deve** atribuir o ticket usando distribuição equilibrada (round-robin) entre eles.
- **FR-015** Não havendo elegível, o ticket **deve** entrar na fila do departamento com status `queued`, e ser atribuído automaticamente assim que houver elegível.
- **FR-016** O sistema **deve** garantir que um ticket nunca seja atribuído simultaneamente a dois atendentes (lock de concorrência atômico, com timeout protetivo).
- **FR-017** O atendente atribuído **deve** ser notificado em tempo real (WebSocket) ao receber um ticket.
- **FR-018** Atendente que atinge o limite de atendimentos simultâneos **não deve** receber novos tickets até que algum dos seus atuais seja encerrado, transferido ou desatribuído.

### Assumir ticket manualmente

- **FR-019** Qualquer atendente do departamento **deve** poder assumir manualmente um ticket que esteja na fila (`queued`).
- **FR-020** Se o ticket já estiver atribuído a outro atendente, o sistema **deve** exigir confirmação antes de transferir a posse.
- **FR-021** Ao assumir um ticket de outro atendente, o atendente original **deve** ser notificado em tempo real.

### Transferência

- **FR-022** O atendente **deve** poder transferir um ticket para outro atendente (mesmo ou outro departamento) ou diretamente para um departamento (entrando em fila).
- **FR-023** O histórico completo da conversa **deve** acompanhar o ticket em qualquer transferência, sem perda de mensagens, anexos ou metadados.
- **FR-024** O atendente destino **deve** receber notificação imediata ao receber um ticket transferido.
- **FR-025** A transferência **deve** registrar no histórico do ticket: de quem, para quem (atendente ou departamento), motivo opcional e timestamp.
- **FR-026** Ao transferir um ticket entre departamentos com SLAs distintos, o sistema **deve** recalcular o contador de SLA usando as metas do departamento destino, a partir do momento da transferência.

### Regra de transbordo

- **FR-027** Dentro do horário comercial, com pelo menos um atendente `online`, o sistema **deve** transferir o ticket normalmente.
- **FR-028** Dentro do horário comercial, sem atendente `online`, o sistema **deve** colocar o ticket em fila e fazer a IA informar ao cliente "todos os atendentes estão ocupados; retornaremos em breve".
- **FR-029** Fora do horário comercial, com atendente `online`, o sistema **deve** transferir normalmente (atendente online tem prioridade sobre o calendário).
- **FR-030** Fora do horário comercial, sem atendente `online`, o sistema **deve** colocar o ticket em fila e fazer a IA informar ao cliente o próximo horário de atendimento do departamento.

### Respostas pré-formadas

- **FR-031** Atendentes **devem** poder criar, editar e excluir respostas pré-formadas próprias; tenant admin **deve** poder gerenciar todas.
- **FR-032** Cada resposta **deve** ter título e conteúdo obrigatórios e escopo opcional (departamento específico ou global).
- **FR-033** Respostas **devem** suportar substituição das variáveis `{{client_name}}`, `{{attendant_name}}`, `{{ticket_number}}` e `{{department_name}}` no momento da inserção.
- **FR-034** Variáveis sem valor disponível no contexto **devem** ser substituídas por um fallback humano ("cliente" para nome, "—" para número), nunca pelo placeholder literal nem por string vazia.
- **FR-035** O atendente **deve** poder buscar respostas por título e por trecho do conteúdo no campo de mensagem.

### Sugestão de resposta com IA

- **FR-036** O atendente **deve** poder solicitar uma sugestão de resposta com IA durante uma conversa.
- **FR-037** A sugestão **deve** considerar o contexto recente da conversa e o prompt do sub-agente do departamento (Spec 002), quando existir.
- **FR-038** A sugestão **nunca deve** ser enviada automaticamente ao cliente; sempre exige aprovação humana explícita (aprovar, editar ou descartar).
- **FR-039** O sistema **deve** registrar para análise: quando uma sugestão foi gerada, qual ação humana ocorreu (aprovou, editou, descartou) e em qual conversa/ticket.
- **FR-040** Falhas na geração de sugestão (timeout, erro do provedor) **não devem** afetar a conversa em andamento e **devem** retornar mensagem de erro clara ao atendente.

### SLA simples

- **FR-041** Cada departamento **pode** ter metas de SLA opcionais para primeira resposta e resolução.
- **FR-042** O contador de primeira resposta **deve** iniciar quando o ticket é atribuído ao atendente; o de resolução **deve** iniciar quando o ticket é criado.
- **FR-043** O contador **deve** pausar fora do horário comercial do departamento e retomar no próximo período comercial.
- **FR-044** O sistema **deve** sinalizar visualmente: badge **amarelo** (≥ 80 % do tempo consumido) e badge **vermelho** (prazo expirado).
- **FR-045** Departamentos sem SLA configurado **não devem** exibir contador.
- **FR-046** Não há escalonamento automático nem relatórios de performance por SLA no MVP — apenas a visibilidade visual.

### Eventos em tempo real

- **FR-047** O sistema **deve** emitir eventos em tempo real (WebSocket) para mudanças de status de atendente, atribuição, transferência e entrada em fila de tickets.
- **FR-048** Os clientes do CRM (atendente, supervisor, tenant admin) **devem** receber e refletir os eventos sem necessidade de refresh manual.

---

### Key Entities *(include if feature involves data)*

- **Departamento**: grupo de atendentes que compartilha uma fila de tickets. Atributos chave: nome, descrição, horário comercial (início, fim, dias úteis), metas de SLA opcionais, ativo.
- **Atendente**: operador humano que atende clientes. Atributos chave: vínculo com usuário da plataforma, nome de exibição, avatar, limite de atendimentos simultâneos, ativo.
- **Vínculo Atendente↔Departamento**: relação muitos-para-muitos com marcação de departamento principal por atendente.
- **Status do Atendente**: estado de presença (`online`, `away`, `offline`) com origem (`manual` ou `system`) e timestamp da última mudança.
- **Histórico de Status**: trilha de auditoria com cada transição de status (de, para, motivo, timestamp).
- **Resposta Pré-formada**: texto reutilizável com variáveis substituíveis, escopo opcional por departamento e autor.
- **Atribuição de Ticket**: vínculo entre ticket e atendente, com lock de concorrência durante o processo de atribuição.
- **Transferência de Ticket**: registro de mudança de dono ou departamento de um ticket, com motivo opcional.
- **Sugestão de IA (uso)**: telemetria de quando uma sugestão foi gerada, ação humana resultante e conversa associada.

---

## Success Criteria *(mandatory)*

> Métricas observáveis pelo usuário ou pela operação, sem referência a tecnologia.

### Mensuráveis (quantitativas)

- **SC-001** **100 %** dos tickets transferidos pela IA caem em uma de três rotas legítimas (atribuído, em fila com mensagem "ocupados", em fila com horário do próximo turno) — zero tickets perdidos.
- **SC-002** **0** casos de atribuição duplicada (dois atendentes recebendo o mesmo ticket) em uma simulação de carga com 50 pares concorrentes.
- **SC-003** Distribuição round-robin mantém **diferença máxima de 1 ticket** na contagem por atendente em uma rajada de até 100 tickets entre N atendentes elegíveis.
- **SC-004** Mudanças de status entre o toggle no CRM e o reflexo no painel do supervisor ocorrem em **≤ 1 segundo** (P95).
- **SC-005** Atendente `online` sem interação por 15 minutos é movido para `away` em **≤ 30 segundos** após o limite (tolerância de tick).
- **SC-006** Substituição de variáveis em respostas pré-formadas ocorre em **100 %** das inserções; **0** placeholders literais (`{{...}}`) chegam ao cliente.
- **SC-007** **0** mensagens enviadas ao cliente sem aprovação humana quando o fluxo de sugestão de IA é usado.
- **SC-008** Quando configurado, o badge de SLA passa a amarelo aos **80 %** do tempo consumido e a vermelho aos **100 %** com erro absoluto **≤ 30 segundos**.
- **SC-009** Em ambiente de produção, o tempo médio de **primeira resposta humana** após atribuição automática é **≥ 30 % menor** do que com atribuição manual (medido em janela de 30 dias após adoção).
- **SC-010** Pelo menos **70 %** das sugestões de IA são aprovadas (com ou sem edição) por atendentes treinados, indicando qualidade aceitável.

### Qualitativas

- **SC-011** Tenant admin consegue cadastrar do zero 1 departamento com 3 atendentes vinculados em **menos de 5 minutos** sem consultar documentação.
- **SC-012** Atendente experiente reduz tempo de envio de mensagens repetitivas (saudação, encerramento) em **≥ 40 %** após adoção das respostas pré-formadas (medido por amostragem qualitativa em pilotos).
- **SC-013** Supervisores relatam visibilidade satisfatória da operação (status, fila, SLA) em pesquisa pós-implantação — **≥ 80 % de aprovação**.

---

## Assumptions

- **A1** O módulo de Auth (Spec 002) já fornece usuários, roles (`tenant_admin`, `supervisor`, `attendant`) e o JWT que identifica o tenant atual; este módulo apenas consome.
- **A2** O módulo de Roles e Permissões (Spec 004) cobre a matriz de policies aplicada aos endpoints listados (ex.: `Departments.Create`, `Attendants.Create`, `CanViewAllConversations`); este módulo apenas registra o uso esperado.
- **A3** Tickets, conversas e mensagens são entidades pertencentes a outras specs (provavelmente Spec 008 — Tickets). Esta spec **lê** e **referencia** tickets mas não os cria.
- **A4** A IA do canal (Spec 002 — Agentes de IA) é quem invoca a regra de transbordo; este módulo expõe a decisão (atendente disponível? horário comercial?) e a IA decide o que dizer ao cliente.
- **A5** Telemetria de uso de IA fica armazenada no canal de logs estruturados do projeto (MongoDB), em coleções por tenant.
- **A6** O CRM do tenant (`omniDesk.Crm`) é o único cliente desses endpoints e WebSocket events; o Painel Admin (`omniDesk.Admin`) não opera departamentos/atendentes diretamente neste momento.
- **A7** Supervisor e tenant admin compartilham 100 % da visibilidade desses dados; controle granular de visualização entre supervisores fica fora de escopo no MVP.
- **A8** Histórico de auditoria de status (Mongo) tem retenção alinhada com a política geral do tenant (definida na Spec 011 — Auditoria); não é refeito aqui.
- **A9** Avatares de atendente reusam o storage de mídia já provisionado por tenant (MinIO); upload e moderação de imagens ficam fora de escopo desta spec.
- **A10** Round-robin é "memoryless" entre reinícios — após reinicialização do componente de distribuição, a próxima atribuição parte do primeiro elegível ordenado por id; isso é aceitável dado o volume esperado.

---

## Dependencies

- **Spec 002 — Auth e JWT**: identidade do usuário, claim `tenant_slug`.
- **Spec 003 — Tenant Provisioning**: schema do tenant criado antes de qualquer operação.
- **Spec 004 — Roles e Permissões**: matriz de policies (`Departments.*`, `Attendants.*`, `Conversations.ViewAll`, `Tickets.ViewAll`, `Notifications.ConfigureForClients`).
- **Spec 002 — Agentes de IA** (referenciada como dependência futura por User Stories 7 e 8): prompt do sub-agente do departamento + provedor LLM.
- **Spec 008 — Tickets** (consumidor primário): este módulo serve a estrutura humana que recebe os tickets.

## Out of Scope (V1)

- Escalonamento automático por SLA (notificação a supervisor, reatribuição forçada).
- Relatórios de performance por atendente e departamento.
- Distribuição inteligente baseada em skills, idiomas ou especialização (V1 só round-robin).
- Permissões granulares dentro de "supervisor" (ex.: supervisor só vê seus departamentos).
- Mensagens automáticas pré-prontas disparadas por evento (ex.: "atendente entrou no chat") — só substituição em respostas que o atendente envia manualmente.
- Treinamento ou ajuste fino do modelo de IA com base no histórico de aprovações de sugestão.
- Calendário de exceções (feriados, plantões especiais) — apenas dias úteis simples no MVP.
- Política de retenção específica de auditoria de status (segue a Spec 011).
