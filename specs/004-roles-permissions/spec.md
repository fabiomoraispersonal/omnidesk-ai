# Especificação: Roles e Permissões

**Feature Branch**: `004-roles-permissions`
**Criada em**: 2026-05-06
**Status**: Draft
**Input**: Descrição do usuário detalhando contextos de acesso (Painel Admin SaaS e CRM do Tenant), 4 roles (`saas_admin`, `tenant_admin`, `supervisor`, `attendant`), matriz de permissões cruzada com Specs 01–11, regras de isolamento multi-tenant, impersonation, criação de usuários e tabela de tokens.

## Visão Geral

Esta spec é **transversal**: define o modelo de roles e a matriz de permissões usados pelas demais features do OmniDesk. Não introduz nova UI nem novo fluxo de negócio próprio — estabelece o contrato de autorização que todas as outras specs (auth, provisionamento, departamentos, live chat, agentes, tickets, WhatsApp, notificações, agenda, auditoria) consomem para decidir quem pode fazer o quê.

O OmniDesk possui **dois contextos de acesso completamente separados**:

| Contexto | URL | Quem acessa |
|---|---|---|
| **Painel Admin (SaaS)** | `admin.omnicare.ia.br` | Apenas o Operador SaaS |
| **CRM do Tenant** | `{slug}.omnicare.ia.br` | Usuários internos da empresa (tenant) |

As roles são independentes entre contextos — um usuário nunca pertence aos dois ao mesmo tempo, com a única exceção do token de impersonation (`saas_admin` acessando temporariamente o CRM de um tenant).

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Modelo de roles consistente e aplicável (Priority: P1)

Como **arquiteto/desenvolvedor de qualquer feature do OmniDesk**, preciso de um conjunto único e bem definido de roles e de uma matriz de permissões consultável, para que cada endpoint, tela e ação respeite as mesmas regras de autorização sem reinventá-las localmente.

**Why this priority**: Toda a segurança do produto depende deste modelo. Sem ele, cada spec inventaria suas próprias roles e teríamos divergências, escaladas de privilégio e bugs de autorização. É bloqueante para qualquer feature que tenha controle de acesso.

**Independent Test**: Pode ser validada em revisão lendo a matriz: para qualquer combinação `(role, ação)` listada nas seções 4.1–4.12, o leitor consegue afirmar sem ambiguidade se é permitida ou negada.

**Acceptance Scenarios**:

1. **Given** um endpoint protegido em qualquer spec do OmniDesk, **When** o desenvolvedor consulta esta spec, **Then** existe uma linha clara na matriz indicando se cada role pode ou não executar a ação.
2. **Given** uma ação não listada explicitamente na matriz, **When** ocorre durante implementação, **Then** o desenvolvedor segue o princípio de menor privilégio (negar por padrão) e abre um update desta spec.
3. **Given** dois desenvolvedores diferentes implementando specs diferentes, **When** ambos precisam autorizar a mesma role, **Then** chegam à mesma decisão sem conflito.

---

### User Story 2 — Operador SaaS gerencia a plataforma (Priority: P1)

Como **Operador SaaS** (`saas_admin`), preciso acessar o Painel Admin com permissões totais sobre tenants, templates de agentes globais e saúde do sistema, para operar a plataforma na V1 (provisionamento e billing manuais).

**Why this priority**: Sem `saas_admin` operacional, ninguém provisiona tenants nem responde a incidentes — é a role que mantém o produto vivo.

**Independent Test**: Login no painel admin com a única conta `saas_admin` permite executar todas as ações da seção 4.1; nenhuma outra role consegue acessar `admin.omnicare.ia.br`.

**Acceptance Scenarios**:

1. **Given** um usuário com role `saas_admin`, **When** acessa `admin.omnicare.ia.br`, **Then** vê o painel completo com listagem de tenants, métricas de saúde e gestão de templates.
2. **Given** um usuário com role `tenant_admin`, `supervisor` ou `attendant`, **When** tenta acessar `admin.omnicare.ia.br`, **Then** é bloqueado (sem login válido para esse contexto).
3. **Given** uma tentativa de criar um segundo usuário `saas_admin` via CRM do tenant, **When** a operação é submetida, **Then** é rejeitada — `saas_admin` é exclusivo do painel admin.

---

### User Story 3 — Tenant Admin assume o CRM no provisionamento (Priority: P1)

Como **super admin do tenant** (`tenant_admin`), preciso ser criado automaticamente no provisionamento e ter acesso total ao CRM do meu tenant desde o primeiro login, para configurar a operação (departamentos, atendentes, canais, agentes de IA).

**Why this priority**: É a porta de entrada do cliente final. Sem ele, o tenant recém-provisionado fica inutilizável.

**Independent Test**: Após provisionamento bem-sucedido (Spec 03), o e-mail informado recebe credenciais de `tenant_admin` e o primeiro login lhe dá acesso a todas as ações da hierarquia (≥ supervisor ≥ attendant).

**Acceptance Scenarios**:

1. **Given** um tenant recém-provisionado, **When** o destinatário cadastrado faz o primeiro login no `{slug}.omnicare.ia.br`, **Then** recebe role `tenant_admin` e acessa todas as configurações operacionais e de sistema do CRM.
2. **Given** um `tenant_admin`, **When** executa qualquer ação permitida a `supervisor` ou `attendant`, **Then** a ação é aceita sem necessidade de role adicional.
3. **Given** dados de outro tenant, **When** `tenant_admin` tenta acessá-los por manipulação direta de URL ou API, **Then** o middleware de tenant bloqueia o acesso antes da autorização de role.

---

### User Story 4 — Supervisor opera departamentos sem mexer no sistema (Priority: P2)

Como **supervisor**, preciso gerenciar departamentos, atendentes, agentes de IA, pipeline Kanban, configuração do widget e templates de WhatsApp, mas **não** alterar configurações de sistema (slug, CNPJ, OpenAI, domínios autorizados, ligar/desligar widget, política de cancelamento, access token do WhatsApp), para que a separação operação ↔ sistema fique clara.

**Why this priority**: Permite ao tenant_admin delegar operação sem perder governança de configurações sensíveis. Importante para clínicas com gestor operacional separado do dono.

**Independent Test**: Login como `supervisor` permite todas as ações marcadas com ✅ na coluna `supervisor` da matriz e bloqueia explicitamente todas as ❌, sem exceção.

**Acceptance Scenarios**:

1. **Given** um `supervisor` autenticado, **When** tenta editar dados cadastrais do tenant (slug, CNPJ) ou alternar widget global, **Then** a UI oculta o controle e a API retorna negação de autorização.
2. **Given** um `supervisor`, **When** cria um novo `attendant` ou edita um agente de IA, **Then** a operação é aceita.
3. **Given** um `supervisor`, **When** tenta visualizar o Access Token do WhatsApp, **Then** vê apenas a indicação "configurado/não configurado", sem o valor.

---

### User Story 5 — Atendente atua apenas no seu escopo (Priority: P2)

Como **attendant**, preciso enxergar e atuar apenas em conversas, tickets e respostas pré-formadas que me dizem respeito (departamentos aos quais pertenço; itens atribuídos a mim ou criados por mim), para evitar exposição de dados de outros atendentes/departamentos e manter a UX focada.

**Why this priority**: Princípio de menor privilégio aplicado à role mais numerosa do sistema. Falhas aqui criam vazamento horizontal entre atendentes.

**Independent Test**: Um `attendant` vinculado ao Departamento A não consegue listar nem abrir conversas/tickets do Departamento B (mesmo dentro do mesmo tenant), e não enxerga respostas pré-formadas alheias.

**Acceptance Scenarios**:

1. **Given** `attendant` vinculado apenas ao Departamento A, **When** lista conversas, **Then** recebe apenas conversas atribuídas a ele ou ao Departamento A.
2. **Given** `attendant` que pertence aos Departamentos A e B, **When** lista tickets, **Then** vê tickets dos dois departamentos, mas nunca de C.
3. **Given** `attendant` tentando editar resposta pré-formada de outro atendente, **When** submete, **Then** é negado pela API.
4. **Given** `attendant` tentando assumir manualmente um ticket disponível em seu departamento, **When** clica em "Assumir", **Then** a operação é aceita.

---

### User Story 6 — Operador SaaS impersona tenant para suporte (Priority: P3)

Como **Operador SaaS**, preciso acessar temporariamente o CRM de um tenant via token de impersonation curto (5 min, não renovável) com banner de aviso permanente e auditoria completa, para investigar incidentes reportados pelo cliente sem solicitar credenciais dele.

**Why this priority**: Suporte é viável sem isso (pedindo credenciais ao cliente), mas em V1 a experiência piora e a rastreabilidade fica comprometida. Por isso P3 e não P1/P2.

**Independent Test**: A partir do painel admin, gerar um token de impersonation para um tenant abre o CRM dele em uma nova aba com barra de aviso visível; após 5 minutos, o token expira e exige nova geração; todas as ações do período aparecem em auditoria com `impersonated_by: "saas_admin"`.

**Acceptance Scenarios**:

1. **Given** `saas_admin` no painel admin, **When** clica em "Impersonar tenant X", **Then** recebe um JWT de impersonation com claims `role: saas_admin`, `impersonating: true`, `tenant_slug: X`, `impersonated_by: "saas_admin"` e TTL de 5 min.
2. **Given** sessão de impersonation ativa, **When** o `saas_admin` navega no CRM, **Then** uma barra fixa no topo indica modo impersonation e identifica o tenant.
3. **Given** ações realizadas durante impersonation, **When** consultadas em auditoria (Spec 11), **Then** cada evento traz o campo `impersonated_by: "saas_admin"`.
4. **Given** token de impersonation expirado, **When** o `saas_admin` tenta uma nova ação no CRM, **Then** é redirecionado a regenerar o token no painel admin (não há refresh).

---

### User Story 7 — Desativação corta acesso instantaneamente (Priority: P3)

Como **tenant_admin**, ao desativar um usuário, preciso que ele perca acesso imediatamente (sessões invalidadas em todos os dispositivos) preservando o histórico de atendimentos, para responder rápido a desligamentos sem perder rastreabilidade.

**Why this priority**: Importante para compliance/segurança, mas não bloqueia a operação inicial — desligamentos não acontecem no dia 1 do tenant.

**Independent Test**: Após desativação, qualquer requisição autenticada do usuário desativado falha em < 1 segundo, e o histórico de tickets/conversas dele continua acessível para `tenant_admin`/`supervisor`.

**Acceptance Scenarios**:

1. **Given** usuário ativo com sessão aberta, **When** `tenant_admin` o desativa, **Then** `is_active = false` é gravado e as sessões/refresh tokens dele no Redis são invalidados.
2. **Given** usuário recém-desativado com access token ainda dentro do TTL de 15 min, **When** faz uma requisição autenticada, **Then** é negada na primeira verificação após a invalidação.
3. **Given** usuário desativado, **When** `tenant_admin`/`supervisor` consulta tickets antigos, **Then** o nome do atendente aparece no histórico (apenas marcado como desativado), mas ele não recebe mais nada novo.

---

### Edge Cases

- **Tenant bloqueado** (Spec 02): qualquer login, refresh ou ação de qualquer role do tenant é negado, mesmo `tenant_admin`. Apenas `saas_admin` continua operando o tenant pelo painel admin.
- **Promoção/rebaixamento de role**: alteração de role de um usuário ativo invalida todos os tokens existentes dele (tratamento equivalente ao de desativação) — o usuário precisa logar de novo para receber o novo escopo.
- **Attendant sem departamento vinculado**: comportamento conservador — não enxerga nenhuma conversa nem ticket. Não bloqueia o login (pode usar agenda, perfil, notificações próprias).
- **Tentativa de criar `saas_admin` via API do CRM**: rejeitada em todas as camadas (validação de payload + autorização). A role só é atribuída no provisionamento de sistema.
- **Token de impersonation usado após expiração**: rejeitado com 401; nenhuma renovação automática possível.
- **Conflito de subdomínio**: se um access token do tenant A for usado contra `tenantB.omnicare.ia.br`, o middleware de resolução de tenant bloqueia antes da camada de role.
- **Múltiplos `tenant_admin` no mesmo tenant**: permitido (provisionamento cria 1, mas o `tenant_admin` pode promover outros). Não há limite hard.
- **Self-deactivation**: `tenant_admin` não pode se desativar enquanto for o único `tenant_admin` ativo do tenant — para evitar lockout do tenant.
- **Reativação de usuário**: restaura `is_active = true`, mas exige que o usuário faça novo login (sem reaproveitar sessões antigas).

## Requirements *(mandatory)*

### Functional Requirements

#### Modelo de roles

- **FR-001**: O sistema MUST definir exatamente 4 roles na V1: `saas_admin`, `tenant_admin`, `supervisor`, `attendant`. Nenhuma outra role é introduzida nesta versão.
- **FR-002**: O sistema MUST manter `saas_admin` como role exclusiva do contexto Painel Admin (`admin.omnicare.ia.br`) e as três roles de CRM (`tenant_admin`, `supervisor`, `attendant`) como exclusivas do contexto CRM do Tenant (`{slug}.omnicare.ia.br`).
- **FR-003**: Um usuário MUST pertencer a exatamente um contexto. A única forma de um `saas_admin` aparecer no contexto CRM é via token de impersonation (FR-028).
- **FR-004**: O sistema MUST aplicar hierarquia no contexto CRM — `tenant_admin` herda permissões de `supervisor`, que herda permissões de `attendant`. A herança é cumulativa e silenciosa (não exige declaração explícita por permissão).

#### Matriz de permissões (autoridade)

- **FR-005**: A matriz de permissões deste documento (seções 4.1–4.12 abaixo) MUST ser a fonte única de verdade para autorização. Specs individuais referenciam esta spec — não redefinem permissões localmente.
- **FR-006**: Toda ação não listada na matriz MUST ser tratada como negada por padrão (princípio de menor privilégio). Adicionar nova ação exige update desta spec.
- **FR-007**: O sistema MUST aplicar autorização de role **após** a resolução de tenant (FR-026). Acessos a dados de outro tenant são bloqueados antes mesmo de a role ser avaliada.

#### Painel Admin (Spec 02)

- **FR-008**: `saas_admin` MUST poder: listar/detalhar tenants, criar tenant (provisionar), editar dados cadastrais, bloquear/desbloquear, redefinir senha do super admin do tenant, impersonar, ver métricas de saúde, retentar provisionamento com erro, e gerenciar templates de agentes globais.
- **FR-009**: Nenhuma das três roles de CRM MUST conseguir acessar o Painel Admin.

#### Departamentos e Atendentes (Spec 04)

- **FR-010**: Apenas `tenant_admin` MUST poder criar, editar e desativar departamentos.
- **FR-011**: `tenant_admin` e `supervisor` MUST poder criar, editar e desativar `attendant`s. Apenas `tenant_admin` MUST poder promover um usuário a `supervisor`.
- **FR-012**: Todas as roles de CRM MUST poder alterar o próprio status (online/away/offline) e criar respostas pré-formadas próprias.
- **FR-013**: `attendant` MUST visualizar apenas departamentos aos quais pertence; `tenant_admin` e `supervisor` enxergam todos.

#### Live Chat — Widget e Conversas (Spec 06)

- **FR-014**: Edição de domínios autorizados, alternar widget globalmente e configurações sensíveis MUST ser exclusivos de `tenant_admin`. Aparência, comportamento, termos de privacidade e visualização do código de instalação são acessíveis a `tenant_admin` e `supervisor`.
- **FR-015**: `attendant` MUST ver apenas conversas dos seus departamentos ou atribuídas a si; só pode encerrar manualmente as atribuídas a si.

#### Agentes de IA (Spec 05)

- **FR-016**: Edição de prompt/nome/modelo do Orchestrator, criação/edição/ativação de sub-agentes e uso do playground MUST ser permitido a `tenant_admin` e `supervisor`. Configurações avançadas como `context_window_messages` MUST ser exclusivas de `tenant_admin`.
- **FR-017**: `attendant` MUST não acessar a configuração de agentes de IA.

#### Tickets e Pipeline (Spec 08)

- **FR-018**: `attendant` MUST ver apenas tickets de seus departamentos e editar/encerrar/anotar apenas os atribuídos a si. `tenant_admin` e `supervisor` veem todos os tickets do tenant.
- **FR-019**: Configurar/renomear colunas do pipeline MUST ser permitido a `tenant_admin` e `supervisor` apenas.

#### Contatos (Spec 08)

- **FR-020**: Listagem, busca, criação e edição de contatos MUST ser permitidas a todas as roles de CRM (sem distinção por departamento — contatos são compartilhados no tenant).

#### WhatsApp (Spec 07)

- **FR-021**: Edição de configuração do canal (Phone ID, WABA ID, nome), Access Token e ativação/desativação MUST ser exclusivos de `tenant_admin`. Templates podem ser gerenciados por `tenant_admin` e `supervisor`.
- **FR-022**: O Access Token MUST ser visualizável apenas por `tenant_admin`. `supervisor` enxerga apenas o estado "configurado/não configurado".

#### Notificações, Agenda, Auditoria (Specs 09, 10, 11)

- **FR-023**: Configurar notificações enviadas para clientes do tenant MUST ser permitido a `tenant_admin` e `supervisor`. Preferências de push pessoais e notificações in-app são acessíveis a todas as roles de CRM.
- **FR-024**: Gerenciar profissionais, catálogo de serviços e disponibilidade da agenda MUST ser permitido a `tenant_admin` e `supervisor`. Configuração da política de cancelamento MUST ser exclusiva de `tenant_admin`. Operações do dia-a-dia (criar/cancelar/confirmar agendamento, marcar no-show, reenviar lembrete) são abertas a todas as roles de CRM.
- **FR-025**: Auditoria (visualizar atividade recente no CRM e gerenciar API Keys de auditoria) MUST ser exclusiva de `tenant_admin`.

#### Isolamento Multi-tenant

- **FR-026**: O sistema MUST executar middleware de resolução de tenant (com base no subdomínio) antes de qualquer verificação de role. Requisição com access token de tenant A para subdomínio de tenant B MUST ser rejeitada.
- **FR-027**: Toda query do banco MUST ser filtrada implicitamente pelo `tenant_id` do contexto resolvido.

#### Impersonation

- **FR-028**: O Painel Admin MUST permitir gerar token de impersonation para qualquer tenant (apenas `saas_admin`).
- **FR-029**: O token de impersonation MUST ser um JWT com TTL de 5 minutos, sem refresh token associado, com claims: `role: saas_admin`, `impersonating: true`, `tenant_slug: {slug}`, `impersonated_by: "saas_admin"`.
- **FR-030**: O CRM MUST exibir uma barra de aviso permanente no topo durante toda a sessão de impersonation, identificando o tenant alvo.
- **FR-031**: Toda ação executada durante impersonation MUST ser registrada no log de auditoria (Spec 11) com campo `impersonated_by: "saas_admin"`.

#### Criação e ciclo de vida de usuários

- **FR-032**: O `tenant_admin` inicial MUST ser criado automaticamente no provisionamento (Spec 03), com credenciais entregues ao destinatário cadastrado.
- **FR-033**: Convites para novos usuários MUST poder ser enviados por: `tenant_admin` (qualquer role do CRM, exceto `saas_admin`); `supervisor` (apenas `attendant` e outros `supervisor`).
- **FR-034**: Apenas `tenant_admin` MUST poder desativar/reativar usuários do tenant.
- **FR-035**: Usuários MUST nunca ser deletados fisicamente — apenas desativados (`is_active = false`).
- **FR-036**: Desativação MUST invalidar imediatamente todas as sessões e refresh tokens do usuário no Redis. A próxima requisição autenticada do usuário desativado falha.
- **FR-037**: Reativação MUST exigir novo login (sessões antigas não são reaproveitadas).
- **FR-038**: O sistema MUST impedir que o último `tenant_admin` ativo de um tenant se desative ou seja desativado, evitando lockout.

#### Tokens

- **FR-039**: O sistema MUST suportar os seguintes tokens com TTLs e regras de renovação:
  - Access token (Bearer JWT) — 15 min, renovável via refresh token.
  - Refresh token (Cookie HttpOnly) — 7 dias padrão / 30 dias com remember me, rotativo.
  - Token de impersonation (Bearer) — 5 min, não renovável.
  - `widget_token` (UUID público) — não expira; identifica o tenant em chamadas públicas do widget.
  - Invite token (URL param) — 72 horas, não renovável.
  - Reset token (URL param) — 1 hora, não renovável.

#### Roadmap

- **FR-040**: Permissões sobre Billing/Planos (limites de uso, upgrade, downgrade) MUST ser tratadas em V2; estão fora do escopo desta spec.

### Matriz de Permissões (referência completa)

#### 4.1 Painel Admin (Spec 02)

| Ação | `saas_admin` |
|---|---|
| Listar / detalhar tenants | ✅ |
| Criar tenant (provisionar) | ✅ |
| Editar dados cadastrais do tenant | ✅ |
| Bloquear / desbloquear tenant | ✅ |
| Redefinir senha do super admin do tenant | ✅ |
| Impersonar (acessar CRM do tenant) | ✅ |
| Ver métricas de saúde de todos os tenants | ✅ |
| Retentar provisionamento com erro | ✅ |
| Criar / editar / desativar templates de agentes globais | ✅ |

#### 4.2 Departamentos (Spec 04)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Criar departamento | ✅ | ❌ | ❌ |
| Editar departamento | ✅ | ❌ | ❌ |
| Desativar departamento (soft delete) | ✅ | ❌ | ❌ |
| Listar / visualizar departamentos | ✅ | ✅ | ✅ (apenas os seus) |
| Criar atendente | ✅ | ✅ | ❌ |
| Editar atendente | ✅ | ✅ | ❌ (apenas próprio perfil) |
| Desativar atendente | ✅ | ✅ | ❌ |
| Alterar próprio status (online/away/offline) | ✅ | ✅ | ✅ |
| Ver tickets ativos de qualquer atendente | ✅ | ✅ | ❌ (apenas os próprios) |
| Criar respostas pré-formadas | ✅ | ✅ | ✅ |
| Editar / excluir respostas pré-formadas de qualquer atendente | ✅ | ✅ | ❌ (apenas as próprias) |
| Transferir ticket para outro atendente / departamento | ✅ | ✅ | ✅ |
| Assumir ticket manualmente | ✅ | ✅ | ✅ |

#### 4.3 Live Chat — Configuração do Widget (Spec 06)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Ver configuração do widget | ✅ | ✅ | ❌ |
| Editar aparência, identificação, comportamento | ✅ | ✅ | ❌ |
| Editar termos de privacidade / LGPD | ✅ | ✅ | ❌ |
| Editar domínios autorizados | ✅ | ❌ | ❌ |
| Ligar / desligar widget globalmente | ✅ | ❌ | ❌ |
| Ver código de instalação | ✅ | ✅ | ❌ |

#### 4.4 Live Chat — Conversas (Spec 06)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Ver todas as conversas do tenant | ✅ | ✅ | ❌ |
| Ver conversas do próprio departamento | ✅ | ✅ | ✅ |
| Ver conversas atribuídas a si | ✅ | ✅ | ✅ |
| Encerrar conversa manualmente | ✅ | ✅ | ✅ (apenas as atribuídas) |
| Gerenciar permissões de browser notification | ✅ | ✅ | ✅ (apenas as próprias) |

#### 4.5 Agentes de IA (Spec 05)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Ver lista de agentes | ✅ | ✅ | ❌ |
| Editar prompt / nome / modelo do Orchestrator | ✅ | ✅ | ❌ |
| Criar sub-agente | ✅ | ✅ | ❌ |
| Editar sub-agente | ✅ | ✅ | ❌ |
| Ativar / desativar sub-agente | ✅ | ✅ | ❌ |
| Usar playground (testador de agente) | ✅ | ✅ | ❌ |
| Editar configurações avançadas de IA (`context_window_messages`) | ✅ | ❌ | ❌ |
| Ver logs de atividade dos agentes | ✅ | ✅ | ❌ |

#### 4.6 Tickets (Spec 08)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Ver todos os tickets do tenant | ✅ | ✅ | ❌ |
| Ver tickets dos próprios departamentos | ✅ | ✅ | ✅ |
| Criar ticket manualmente | ✅ | ✅ | ✅ |
| Editar ticket (assunto, prioridade, tags) | ✅ | ✅ | ✅ (apenas os seus) |
| Mudar status / encerrar / cancelar | ✅ | ✅ | ✅ (apenas com acesso) |
| Transferir ticket | ✅ | ✅ | ✅ |
| Adicionar / ver anotações internas | ✅ | ✅ | ✅ (apenas dos seus) |
| Ver histórico de eventos do ticket | ✅ | ✅ | ✅ (apenas os seus) |
| Configurar / renomear colunas do pipeline | ✅ | ✅ | ❌ |

#### 4.7 Contatos (Spec 08)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Listar / buscar / ver perfil do contato | ✅ | ✅ | ✅ |
| Ver histórico de tickets e conversas | ✅ | ✅ | ✅ |
| Criar / editar contato | ✅ | ✅ | ✅ |

#### 4.8 WhatsApp — Configuração e Templates (Spec 07)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Ver status e dados do canal (exceto access_token) | ✅ | ✅ | ❌ |
| Editar configuração do canal (Phone ID, WABA ID, nome) | ✅ | ❌ | ❌ |
| Editar / visualizar Access Token | ✅ | ❌ (só sabe se está configurado) | ❌ |
| Ativar / desativar canal WhatsApp | ✅ | ❌ | ❌ |
| Ver templates | ✅ | ✅ | ❌ |
| Criar / editar template | ✅ | ✅ | ❌ |
| Submeter template para aprovação da Meta | ✅ | ✅ | ❌ |

#### 4.9 Notificações (Spec 09)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Ver próprias notificações in-app | ✅ | ✅ | ✅ |
| Marcar notificações como lidas | ✅ | ✅ | ✅ |
| Configurar preferências de push (próprias) | ✅ | ✅ | ✅ |
| Configurar notificações para clientes (tenant) | ✅ | ✅ | ❌ |

#### 4.10 Agenda e Catálogo de Serviços (Spec 10)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Ver agenda (grade + lista) | ✅ | ✅ | ✅ |
| Criar / editar agendamento | ✅ | ✅ | ✅ |
| Confirmar agendamento | ✅ | ✅ | ✅ |
| Cancelar agendamento | ✅ | ✅ | ✅ |
| Marcar no-show | ✅ | ✅ | ✅ |
| Reenviar lembrete WhatsApp | ✅ | ✅ | ✅ |
| Gerenciar profissionais | ✅ | ✅ | ❌ |
| Gerenciar catálogo de serviços | ✅ | ✅ | ❌ |
| Configurar disponibilidade / bloqueios | ✅ | ✅ | ❌ |
| Configurar política de cancelamento | ✅ | ❌ | ❌ |

#### 4.11 Auditoria (Spec 11)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Ver atividade recente (listagem no CRM) | ✅ | ❌ | ❌ |
| Criar / revogar API Keys de auditoria | ✅ | ❌ | ❌ |

#### 4.12 Autenticação (Spec 01)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Enviar convite para novo usuário | ✅ | ✅ (roles attendant/supervisor) | ❌ |
| Desativar / reativar usuário | ✅ | ❌ | ❌ |
| Ver sessões ativas próprias | ✅ | ✅ | ✅ |
| Encerrar própria sessão | ✅ | ✅ | ✅ |
| Ativar / desativar 2FA (próprio) | ✅ | ✅ | ✅ |

### Tokens e Autenticação (referência)

| Token | Tipo | TTL | Renovável | Escopo |
|---|---|---|---|---|
| Access token (JWT) | Bearer | 15 min | ✅ (via refresh token) | Acesso ao CRM do tenant |
| Refresh token | Cookie HttpOnly | 7d (padrão) / 30d (remember me) | ✅ (rotativo) | Renovação do access token |
| Token de impersonation | Bearer | 5 min | ❌ | Acesso temporário ao CRM por `saas_admin` |
| `widget_token` | UUID público | Não expira | N/A | Identificação do tenant nas requisições públicas do widget |
| Invite token | URL param | 72h | ❌ | Convite de novo usuário |
| Reset token | URL param | 1h | ❌ | Redefinição de senha |

### Key Entities

- **Role**: Identificador único do nível de acesso de um usuário. Conjunto fechado em V1: `saas_admin`, `tenant_admin`, `supervisor`, `attendant`.
- **Usuário**: Pessoa autenticada. Atributos relevantes para esta spec: `role`, `tenant_id` (nulo para `saas_admin`), `is_active`, vínculos com Departamentos (somente para roles do CRM).
- **Departamento**: Agrupamento operacional dentro de um tenant que define o escopo de visibilidade do `attendant` sobre conversas e tickets.
- **Sessão de impersonation**: Estado temporário em que um `saas_admin` opera o CRM de um tenant. Possui token próprio, banner de aviso e marcação de auditoria distinta.
- **Token**: Credencial portadora com tipo, TTL, renovabilidade e escopo definidos na tabela acima.
- **Permissão**: Par `(role, ação)` com resultado booleano. Conjunto agregado nas tabelas 4.1–4.12 forma a matriz de autorização.

## Success Criteria *(mandatory)*

### Outcomes mensuráveis

- **SC-001**: 100% dos endpoints/telas das specs 01–11 conseguem citar a célula da matriz que justifica sua autorização (rastreabilidade total entre regras e implementação).
- **SC-002**: Em revisão de qualquer PR que adicione nova ação protegida, ≤ 1 minuto é gasto para localizar a permissão correspondente nesta spec — ou a ausência dela dispara update obrigatório.
- **SC-003**: Zero incidentes de privilégio cruzado entre tenants em produção (qualquer ocorrência é tratada como bug crítico, P0).
- **SC-004**: Zero incidentes de escalada de privilégio dentro do mesmo tenant (ex.: `attendant` enxergando ticket de outro departamento) ao longo dos primeiros 90 dias após GA.
- **SC-005**: Após desativação de um usuário, ≥ 99% das requisições autenticadas dele falham em ≤ 1 segundo (medido em testes de integração e em produção).
- **SC-006**: Toda sessão de impersonation tem 100% das ações associadas a um `impersonated_by` correspondente em auditoria — auditável a partir da Spec 11.
- **SC-007**: O tempo médio para um `tenant_admin` recém-provisionado executar o primeiro login e configurar o primeiro departamento é ≤ 10 minutos (sem bloqueios de permissão).
- **SC-008**: ≤ 5% das aberturas de chamado de suporte da V1 estão relacionadas a "não consigo acessar X" causadas por dúvida sobre a role correta (medido em categorização de tickets de suporte).

## Assumptions

- **Atribuição de role**: cada usuário tem exatamente uma role no contexto em que vive. Não há multi-role. Mudanças de role são feitas via promoção/rebaixamento (admin → supervisor → attendant).
- **`saas_admin` único na V1**: apenas uma conta `saas_admin` existe em V1 (o operador SaaS — Fabio). Provisionamento manual; não há auto-criação ou multiusuário neste contexto.
- **Provisionamento cria 1 `tenant_admin`**: o provisionamento (Spec 03) entrega credenciais para um único e-mail informado. Esse usuário pode promover outros `tenant_admin`s depois.
- **Roles armazenadas no JWT**: a role é uma claim do access token e do token de impersonation. Mudanças de role exigem revogação de tokens em vigor.
- **Escopo de departamento é a única dimensão de filtro horizontal para `attendant`**: não há, em V1, regra adicional como "ticket privado" ou ACL por contato.
- **Contatos compartilhados no tenant**: todos os usuários do CRM enxergam todos os contatos, independentemente de departamento. Filtros por departamento aplicam apenas a tickets/conversas.
- **Política "deny by default"**: ações não previstas na matriz são negadas. Adições de funcionalidade exigem update prévio desta spec.
- **Resolução de tenant precede autorização**: o middleware de tenant (Spec 03) é executado antes desta camada; tenants bloqueados nem chegam à autorização de role.
- **Auditoria fica na Spec 11**: esta spec define os requisitos de logging mínimo (impersonation, desativação), mas o motor de auditoria pertence à Spec 11.
- **Billing/planos fora de escopo na V1**: nenhuma permissão depende de plano/limite — controles de billing são V2.
- **Sessões em Redis**: invalidação imediata depende da Spec 01 (auth/JWT). Esta spec assume que o mecanismo existe e o consome.
- **Idioma das interfaces**: mensagens de erro de autorização são entregues em PT-BR no CRM e PT-BR no Painel Admin (consistente com público brasileiro).

## Dependências entre Specs

| Spec | Como esta spec é consumida |
|---|---|
| 01 (Auth/JWT) | Define como roles são embarcadas no JWT e revogadas. Esta spec referencia tokens e ciclo de vida. |
| 02 (Painel Admin) | Consome FR-008/FR-009; matriz 4.1. |
| 03 (Tenant Provisioning) | Cria o `tenant_admin` inicial (FR-032) e o `tenant_id` usado pelo middleware de tenant (FR-026). |
| 04 (Departamentos) | Consome matriz 4.2 e FR-013. |
| 05 (Agentes de IA) | Consome matriz 4.5 e FR-016/FR-017. |
| 06 (Live Chat) | Consome matrizes 4.3 e 4.4 e FR-014/FR-015. |
| 07 (WhatsApp) | Consome matriz 4.8 e FR-021/FR-022. |
| 08 (Tickets/Contatos) | Consome matrizes 4.6 e 4.7 e FR-018/FR-019/FR-020. |
| 09 (Notificações) | Consome matriz 4.9 e FR-023. |
| 10 (Agenda) | Consome matriz 4.10 e FR-024. |
| 11 (Auditoria) | Consome FR-031 (impersonation) e matriz 4.11. |
