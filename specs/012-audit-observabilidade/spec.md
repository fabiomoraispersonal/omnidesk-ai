# Feature Specification: Auditoria e Observabilidade

**Feature Branch**: `012-audit-observabilidade`
**Created**: 2026-05-13
**Status**: Aprovado
**Input**: Spec 12 — Auditoria e Observabilidade v1.0

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Registro Automático de Eventos Críticos (Priority: P1)

O sistema registra automaticamente todos os eventos críticos (autenticação, tickets, agendamentos, usuários, configurações de tenant) sem intervenção manual, garantindo rastreabilidade completa de ações na plataforma.

**Why this priority**: É a fundação de toda a observabilidade — sem registro confiável dos eventos, as demais funcionalidades não têm dados para operar.

**Independent Test**: Pode ser testado realizando uma ação (ex: login, mudança de status de ticket) e verificando que um documento com a estrutura correta foi criado na coleção de logs.

**Acceptance Scenarios**:

1. **Given** um usuário realiza login com credenciais válidas, **When** a autenticação é concluída, **Then** um evento `auth.login_success` é registrado com `tenant_slug`, `tenant_id`, `actor.user_id`, `actor.role` e `timestamp` preenchidos.
2. **Given** alguém tenta login com credenciais inválidas, **When** a autenticação falha, **Then** um evento `auth.login_failed` é registrado mesmo que o e-mail não exista no sistema, com o e-mail tentado no campo `metadata`.
3. **Given** um `tenant_admin` muda o status de um ticket de `in_progress` para `resolved`, **When** a mudança é salva, **Then** um evento `ticket.status_changed` é registrado com `metadata.from = "in_progress"` e `metadata.to = "resolved"`.
4. **Given** um `saas_admin` inicia impersonation de um tenant, **When** qualquer ação é executada durante a impersonation, **Then** todos os logs do período contêm `actor.impersonated_by = "saas_admin"` preenchido.
5. **Given** um agendamento é cancelado, **When** o cancelamento é registrado, **Then** um evento `appointment.cancelled` é criado com o campo `cancelled_by` preenchido no `metadata`.

---

### User Story 2 — Consulta de Atividade Recente no CRM (Priority: P2)

O `tenant_admin` acessa uma lista paginada das ações recentes no painel CRM, filtrada por tipo de evento, usuário e período, sem necessidade de ferramentas externas para o acompanhamento básico.

**Why this priority**: Permite que o gestor do tenant monitore atividades suspeitas ou relevantes diretamente no produto, sem depender de configuração de ferramentas externas.

**Independent Test**: Pode ser testado acessando CRM → Configurações → Atividade Recente como `tenant_admin` e verificando listagem com filtros funcionais.

**Acceptance Scenarios**:

1. **Given** um `tenant_admin` autenticado acessa "Atividade Recente", **When** a página carrega, **Then** são exibidos até 20 registros paginados, ordenados por data decrescente, cada um com ícone, descrição legível, ator e timestamp relativo.
2. **Given** o `tenant_admin` aplica filtro por tipo de evento "ticket.status_changed", **When** o filtro é ativado, **Then** apenas eventos desse tipo são exibidos.
3. **Given** o `tenant_admin` aplica filtro por período (ex: últimos 7 dias), **When** o filtro é ativado, **Then** apenas eventos dentro do intervalo são exibidos.
4. **Given** um usuário com role `tenant_attendant` tenta acessar "Atividade Recente", **When** tenta navegar para a página, **Then** recebe erro de acesso negado (403).
5. **Given** o `tenant_admin` está na página de atividade recente, **When** não há eventos no período filtrado, **Then** uma mensagem de estado vazio é exibida.

---

### User Story 3 — Consulta via API para Ferramentas Externas (Priority: P2)

Ferramentas externas como Metabase acessam os logs de auditoria via API REST autenticada por API Key, com filtros por evento, ator e período, possibilitando análises avançadas fora do produto.

**Why this priority**: Viabiliza o uso do Metabase e outras ferramentas de BI para análises que o CRM não oferece nativamente.

**Independent Test**: Pode ser testado com uma requisição HTTP autenticada com API Key válida ao endpoint de logs, verificando retorno paginado com filtros.

**Acceptance Scenarios**:

1. **Given** uma ferramenta externa possui uma API Key válida com escopo `audit_logs:read`, **When** faz `GET /api/audit-logs` com o header `X-Api-Key`, **Then** recebe resposta paginada com os logs do tenant correspondente.
2. **Given** uma requisição com API Key inválida ou revogada, **When** tenta acessar os logs, **Then** recebe erro 401.
3. **Given** filtros `?event=ticket.status_changed&from=2026-06-01&to=2026-06-30`, **When** a API é chamada, **Then** apenas logs do evento e período especificados são retornados.
4. **Given** a API Key é do tenant A, **When** a requisição é feita, **Then** apenas logs do tenant A são retornados — nunca de outros tenants.
5. **Given** `per_page=100` é especificado, **When** há 250 registros no período, **Then** a resposta contém 100 registros e `meta.total = 250`.

---

### User Story 4 — Gestão de API Keys pelo Tenant Admin (Priority: P3)

O `tenant_admin` cria, lista e revoga API Keys para integrações externas no painel CRM, com a chave bruta exibida apenas no momento da criação.

**Why this priority**: Necessário para que o US3 seja utilizável, mas depende do US1 e US3 estarem implementados primeiro.

**Independent Test**: Pode ser testado criando uma API Key no CRM, copiando a chave bruta, e confirmando que ela nunca mais é exibida em acessos subsequentes.

**Acceptance Scenarios**:

1. **Given** um `tenant_admin` acessa CRM → Configurações → Integrações, **When** clica em "Criar API Key" e fornece um nome, **Then** a chave bruta é exibida uma única vez em um modal com instrução clara para copiá-la.
2. **Given** o `tenant_admin` retorna à listagem de API Keys, **When** visualiza a chave criada, **Then** apenas nome, data de criação, último uso e status são exibidos — a chave bruta nunca aparece novamente.
3. **Given** o tenant já possui 5 API Keys ativas, **When** tenta criar uma sexta, **Then** recebe erro informando que o limite foi atingido.
4. **Given** o `tenant_admin` clica em "Revogar" em uma API Key, **When** confirma a ação, **Then** a chave é marcada como revogada e requisições subsequentes com ela recebem 401.
5. **Given** um usuário com role `tenant_attendant` acessa a área de integrações, **When** tenta criar ou revogar uma API Key, **Then** recebe erro de acesso negado (403).

---

### Edge Cases

- O que acontece se o serviço de registro de logs falhar durante uma operação crítica? (A operação principal não deve falhar; o log é best-effort exceto para eventos de segurança.)
- Como o sistema lida com tentativas de login com e-mail que não existe no banco? (Registra o evento com o e-mail tentado no `metadata`, sem referência a `user_id`.)
- O que ocorre se o job de retenção falhar em um mês? (Será reprocessado na próxima execução mensal; logs com mais de 12 meses permanecem até limpeza bem-sucedida.)
- Uma API Key pode ser reativada após revogação? (Não — revogação é permanente. O tenant deve criar uma nova chave.)
- O que acontece quando a impersonation termina e um log é gerado no intervalo de expiração? (O token de impersonation já expirou registra `auth.impersonation_ended`; logs pós-expiração não carregam `impersonated_by`.)

---

## Requirements *(mandatory)*

### Functional Requirements

#### Registro de Eventos

- **FR-001**: O sistema DEVE registrar automaticamente todos os 29 eventos definidos na Seção 3 (autenticação, usuários, tickets, agendamentos, configurações de tenant) com a estrutura de documento completa.
- **FR-002**: Todo documento de log DEVE conter obrigatoriamente: `tenant_slug`, `tenant_id`, `event`, `actor.user_id`, `actor.role` e `timestamp`.
- **FR-003**: Quando a ação for executada por um `saas_admin` em impersonation, o campo `actor.impersonated_by` DEVE ser preenchido com o identificador do admin.
- **FR-004**: Tentativas de login com e-mail inexistente DEVEM ser registradas em `auth.login_failed` com o e-mail tentado no campo `metadata`.
- **FR-005**: Logs DEVEM ser imutáveis — nenhuma operação de edição ou exclusão via API deve ser permitida.
- **FR-006**: Um job mensal DEVE remover automaticamente documentos com `timestamp` anterior a 12 meses da data de execução.

#### Interface de Atividade Recente (CRM)

- **FR-007**: A seção "Atividade Recente" em CRM → Configurações DEVE ser acessível exclusivamente a usuários com role `tenant_admin`.
- **FR-008**: A listagem DEVE exibir eventos paginados (20 por página), ordenados por `timestamp` decrescente, com ícone do evento, descrição legível, nome do ator e timestamp relativo.
- **FR-009**: A listagem DEVE oferecer filtros por tipo de evento (dropdown com todos os 29 tipos), por usuário (select) e por período (date range).
- **FR-010**: A listagem NÃO deve oferecer exportação, gráficos ou análises — apenas a lista filtrada.

#### API REST de Consulta

- **FR-011**: O endpoint `GET /api/audit-logs` DEVE retornar logs paginados com suporte a filtros por `event`, `actor_id`, `from` e `to`.
- **FR-012**: O endpoint DEVE aceitar autenticação via header `X-Api-Key` com uma API Key válida e não revogada do tenant.
- **FR-013**: A resposta DEVE seguir o envelope padrão: `{ "data": [...], "meta": { "total", "page", "per_page" } }`.
- **FR-014**: O endpoint DEVE garantir isolamento de tenant — nunca retornar logs de tenants diferentes da API Key utilizada.

#### Gestão de API Keys

- **FR-015**: Somente `tenant_admin` DEVE poder criar, listar e revogar API Keys.
- **FR-016**: No momento da criação, a chave bruta DEVE ser exibida uma única vez; acessos posteriores DEVEM exibir apenas metadados (nome, datas, status).
- **FR-017**: O sistema DEVE impedir a criação de mais de 5 API Keys ativas por tenant simultaneamente.
- **FR-018**: A revogação de uma API Key DEVE ser permanente — chaves revogadas não podem ser reativadas.
- **FR-019**: O campo `last_used_at` da API Key DEVE ser atualizado a cada uso bem-sucedido.
- **FR-020**: API Keys DEVEM suportar o escopo `audit_logs:read` (V1 único escopo disponível).

### Key Entities

- **AuditLog** (documento MongoDB por tenant): Representa um evento auditado. Contém `tenant_slug`, `tenant_id`, `event` (string com namespacing por ponto), `actor` (objeto com `user_id`, `name`, `role`, `impersonated_by`), `target` (objeto opcional com `entity_type`, `entity_id`, `label`), `metadata` (objeto livre com dados contextuais do evento), `ip_address`, `user_agent` e `timestamp`. Imutável após criação.

- **ApiKey** (registro relacional por tenant): Representa uma credencial de acesso à API para integrações externas. Contém `id`, `tenant_id`, `name`, `key_hash` (hash SHA-256 — chave bruta nunca armazenada), `scopes` (lista de permissões), `last_used_at`, `expires_at` (opcional), `revoked` e `created_at`.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% dos 29 eventos definidos na Spec geram registro com estrutura correta — verificável por suite de testes de integração.
- **SC-002**: Toda ação executada durante impersonation contém `actor.impersonated_by` preenchido — sem exceções.
- **SC-003**: A seção "Atividade Recente" carrega a listagem inicial em menos de 2 segundos para volumes de até 10.000 eventos no tenant.
- **SC-004**: A API de consulta retorna resultados filtrados em menos de 1 segundo para queries no período de 30 dias.
- **SC-005**: Zero logs são acessíveis via API sem autenticação válida por API Key — taxa de bypass de autenticação = 0%.
- **SC-006**: Logs com mais de 12 meses são removidos no ciclo mensal seguinte ao vencimento — retenção verificável por data de criação do documento mais antigo.
- **SC-007**: A chave bruta de uma API Key nunca é recuperável após o modal de criação ser fechado — confirmável por inspeção do banco e dos endpoints.

---

## Assumptions

- A coleção de logs no MongoDB é particionada por tenant (`{tenant_slug}_audit_logs` ou campo `tenant_slug` como discriminador) para garantir isolamento e performance de queries.
- O sistema de impersonation do `saas_admin` já está implementado no módulo de autenticação (Spec 02) e expõe o contexto necessário para popular `actor.impersonated_by`.
- O job de retenção mensal utiliza o Hangfire já na stack — não requer infraestrutura adicional de agendamento.
- A interface "Atividade Recente" não requer internacionalização de descrições dos eventos em V1 — exibição em português.
- Campos `ip_address` e `user_agent` são coletados via middleware HTTP e disponibilizados no contexto de cada request; eventos de background jobs (ex: `appointment.no_show`) não terão esses campos preenchidos.
- A API Key não possui expiração por padrão (`expires_at = null`) — expiração automática é feature futura.
- O Metabase e outras ferramentas externas são responsabilidade do operador do tenant — o produto fornece apenas o endpoint autenticado.
- Formato de chave bruta gerada: string aleatória de 32 bytes em base64url (43 caracteres), com prefixo `omni_` para identificação visual. Ex: `omni_abc123...`.
