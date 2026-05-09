# Feature Specification: Live Chat (Widget)

**Feature Branch**: `007-live-chat-widget`
**Created**: 2026-05-09
**Status**: Draft
**Input**: User description: "Spec 07 — Live Chat (Widget): widget JavaScript instalável em qualquer site do tenant, canal de entrada de conversas via web com IA como primeiro contato e transbordo para humano. Cobre widget front-end, configuração visual por tenant, gestão de sessões de conversa (open/resolved/abandoned), persistência de histórico, comunicação em tempo real via WebSocket, painel de configuração no CRM (aparência, identificação, LGPD, comportamento, segurança, instalação) e visão multi-conversas para atendentes."

## User Scenarios & Testing *(mandatory)*

> Atores principais: **Visitante** (cliente final do tenant, navega pelo site e abre o widget), **Tenant Admin** (configura aparência, LGPD e comportamento do widget no CRM), **Atendente** (humano que recebe transbordos e gerencia múltiplas conversas no CRM), **Agente Orchestrator** (Spec 006, primeiro contato automatizado), **Sistema** (gerencia ciclo de vida da conversa, abandono, inatividade e desabilitação).

### User Story 1 — Visitante conversa com a IA pelo widget no site do tenant (Priority: P1) 🎯 MVP

Um visitante anônimo chega ao site do tenant, vê um botão flutuante (launcher) no canto da página, clica e abre o painel do widget. O painel exibe a mensagem de boas-vindas configurada pelo tenant e (opcionalmente) um formulário pré-chat solicitando dados de identificação. Antes de enviar a primeira mensagem, o visitante precisa aceitar os termos de privacidade/LGPD. Após aceitar, ele envia a primeira mensagem; o Agente Orchestrator (Spec 006) responde em tempo real no mesmo painel. Toda a conversa acontece sem o visitante sair do site do tenant.

**Why this priority**: É o canal principal de entrada de conversas via web. Sem o widget funcionando ponta a ponta (carregar no site do tenant → aceitar LGPD → enviar mensagem → receber resposta da IA), o módulo não entrega valor algum. Esta história é o MVP do Live Chat.

**Independent Test**: Em um tenant recém-provisionado, o admin cola o snippet de instalação no `<head>` de uma página HTML qualquer, abre essa página em um navegador anônimo, clica no launcher, aceita os termos LGPD, envia "Olá, gostaria de mais informações" e recebe resposta coerente da IA em até alguns segundos. A conversa fica registrada no CRM com status `open`.

**Acceptance Scenarios**:

1. **Given** o snippet do widget está instalado em uma página do site do tenant, **When** o visitante abre a página, **Then** o launcher é exibido na posição configurada (`bottom_right` por padrão) com a cor primária e ícone configurados, sem impactar o tempo de carregamento da página host.
2. **Given** o widget está habilitado (`is_enabled = true`), **When** o visitante clica no launcher pela primeira vez, **Then** o painel abre com animação de slide-up exibindo o nome da empresa, a mensagem de boas-vindas e o checkbox de aceite LGPD (com link para a política completa, se configurada).
3. **Given** o visitante ainda não aceitou os termos LGPD, **When** tenta digitar e enviar uma mensagem, **Then** o botão de envio permanece desabilitado e a mensagem não é transmitida ao backend.
4. **Given** o visitante marca o checkbox de aceite LGPD e digita a primeira mensagem, **When** clica em enviar, **Then** o sistema cria um novo `visitor` (associado ao `anonymous_id` gerado via `crypto.randomUUID()` e salvo em `localStorage`), cria uma `conversation` com `status = open` e `lgpd_consent_at` preenchido, e o Orchestrator responde no mesmo painel via WebSocket.
5. **Given** a conversa está em andamento, **When** o agente está compondo resposta, **Then** o widget exibe indicador "digitando…" antes da mensagem chegar.
6. **Given** o tenant configurou `require_identification = true` com campos obrigatórios, **When** o visitante abre o widget pela primeira vez, **Then** o formulário pré-chat é exibido antes do checkbox LGPD e os dados informados são associados ao `visitor` (e ao `contact` quando aplicável).

---

### User Story 2 — Tenant configura aparência, privacidade e comportamento do widget no CRM (Priority: P1)

O tenant admin acessa **CRM → Configurações → Live Chat** e personaliza o widget que será exibido no site da empresa: cor primária, ícone, posição, nome da empresa, mensagem de boas-vindas, formulário pré-chat (opcional), texto dos termos LGPD, timeouts de abandono/inatividade, domínios autorizados. Um preview ao vivo do widget é renderizado ao lado do formulário, atualizando em tempo real conforme o admin edita os campos. Por fim, na aba "Instalação", o admin copia o snippet HTML pronto e cola no site da empresa.

**Why this priority**: Sem configuração funcional, o widget vai ao ar com defaults genéricos, sem identidade visual e sem termos LGPD válidos — bloqueando o uso real (LGPD impede envio de mensagens). É P1 porque é pré-requisito para qualquer tenant de produção colocar o widget em uso.

**Independent Test**: Tenant admin abre **CRM → Configurações → Live Chat**, define cor `#7A9E7E`, ícone `support`, posição `bottom_left`, nome "Clínica Exemplo", mensagem de boas-vindas customizada, preenche o texto LGPD, salva, e copia o snippet da aba "Instalação". Cola em uma página de teste e verifica que o widget aparece com as cores, ícone e mensagens configuradas.

**Acceptance Scenarios**:

1. **Given** o tenant é provisionado pela primeira vez, **When** o admin acessa o painel de configuração do widget, **Then** existe exatamente uma configuração (1:1 com o tenant) já criada com defaults seguros: `is_enabled = true`, cor padrão `#2563EB`, ícone `chat`, posição `bottom_right`, mensagem de boas-vindas padrão.
2. **Given** o admin edita campos da aba "Aparência", **When** altera cor, ícone ou texto, **Then** o preview ao vivo (renderizado dentro do CRM) reflete a mudança imediatamente, sem necessidade de salvar.
3. **Given** o admin não preencheu o texto LGPD (`privacy_policy_text` vazio), **When** abre a aba "Privacidade / LGPD", **Then** o sistema exibe alerta visível: "⚠️ Configure os termos de privacidade. O widget exibirá um texto genérico enquanto este campo estiver vazio." e os widgets em produção exibem um aviso genérico de coleta de dados.
4. **Given** o admin configura `allowed_domains` com uma lista de domínios, **When** o widget é carregado a partir de um domínio que não está na lista, **Then** o backend rejeita as requisições públicas e WebSocket com `403 Forbidden`.
5. **Given** o admin copia o snippet da aba "Instalação", **When** cola em qualquer página HTML que use o `widget_token` do tenant, **Then** o widget é carregado de forma assíncrona usando o token público (que é fixo, imutável e não-secreto).
6. **Given** o admin altera o toggle geral para `is_enabled = false`, **When** confirma a desabilitação, **Then** todas as conversas `open` recebem mensagem automática de encerramento ("O atendimento foi encerrado pelo sistema."), passam para `status = resolved` com `ended_by = system_disable`, e novas visitas ao site exibem "No momento o atendimento está indisponível." sem campo de envio.

---

### User Story 3 — Atendente gerencia múltiplas conversas simultâneas no CRM após transbordo (Priority: P2)

Após o transbordo da IA para humano (Spec 006), a conversa aparece no CRM do atendente: na coluna esquerda, lista de conversas ativas (atribuídas a ele ou ao seu departamento); na coluna direita, conversa selecionada com histórico completo e campo de envio. O atendente pode ter várias conversas abertas simultaneamente (limitado por `max_simultaneous_chats` da Spec 005), respondendo a cada uma. Conversas `resolved` e `abandoned` não aparecem na lista. Quando o CRM está em background, o atendente recebe notificação do navegador para novas conversas e novas mensagens. O atendente também encerra a conversa manualmente clicando em "Encerrar conversa".

**Why this priority**: Sem essa visão multi-conversas, o atendente fica cego aos transbordos e não consegue dar continuidade ao atendimento humano — quebrando a promessa de atendimento omnichannel da plataforma. É P2 porque P1 (História 1) já entrega valor mesmo só com IA, mas em operação real o transbordo é frequente.

**Independent Test**: Tenant configura um sub-agente ou usa o Orchestrator para acionar transbordo. Visitante envia "quero falar com um atendente". Atendente do depto correto, logado no CRM com a aba minimizada, recebe browser notification "Nova conversa de [Anônimo]". Maximiza o CRM, vê a conversa na lista esquerda com badge vermelho, clica nela, lê o histórico e responde. Cliente recebe a resposta no widget em tempo real.

**Acceptance Scenarios**:

1. **Given** uma conversa foi transferida para o departamento do atendente, **When** o atendente acessa o painel do CRM, **Then** a conversa aparece na lista esquerda com badge vermelho ("nova"), nome do visitante (ou "Anônimo" se não identificado) e prévia da última mensagem.
2. **Given** o atendente tem múltiplas conversas abertas (até `max_simultaneous_chats`), **When** seleciona uma na lista, **Then** o painel direito carrega o histórico completo e permite enviar mensagens enquanto as demais conversas continuam visíveis na lista com seus badges atualizados em tempo real.
3. **Given** o CRM do atendente está em background ou minimizado, **When** chega uma nova conversa atribuída ou nova mensagem em conversa aberta, **Then** o sistema emite browser notification com título e prévia da mensagem; se a aba estiver focada na conversa em questão, apenas o indicador visual na lista é atualizado (sem notificação).
4. **Given** uma conversa está em atendimento humano, **When** o atendente clica em "Encerrar conversa", **Then** a conversa transita para `status = resolved` com `ended_by = attendant`, o widget do visitante exibe "O atendimento foi encerrado.", e a conversa some da lista de ativas do atendente.
5. **Given** o visitante envia uma nova mensagem após o encerramento manual pelo atendente, **When** a mensagem chega ao backend, **Then** uma nova conversa é iniciada com o Orchestrator (não reabre a anterior), seguindo o fluxo inicial.
6. **Given** uma conversa em atendimento humano fica sem mensagens por mais de `inactivity_close_hours` (default 24h), **When** o job de inatividade roda, **Then** a conversa é encerrada com `ended_by = system_inactivity` e some da lista do atendente.

---

### User Story 4 — Visitante retorna ao site e retoma a conversa de onde parou (Priority: P2)

Um visitante que já conversou anteriormente volta ao site do tenant. O widget reconhece o navegador (via `anonymous_id` em `localStorage`, sem fingerprinting), verifica o status da conversa anterior e age de acordo: se `open`, retoma a conversa exatamente onde parou (com histórico completo); se `resolved`, mostra o histórico em modo somente leitura com botão "Iniciar nova conversa"; se `abandoned`, inicia automaticamente uma nova conversa. Quando uma nova conversa é iniciada após uma `resolved`, o Orchestrator recebe como contexto as últimas 50 mensagens da anterior (limite configurável) para manter continuidade sem reprocessar histórico inteiro.

**Why this priority**: Continuidade de experiência é diferencial competitivo: visitantes que voltam depois de algumas horas ou dias não querem repetir tudo. P2 porque o MVP (P1) já entrega valor sem isso, mas a satisfação aumenta significativamente com retomada inteligente.

**Independent Test**: Visitante envia mensagens, fecha o navegador (sem limpar `localStorage`), volta no dia seguinte e abre o widget — vê o histórico completo e continua de onde parou. Em outro caso, espera a conversa ser marcada como `abandoned` (após 8h sem mensagem) e volta — uma nova conversa é iniciada automaticamente.

**Acceptance Scenarios**:

1. **Given** o visitante já tem `omnidesk_visitor_id` e `omnidesk_conversation_id` em `localStorage` apontando para uma conversa `open` com IA, **When** abre o widget, **Then** o histórico é carregado via REST e o WebSocket conecta para receber mensagens em tempo real; a IA continua sem solicitar dados já fornecidos.
2. **Given** o visitante tem uma conversa `open` em atendimento humano, **When** abre o widget, **Then** o histórico é carregado e as próximas mensagens chegam via WebSocket; a conversa permanece atribuída ao mesmo atendente (ou departamento) e não retorna à IA.
3. **Given** a conversa anterior está `resolved`, **When** o visitante abre o widget, **Then** o painel exibe o histórico em modo somente leitura e um botão proeminente "Iniciar nova conversa".
4. **Given** o visitante clica em "Iniciar nova conversa" após uma `resolved`, **When** envia a primeira mensagem, **Then** uma nova `conversation` é criada e o Orchestrator processa a mensagem com as últimas 50 mensagens da conversa anterior anexadas ao contexto.
5. **Given** a conversa anterior está `abandoned`, **When** o visitante abre o widget, **Then** uma nova conversa é iniciada automaticamente seguindo o fluxo inicial (welcome, pré-chat se configurado, LGPD).
6. **Given** o visitante limpou o `localStorage` (ou está em outro navegador), **When** abre o widget, **Then** um novo `anonymous_id` é gerado e uma nova conversa é iniciada do zero, sem vínculo com o histórico anterior.

---

### User Story 5 — Sistema gerencia ciclo de vida das conversas automaticamente (Priority: P2)

O sistema executa rotinas em background para manter a base de conversas saudável: conversas com IA sem atividade por `abandonment_timeout_hours` (default 8h) são marcadas como `abandoned`; conversas com humano sem atividade por `inactivity_close_hours` (default 24h) são encerradas com `ended_by = system_inactivity`. O timer reinicia a cada nova mensagem. A IA pode encerrar a conversa naturalmente ao detectar conclusão do fluxo (agendamento confirmado, dúvida resolvida) — envia despedida, marca `resolved` com `ended_by = ai_agent`. Esses ciclos liberam fila de atendentes, evitam conversas zumbis e geram métricas confiáveis.

**Why this priority**: Mantém a base de conversas higiênica, libera vagas de atendimento simultâneo e reduz ruído nas listas. P2 porque o sistema funciona sem isso (conversas só ficam abertas indefinidamente), mas em operação real essas regras são essenciais para escala.

**Independent Test**: Configurar timeouts mais curtos para teste (ex.: 1 hora). Iniciar conversa com IA, parar de responder, esperar o timeout e verificar que ela aparece como `abandoned` no CRM. Em outro caso, fazer transbordo para humano, parar de trocar mensagens, esperar `inactivity_close_hours` e verificar encerramento automático.

**Acceptance Scenarios**:

1. **Given** uma conversa `open` com IA não recebe mensagens há mais de `abandonment_timeout_hours`, **When** o job de abandono executa (a cada hora), **Then** a conversa transita para `status = abandoned` e some da lista de conversas ativas (não tem `ended_by` específico, é abandono e não encerramento).
2. **Given** uma conversa `open` em atendimento humano não recebe mensagens há mais de `inactivity_close_hours`, **When** o job de inatividade executa, **Then** a conversa transita para `status = resolved` com `ended_by = system_inactivity` e o widget do visitante (se aberto) é notificado.
3. **Given** uma conversa com humano está inativa há tempo próximo do limite, **When** o atendente ou o visitante envia nova mensagem, **Then** o timer é reiniciado e a conversa permanece `open`.
4. **Given** a IA detecta conclusão natural do fluxo (ex.: agendamento confirmado), **When** decide encerrar, **Then** envia mensagem de despedida, marca `status = resolved` com `ended_by = ai_agent` e `ended_at` preenchido; o widget exibe a mensagem de encerramento e botão "Iniciar nova conversa".
5. **Given** uma conversa está em atendimento humano, **When** o job de abandono executa, **Then** essa conversa NÃO é marcada como `abandoned` — somente conversas com IA seguem essa regra; conversas com humano seguem `inactivity_close_hours`.

---

### User Story 6 — Visitante envia anexos durante a conversa (Priority: P3)

Visitante clica no ícone de anexo no widget, seleciona um arquivo (imagem ou documento) de até 10 MB, o arquivo é enviado para armazenamento de mídia, validado por tipo MIME (não apenas extensão) e a URL aparece como mensagem na conversa. IA e atendente veem a mensagem com o anexo (preview de imagem inline, link de download para documento).

**Why this priority**: Útil em vários cenários (cliente envia print de erro, foto de documento, planilha), mas o atendimento básico funciona sem anexos. P3 porque é incremental ao MVP texto-only.

**Independent Test**: Visitante anexa uma imagem JPG de 2 MB → upload bem-sucedido, mensagem aparece com preview no widget e no CRM. Tentar arquivo de 15 MB → rejeitado com mensagem clara. Tentar arquivo `.exe` renomeado para `.pdf` → backend detecta MIME real e rejeita.

**Acceptance Scenarios**:

1. **Given** o visitante seleciona uma imagem JPG/PNG/GIF/WEBP de até 10 MB, **When** envia, **Then** o arquivo é armazenado, a URL retornada é enviada via WebSocket e exibida como mensagem com preview inline no widget e no CRM.
2. **Given** o visitante seleciona um documento PDF/DOCX/XLSX de até 10 MB, **When** envia, **Then** o arquivo é armazenado e a mensagem aparece com nome original, tamanho e link de download.
3. **Given** o visitante tenta enviar arquivo > 10 MB, **When** seleciona, **Then** o widget rejeita imediatamente com mensagem clara "Arquivo excede o tamanho máximo de 10 MB" e não envia ao backend.
4. **Given** o visitante tenta enviar um arquivo com extensão permitida mas tipo MIME inválido, **When** o backend recebe, **Then** o upload é rejeitado com erro de validação MIME.
5. **Given** o atendente envia anexo a partir do CRM, **When** o visitante recebe, **Then** o anexo aparece no widget com mesmo comportamento (preview ou link de download).

---

### Edge Cases

- **Visitante limpa localStorage no meio da conversa**: ao reabrir o widget, novo `anonymous_id` é gerado e uma nova conversa é iniciada — a anterior fica órfã mas preservada no CRM.
- **WebSocket cai durante a conversa**: o widget exibe banner discreto "Reconectando…", reconecta com backoff exponencial (1s, 2s, 4s, … até 30s) e enfileira mensagens digitadas durante a queda para envio assíncrono.
- **Visitante abre o widget em múltiplas abas do mesmo navegador**: ambas usam o mesmo `anonymous_id` e a mesma conversa; mensagens chegam em todas via WebSocket.
- **Tenant desabilita o widget enquanto conversa está ativa**: a conversa é encerrada com mensagem automática ("O atendimento foi encerrado pelo sistema."), `ended_by = system_disable`; o visitante vê a mensagem e o widget passa a exibir "indisponível" sem campo de envio em visitas futuras.
- **`privacy_policy_text` está vazio**: o widget exibe um texto genérico de aviso de coleta de dados (suficiente para coletar consentimento mas não substitui política específica) e o CRM exibe alerta para o tenant configurar.
- **Visitante recusa permissão de notificação no browser (atendente)**: o atendente continua vendo conversas e mensagens em tempo real na lista do CRM, mas sem notificações nativas — gerenciamento de permissão fica disponível em CRM → Configurações → Notificações.
- **Conversa muito longa (>50 mensagens) é encerrada e visitante inicia nova**: a IA recebe apenas as últimas 50 mensagens como contexto para controlar custo e tamanho de prompt.
- **Visitante envia mensagem após conversa `resolved` sem clicar em "Iniciar nova conversa"**: o widget cria automaticamente uma nova conversa com o Orchestrator (fluxo inicial completo: welcome, LGPD se ainda válido, etc.).
- **Mensagem `system_event` (eventos internos como "atendente assumiu")**: aparecem centralizadas no widget e no CRM, NÃO são processadas pela IA, NÃO entram no contexto enviado ao modelo.
- **Origem (Origin header) não confere com `allowed_domains`**: backend retorna `403 Forbidden` em REST e fecha conexão WebSocket; o widget exibe erro genérico "Atendimento indisponível neste site".

## Requirements *(mandatory)*

### Functional Requirements

#### Carregamento e identificação do widget

- **FR-001**: O sistema MUST disponibilizar o widget como script JavaScript carregado de forma assíncrona via CDN, sem bloquear a renderização da página host.
- **FR-002**: O sistema MUST identificar o tenant nas requisições públicas via `widget_token` (UUID público, fixo, imutável, não-secreto, gerado no provisionamento).
- **FR-003**: O sistema MUST gerar um `anonymous_id` (UUID) na primeira visita do navegador via API criptográfica nativa do browser e persistir em `localStorage`, sem usar fingerprinting de dispositivo (canvas, fontes, etc.).
- **FR-004**: O sistema MUST persistir em `localStorage` o `anonymous_id` do visitante e o ID da conversa ativa, e usar ambos para retomada em visitas subsequentes.
- **FR-005**: O sistema MUST validar o header `Origin` em cada requisição pública (REST e WebSocket) quando `allowed_domains` está preenchido; origens não autorizadas MUST receber `403 Forbidden`. Lista vazia significa sem restrição.

#### Conversas e ciclo de vida

- **FR-006**: O sistema MUST suportar três status de conversa: `open` (em andamento), `resolved` (encerrada manualmente ou pela IA) e `abandoned` (encerrada por timeout sem atividade com IA).
- **FR-007**: O sistema MUST registrar `ended_by` para conversas resolvidas, com valores: `attendant`, `ai_agent`, `system_inactivity`, `system_disable`.
- **FR-008**: O sistema MUST marcar conversas `open` em atendimento por IA como `abandoned` quando ficam sem novas mensagens por mais de `abandonment_timeout_hours` (default 8h, configurável por tenant).
- **FR-009**: O sistema MUST encerrar automaticamente conversas `open` em atendimento humano quando ficam sem novas mensagens por mais de `inactivity_close_hours` (default 24h, configurável por tenant), com `ended_by = system_inactivity`.
- **FR-010**: O sistema MUST reiniciar o timer de abandono/inatividade a cada nova mensagem na conversa.
- **FR-011**: O sistema MUST permitir que o atendente humano encerre manualmente uma conversa atribuída a ele, marcando `ended_by = attendant`.
- **FR-012**: O sistema MUST permitir que a IA encerre a conversa quando detecta conclusão natural do fluxo, enviando mensagem de despedida e marcando `ended_by = ai_agent`.
- **FR-013**: Quando o tenant desabilita o widget (`is_enabled = false`), o sistema MUST encerrar todas as conversas `open` com mensagem automática "O atendimento foi encerrado pelo sistema." e `ended_by = system_disable`.

#### Retomada e contexto

- **FR-014**: Ao retornar com conversa `open` (IA ou humano), o sistema MUST retomar a conversa carregando o histórico via REST e conectando WebSocket para mensagens em tempo real, sem solicitar dados já fornecidos.
- **FR-015**: Ao retornar com conversa `resolved`, o sistema MUST exibir o histórico em modo somente leitura e um botão "Iniciar nova conversa".
- **FR-016**: Ao retornar com conversa `abandoned`, o sistema MUST iniciar automaticamente nova conversa (fluxo inicial completo).
- **FR-017**: Ao iniciar nova conversa após uma `resolved` para o mesmo visitante, o sistema MUST anexar as últimas 50 mensagens da conversa anterior ao contexto enviado ao Orchestrator (limite configurável via variável de ambiente).

#### LGPD e privacidade

- **FR-018**: O sistema MUST exigir aceite dos termos de privacidade (LGPD) antes de permitir o envio de qualquer mensagem; o botão de envio MUST permanecer desabilitado até o aceite.
- **FR-019**: O sistema MUST registrar o momento do aceite em `lgpd_consent_at` na conversa.
- **FR-020**: O sistema MUST exibir o `privacy_policy_text` configurado pelo tenant; quando vazio, MUST exibir um texto genérico de aviso de coleta de dados e MUST exibir alerta visível para o tenant no CRM.
- **FR-021**: O sistema MUST armazenar apenas os 3 primeiros octetos do IP do visitante (IPv4) em `metadata`, junto com user-agent, página de origem, título e referrer; não armazenar IP completo.

#### Comunicação em tempo real

- **FR-022**: O sistema MUST entregar mensagens em tempo real via WebSocket entre visitante e backend (canal por conversa) e entre atendente e backend (canal por sessão CRM).
- **FR-023**: O sistema MUST emitir indicador "digitando…" via WebSocket quando agente, atendente ou visitante estiver compondo (com debounce de 1 segundo no lado visitante).
- **FR-024**: O sistema MUST reconectar automaticamente após queda de WebSocket usando backoff exponencial (1s, 2s, 4s, … até 30s máximo) e enfileirar mensagens digitadas durante a desconexão para envio ao reconectar.
- **FR-025**: O sistema MUST exibir banner discreto no widget quando desconectado.
- **FR-026**: O sistema MUST atualizar o badge de mensagens não lidas no launcher quando o widget está fechado e zerar quando o visitante o abre (evento `messages.read`).

#### Configuração no CRM (Tenant Admin)

- **FR-027**: O sistema MUST criar automaticamente uma `widget_config` única por tenant durante o provisionamento, com defaults seguros (cor `#2563EB`, ícone `chat`, posição `bottom_right`, mensagem de boas-vindas padrão, `is_enabled = true`).
- **FR-028**: O sistema MUST permitir ao admin do tenant editar: cor primária (hex), ícone do launcher (chat/message/support), posição (bottom_right/bottom_left), nome da empresa, mensagem de boas-vindas, placeholder do campo, formulário pré-chat (toggle + campos opcionais nome/email/telefone com flag de obrigatório), texto e URL da política de privacidade, timeouts de abandono e inatividade, lista de domínios autorizados.
- **FR-029**: O sistema MUST exibir preview ao vivo do widget no painel de configuração, atualizando em tempo real conforme o admin edita os campos, sem necessidade de salvar; o preview consome a API pública do widget com o `widget_token` do próprio tenant e funciona sem restrição de domínio.
- **FR-030**: O sistema MUST disponibilizar trecho HTML pronto para copiar na aba "Instalação", contendo o `widget_token` do tenant e o script de carregamento.
- **FR-031**: O sistema MUST permitir liga/desliga geral do widget via toggle (`is_enabled`); ao desligar, encerrar todas as conversas abertas conforme FR-013.

#### Múltiplas conversas para atendente (CRM)

- **FR-032**: O sistema MUST exibir no CRM do atendente, à esquerda, lista de conversas ativas atribuídas a ele ou ao seu departamento, com badge colorido por status (nova / em andamento / aguardando cliente).
- **FR-033**: O sistema MUST exibir conversa selecionada à direita com histórico completo e campo de envio.
- **FR-034**: O sistema MUST permitir ao atendente ter múltiplas conversas abertas simultaneamente, respeitando o limite `max_simultaneous_chats` definido na Spec 005.
- **FR-035**: Conversas `resolved` e `abandoned` MUST NOT aparecer na lista de conversas ativas do atendente.

#### Notificações de browser (CRM)

- **FR-036**: O sistema MUST solicitar permissão de notificação do browser na primeira sessão do atendente.
- **FR-037**: O sistema MUST emitir browser notification para o atendente quando: nova conversa é atribuída, nova mensagem chega em conversa aberta, conversa é transferida ao atendente.
- **FR-038**: O sistema MUST emitir notificação somente quando a aba do CRM está em background ou minimizada; se o atendente está com a conversa focada, atualizar apenas o indicador visual na lista (sem notificação).
- **FR-039**: O sistema MUST disponibilizar gerenciamento de permissão em **CRM → Configurações → Notificações**.

#### Anexos

- **FR-040**: O sistema MUST aceitar upload de imagens (jpg, png, gif, webp) e documentos (pdf, docx, xlsx) com tamanho máximo de 10 MB por arquivo.
- **FR-041**: O sistema MUST validar o tipo MIME real do arquivo no backend (não apenas pela extensão); arquivos com MIME inválido MUST ser rejeitados.
- **FR-042**: O sistema MUST exibir mensagem de erro clara no widget quando o visitante tenta enviar arquivo > 10 MB.

#### Mensagens e tipos

- **FR-043**: O sistema MUST suportar tipos de remetente em mensagens: `visitor`, `ai_agent`, `attendant`, `system`.
- **FR-044**: O sistema MUST suportar tipos de conteúdo: `text`, `image`, `file`, `system_event`.
- **FR-045**: Mensagens `system_event` MUST NOT ser processadas pelo Agente de IA (não entram no contexto enviado ao modelo).

#### Acessos e permissões

- **FR-046**: Apenas roles com permissão definida na Spec 002 (Auth) MUST poder alterar a configuração do widget; a definição precisa do role permitido fica a cargo dessa spec.

### Key Entities *(include if feature involves data)*

- **Configuração do Widget**: configuração visual e comportamental por tenant (1:1). Inclui token público, toggle geral, identidade visual (cor, ícone, posição, nome, mensagens), formulário pré-chat opcional, texto LGPD, timeouts, domínios autorizados.
- **Conversa**: sessão de troca de mensagens entre um visitante e o sistema (IA e/ou atendente humano). Tem ciclo de vida (`open` → `resolved` ou `abandoned`), pode ter ticket vinculado após transbordo, registra aceite LGPD, agente/atendente atual, departamento, metadados de origem (página, referrer, UA, IP parcial).
- **Visitante**: identidade anônima de quem usa o widget. Identificada por `anonymous_id` (UUID gerado no browser), pode ser enriquecida com nome/email/telefone via formulário pré-chat ou durante a conversa. Sem fingerprinting.
- **Mensagem**: unidade individual de comunicação dentro de uma conversa. Tem remetente (tipo + ID), tipo de conteúdo, texto e/ou anexo, timestamp e flag de leitura. Tabela compartilhada com o módulo WhatsApp (Spec 06).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: O widget carrega no site do tenant em até 500 ms após o carregamento da página host (sem bloquear renderização) em conexões 4G típicas.
- **SC-002**: 100% das tentativas de envio de mensagem antes do aceite LGPD são bloqueadas pelo widget (botão de envio desabilitado).
- **SC-003**: Tempo até a primeira resposta da IA é inferior a 5 segundos em 95% dos casos, medido do envio da mensagem do visitante até a chegada da resposta no widget.
- **SC-004**: 100% das origens não autorizadas (quando `allowed_domains` está configurado) recebem `403 Forbidden` em REST e WebSocket.
- **SC-005**: Conversas com IA inativas por mais de `abandonment_timeout_hours` são marcadas como `abandoned` em até 1 hora após o limite (granularidade do job).
- **SC-006**: Conversas com humano inativas por mais de `inactivity_close_hours` são encerradas em até 1 hora após o limite.
- **SC-007**: Após queda de rede, o widget reconecta em até 30 segundos em 99% dos casos sem perda de mensagens digitadas durante a queda.
- **SC-008**: Um tenant admin completa configuração inicial do widget (aparência + LGPD + instalação) em até 5 minutos partindo de um tenant recém-provisionado.
- **SC-009**: Um atendente em CRM minimizado recebe notificação do navegador para nova conversa em até 2 segundos após a atribuição.
- **SC-010**: Conversa `open` retomada por um visitante que volta ao site exibe histórico completo em até 1 segundo após abrir o widget.
- **SC-011**: 100% dos uploads de arquivos com tipo MIME inválido (mesmo com extensão permitida) são rejeitados pelo backend.
- **SC-012**: Conversas `resolved` e `abandoned` nunca aparecem na lista de conversas ativas do atendente (verificável por inspeção da lista).
- **SC-013**: Preview ao vivo do widget no CRM reflete alterações de configuração em até 200 ms após o admin editar um campo.

## Assumptions

- O Agente Orchestrator (Spec 006) já está implementado e disponível como ponto de entrada de mensagens novas — esta spec apenas integra com ele via fila de mensagens recebidas.
- A Spec 005 (Departments/Attendants) já define `max_simultaneous_chats` e o conceito de "departamento padrão do tenant" usado em transbordos.
- A Spec 002 (Auth) define o role com permissão para alterar configuração do widget; essa spec não decide o role final — apenas referencia o controle de acesso.
- A tabela `messages` é compartilhada com o módulo WhatsApp (Spec 06) — o canal é diferenciado pelo campo `channel` em `conversations`.
- O ambiente de produção tem CDN (Cloudflare) configurada para servir o `loader.js` do widget — o setup da CDN é tarefa de infra, não desta spec.
- O armazenamento de mídia (MinIO) já está configurado em `tenant-{slug}` conforme convenção multi-tenant (CLAUDE.md §4).
- Conversas sem `lgpd_consent_at` preenchido nunca chegam à fila de mensagens recebidas — o widget bloqueia o envio antes (defesa em profundidade: backend também valida).
- Visitantes que limpam `localStorage` perdem a continuidade da conversa anterior — comportamento esperado e documentado, sem fallback por fingerprinting.
- A Spec 006 define o comportamento de transbordo da IA para humano (`transfer_to_human`) — esta spec apenas exibe o resultado no CRM e oferece UI de encerramento manual.
- O limite de 50 mensagens de contexto em reabertura é configurável via variável de ambiente do backend (não exposto no painel do tenant) para permitir ajuste fino sem impacto contratual.
