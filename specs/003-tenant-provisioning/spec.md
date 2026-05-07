# Feature Specification: Tenants (Provisionamento)

**Feature Branch**: `003-tenant-provisioning`
**Created**: 2026-05-06
**Status**: Draft

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Provisionamento de Novo Tenant (Priority: P1)

O operador SaaS acessa o painel admin, preenche o formulário de cadastro de um novo tenant (empresa cliente), e o sistema executa automaticamente o provisionamento completo: cria todos os recursos isolados (schema Postgres, bucket de arquivos, banco de dados de documentos, prefixo de cache), cria o usuário administrador do tenant e envia um e-mail de boas-vindas com as credenciais de acesso ao responsável técnico informado.

**Why this priority**: Sem provisionamento funcional não é possível ter nenhum tenant ativo no sistema. É o pré-requisito absoluto para qualquer outra funcionalidade.

**Independent Test**: Pode ser testado preenchendo o formulário de novo tenant com dados válidos, aguardando a conclusão do provisionamento, verificando que o e-mail de boas-vindas foi recebido e que o login no subdomínio do tenant funciona com as credenciais enviadas.

**Acceptance Scenarios**:

1. **Given** o operador autenticado no painel admin, **When** ele preenche o formulário com razão social, CNPJ válido, slug único e dados dos contatos financeiro e técnico e confirma, **Then** o sistema cria o registro do tenant com status `provisioning` e inicia o processo de provisionamento em segundo plano.
2. **Given** o provisionamento em andamento, **When** todos os recursos são criados com sucesso, **Then** o sistema cria o usuário Super Admin com e-mail do responsável técnico, gera senha automática forte (12 caracteres) e atualiza o status do tenant para `active`.
3. **Given** o provisionamento concluído com sucesso, **When** o Super Admin é criado, **Then** o sistema envia e-mail de boas-vindas ao responsável técnico com a URL de acesso, login e senha gerada.
4. **Given** um formulário de novo tenant, **When** o operador tenta usar um slug já existente, **Then** o sistema rejeita o cadastro informando que o slug já está em uso.
5. **Given** um formulário de novo tenant, **When** o operador informa um CNPJ já cadastrado, **Then** o sistema rejeita informando que o CNPJ já está associado a outro tenant.
6. **Given** um formulário de novo tenant, **When** o operador informa um slug com caracteres inválidos (maiúsculas, acentos, espaços), **Then** o sistema rejeita e informa que o slug aceita apenas letras minúsculas, números e hífen.
7. **Given** uma falha durante o provisionamento, **When** o erro ocorre, **Then** o status do tenant fica como `error`, nenhum e-mail é enviado e o operador vê o log de erro no admin.
8. **Given** um tenant com status `error`, **When** o operador clica em "Retentar provisionamento", **Then** o sistema reinicia o processo de forma idempotente (recursos já criados não são recriados).

---

### User Story 2 - Bloqueio e Desbloqueio de Tenant (Priority: P2)

O operador pode suspender temporariamente o acesso de um tenant ao CRM com um único clique. Ao bloquear, todas as sessões ativas dos usuários do tenant são encerradas imediatamente e o CRM passa a exibir uma tela de acesso suspenso. O desbloqueio restaura o acesso normalmente.

**Why this priority**: Funcionalidade operacional crítica para situações de inadimplência, violação de termos ou manutenção de emergência. Necessária antes do primeiro cliente ativo.

**Independent Test**: Pode ser testado com um usuário autenticado no CRM do tenant, o operador bloqueia o tenant no admin e verifica que o usuário perde acesso imediatamente e vê a tela de "Acesso suspenso". Em seguida desbloquear e verificar que o acesso é restaurado.

**Acceptance Scenarios**:

1. **Given** um tenant com status `active`, **When** o operador clica em "Bloquear acesso" no admin, **Then** o status muda para `blocked`, `blocked_at` é registrado e todas as sessões ativas dos usuários do tenant são invalidadas imediatamente.
2. **Given** um tenant com status `blocked`, **When** qualquer usuário do tenant tenta acessar uma rota do CRM, **Then** o sistema retorna acesso negado (403) em todas as rotas e o CRM exibe a página "Acesso suspenso. Entre em contato com o suporte."
3. **Given** um tenant com status `blocked`, **When** o operador clica em "Desbloquear" no admin, **Then** o status volta para `active` e os usuários do tenant podem autenticar normalmente.

---

### User Story 3 - Impersonation (Acesso do Operador ao CRM do Tenant) (Priority: P2)

O operador pode acessar temporariamente o ambiente CRM de qualquer tenant para fins de suporte e diagnóstico, sem necessitar da senha de nenhum usuário do tenant. O acesso é marcado visivelmente no CRM e expira automaticamente em 15 minutos sem possibilidade de renovação.

**Why this priority**: Essencial para suporte ao cliente sem comprometer a segurança das credenciais do tenant.

**Independent Test**: Pode ser testado com o operador clicando em "Acessar ambiente" de um tenant ativo, verificar que o CRM abre com a barra de aviso "Você está acessando como operador SaaS" e que o token expira em 15 minutos sem opção de renovação.

**Acceptance Scenarios**:

1. **Given** o operador no painel admin, **When** ele clica em "Acessar ambiente" de um tenant ativo, **Then** o sistema gera um token de curta duração (15 minutos, não renovável) e redireciona o operador para o CRM do tenant já autenticado.
2. **Given** o operador acessando o CRM via impersonation, **When** qualquer página é carregada, **Then** uma barra de aviso visível no topo indica "Você está acessando como operador SaaS".
3. **Given** um token de impersonation ativo, **When** os 15 minutos expiram, **Then** qualquer requisição subsequente é rejeitada e o operador é redirecionado para o login.
4. **Given** um token de impersonation, **When** o sistema tenta renová-lo, **Then** o sistema rejeita — tokens de impersonation não são renováveis.

---

### User Story 4 - Redefinição de Senha do Super Admin (Priority: P2)

O operador pode redefinir a senha do Super Admin de qualquer tenant diretamente pelo admin, sem precisar da senha atual. O sistema gera uma nova senha forte automaticamente, encerra as sessões ativas do Super Admin e envia a nova senha por e-mail ao responsável técnico.

**Why this priority**: Funcionalidade de suporte necessária para situações em que o Super Admin perde acesso às credenciais.

**Independent Test**: Pode ser testado com o Super Admin autenticado em uma sessão ativa, o operador aciona "Redefinir senha" no admin, e verificar que a sessão ativa é encerrada e que o login com a nova senha (recebida por e-mail) funciona.

**Acceptance Scenarios**:

1. **Given** o operador no painel admin, **When** ele clica em "Redefinir senha" de um tenant, **Then** o sistema gera uma nova senha forte automaticamente, invalida todas as sessões ativas do Super Admin e envia a nova senha por e-mail ao responsável técnico.
2. **Given** o Super Admin com sessão ativa, **When** o operador redefine sua senha, **Then** a sessão é encerrada imediatamente e qualquer requisição com o token anterior é rejeitada.

---

### User Story 5 - Gestão de Templates de Agentes de IA (Priority: P3)

O operador gerencia um catálogo global de templates de agentes de IA que são automaticamente copiados para cada novo tenant no momento do provisionamento. O tenant recebe os agentes já prontos para personalização, mas alterações posteriores nos templates globais não afetam tenants já provisionados.

**Why this priority**: Melhora a experiência de onboarding dos tenants mas não bloqueia o funcionamento do produto.

**Independent Test**: Pode ser testado criando um novo template no admin, provisionando um tenant, verificar que o agente foi copiado para o tenant, editar o template global e confirmar que o agente do tenant não foi alterado.

**Acceptance Scenarios**:

1. **Given** templates ativos no admin, **When** um novo tenant é provisionado, **Then** todos os templates ativos são copiados como agentes reais no schema do tenant — o tenant pode editá-los livremente.
2. **Given** um template global editado após o provisionamento de um tenant, **When** o tenant acessa seus agentes, **Then** os agentes do tenant não são alterados — a cópia é independente do template original.
3. **Given** o operador no admin, **When** ele desativa um template, **Then** o template não é aplicado em novos provisionamentos mas os tenants que já o receberam não são afetados.
4. **Given** um template já utilizado em ao menos um provisionamento, **When** o operador tenta deletá-lo, **Then** o sistema rejeita a exclusão física e mantém o template como inativo (soft delete).

---

### User Story 6 - Dashboard de Saúde dos Tenants (Priority: P3)

O operador visualiza em um único painel o estado de saúde de todos os tenants: conectividade de cada recurso, métricas de uso (chats, tickets, usuários) e indicadores de configuração OpenAI. As métricas são atualizadas a cada 60 segundos via polling, sempre a partir de dados em cache — nunca consultando os bancos em tempo real.

**Why this priority**: Importante para operação contínua mas não bloqueia o lançamento — pode ser entregue após os fluxos de provisionamento estarem funcionais.

**Independent Test**: Pode ser testado verificando se o painel lista todos os tenants com indicadores de status e se as métricas se atualizam automaticamente a cada 60 segundos sem reload manual.

**Acceptance Scenarios**:

1. **Given** o operador no painel admin, **When** ele acessa o Dashboard de Saúde, **Then** o sistema exibe lista de todos os tenants com status, indicadores de conectividade de cada recurso, métricas de uso e ações disponíveis por linha.
2. **Given** o Dashboard exibindo tenants, **When** os dados são renderizados, **Then** todos os valores são lidos do cache — nenhuma consulta direta ao banco é feita durante a exibição.
3. **Given** o operador visualizando o Dashboard, **When** 60 segundos se passam, **Then** os dados são atualizados automaticamente via polling sem necessidade de reload manual da página.
4. **Given** o operador clicando em um tenant específico, **When** a visão individual abre, **Then** o sistema exibe dados cadastrais completos, contatos, indicador de configuração OpenAI (sem expor a chave), métricas detalhadas por recurso e log dos últimos eventos.

---

### Edge Cases

- O que acontece se o slug tiver menos de 3 ou mais de 50 caracteres? → O sistema rejeita com mensagem de validação antes de salvar.
- O que acontece se o provisionamento falhar parcialmente (ex: schema criado mas bucket não)? → O status fica `error`, o log registra o ponto de falha e o retry é idempotente — recursos já criados não são recriados.
- O que acontece se o e-mail do responsável técnico já estiver em uso por outro usuário? → O sistema rejeita o cadastro informando conflito de e-mail antes de iniciar o provisionamento.
- O que acontece com a API Key da OpenAI ao consultar detalhes do tenant? → A API retorna apenas um indicador booleano (`has_openai_key: true/false`) — a chave nunca é retornada em texto plano.
- O que acontece com o slug após a criação? → O slug é imutável. A interface não expõe opção de edição após o cadastro.
- O que acontece se as métricas do dashboard não estiverem no cache? → O sistema exibe os dados como indisponíveis (`--`) até o próximo ciclo de coleta do background job.

## Requirements *(mandatory)*

### Functional Requirements

**Provisionamento — US1**

- **FR-001**: O sistema DEVE validar que o slug contém apenas letras minúsculas, números e hífen (`[a-z0-9-]`), com mínimo de 3 e máximo de 50 caracteres.
- **FR-002**: O sistema DEVE garantir unicidade de slug e CNPJ antes de iniciar o provisionamento; tentativas duplicadas DEVEM ser rejeitadas com mensagem específica.
- **FR-003**: O sistema DEVE exigir exatamente dois contatos por tenant no cadastro: financeiro e responsável técnico, ambos com nome, e-mail e telefone.
- **FR-004**: O sistema DEVE criar o tenant com status `provisioning` imediatamente ao receber o cadastro e executar o provisionamento em segundo plano de forma assíncrona.
- **FR-005**: O provisionamento DEVE criar todos os recursos isolados nomeados a partir do slug: schema Postgres (com migrations aplicadas), bucket de armazenamento, banco de documentos e prefixo de cache reservado.
- **FR-006**: Após provisionamento bem-sucedido, o sistema DEVE criar o usuário Super Admin com role `tenant_admin`, e-mail do responsável técnico e senha gerada automaticamente com no mínimo 12 caracteres (letras maiúsculas, minúsculas, números e símbolos).
- **FR-007**: O e-mail de boas-vindas DEVE ser enviado SOMENTE após provisionamento completo e bem-sucedido — falhas no provisionamento não disparam e-mail.
- **FR-008**: Em caso de falha no provisionamento, o status DEVE ser atualizado para `error` e o log de erro DEVE ser acessível ao operador no painel admin.
- **FR-009**: O operador DEVE poder retentar o provisionamento de um tenant com status `error`. O retry DEVE ser idempotente — recursos já criados não são recriados.
- **FR-010**: O slug DEVE ser imutável após a criação — a interface não deve expor opção de edição do slug.

**Bloqueio — US2**

- **FR-011**: O operador DEVE poder bloquear e desbloquear qualquer tenant com status `active` ou `blocked`.
- **FR-012**: Ao bloquear um tenant, o sistema DEVE invalidar imediatamente todas as sessões ativas de todos os usuários daquele tenant.
- **FR-013**: Tenants com status `blocked` DEVEM receber resposta de acesso negado em todas as rotas do CRM — nenhuma rota é acessível durante o bloqueio.
- **FR-014**: O campo `blocked_at` DEVE ser preenchido no momento do bloqueio e limpo no desbloqueio.

**Impersonation — US3**

- **FR-015**: O sistema DEVE gerar um token de acesso temporário de 15 minutos para impersonation, com identificação do operador e indicador de impersonation.
- **FR-016**: Tokens de impersonation NÃO DEVEM ser renováveis — expiração definitiva em 15 minutos.
- **FR-017**: O CRM do tenant DEVE exibir barra de aviso visível enquanto o acesso via impersonation estiver ativo.

**Redefinição de Senha — US4**

- **FR-018**: O sistema DEVE gerar nova senha forte automaticamente ao acionar redefinição de senha do Super Admin.
- **FR-019**: A nova senha DEVE ser enviada por e-mail ao responsável técnico cadastrado.
- **FR-020**: Todas as sessões ativas do Super Admin DEVEM ser invalidadas imediatamente ao redefinir a senha.

**Configuração OpenAI**

- **FR-021**: Os campos de configuração OpenAI (API Key, Organization, Project) são opcionais e podem ser configurados após o cadastro inicial.
- **FR-022**: A API Key da OpenAI DEVE ser criptografada antes de salvar — nunca armazenada em texto plano.
- **FR-023**: A API Key da OpenAI NUNCA DEVE ser retornada em texto plano em nenhuma resposta da API — apenas um indicador booleano de presença.

**Templates de Agentes — US5**

- **FR-024**: Os templates ativos no momento do provisionamento DEVEM ser copiados para o schema do tenant como agentes independentes.
- **FR-025**: Alterações em templates globais NÃO DEVEM afetar agentes de tenants já provisionados.
- **FR-026**: Templates utilizados em ao menos um provisionamento NÃO PODEM ser excluídos fisicamente — apenas desativados (soft delete).

**Dashboard — US6**

- **FR-027**: As métricas do dashboard DEVEM ser coletadas por processo em segundo plano a cada 5 minutos e armazenadas em cache com TTL de 5 minutos.
- **FR-028**: A exibição do dashboard DEVE sempre ler do cache — nenhuma consulta direta aos bancos durante a renderização.
- **FR-029**: O dashboard DEVE se atualizar automaticamente via polling a cada 60 segundos.

### Key Entities

- **Tenant**: Empresa cliente (pessoa jurídica) provisionada no sistema. Possui identidade única (slug, CNPJ), status de ciclo de vida (`provisioning`, `active`, `blocked`, `error`), configuração opcional de IA e vínculo com todos os recursos isolados criados no provisionamento.
- **Contato do Tenant**: Pessoa de contato associada ao tenant. Exatamente dois por tenant: financeiro e responsável técnico. O responsável técnico recebe credenciais e comunicações do sistema.
- **Template de Agente**: Configuração global de agente de IA mantida pelo operador. Copiada (não referenciada) para cada novo tenant no provisionamento. Possui estado ativo/inativo e tipo (`orchestrator` ou `sub_agent`).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: O provisionamento completo de um novo tenant (do envio do formulário ao recebimento do e-mail de boas-vindas) ocorre em menos de 3 minutos em condições normais de infraestrutura.
- **SC-002**: O operador consegue bloquear um tenant e ter todas as sessões ativas invalidadas em menos de 5 segundos após a confirmação.
- **SC-003**: 100% das tentativas de acesso a rotas do CRM de um tenant bloqueado resultam em resposta de acesso negado — nenhuma rota é acessível durante o bloqueio.
- **SC-004**: A API Key da OpenAI nunca aparece em texto plano em nenhuma resposta de API, log ou interface — 0 exposições detectadas em revisão de segurança.
- **SC-005**: O operador consegue iniciar impersonation e acessar o CRM do tenant em menos de 10 segundos após clicar em "Acessar ambiente".
- **SC-006**: Tokens de impersonation expiram automaticamente em 15 minutos sem nenhuma ação adicional do operador — 100% de expiração garantida.
- **SC-007**: O dashboard de saúde exibe dados de todos os tenants sem executar consultas diretas aos bancos durante a renderização — 0 queries diretas em tempo de exibição.
- **SC-008**: Novos tenants recebem 100% dos templates de agentes ativos copiados corretamente no provisionamento bem-sucedido.

## Assumptions

- O serviço de e-mail (SendGrid) está disponível e configurado conforme definido na Spec 002 (Autenticação) — esta spec reutiliza a mesma integração.
- A autenticação do operador no painel admin usa role `saas_admin` conforme definido na Spec 002 — esta spec não redefine autenticação.
- O impersonation descrito aqui opera com duração de **15 minutos**. A Spec 002 descreveu 5 minutos para o mesmo recurso — esta spec prevalece para o módulo de Tenants, conforme o documento de requisitos original.
- A criptografia da API Key da OpenAI segue o mesmo padrão de AES-256 já definido para outros secrets sensíveis na Spec 002.
- O background job de coleta de métricas usa Hangfire conforme o stack definido na constituição do projeto.
- Tenants são sempre pessoas jurídicas — não há suporte a pessoa física em V1.
- A invalidação de sessões no bloqueio opera sobre as sessões Redis do tenant, dependendo do modelo de sessões implementado na Spec 002.
- Templates de agentes criados sem prompt customizado têm campo de prompt em branco — o tenant é responsável por configurar o prompt antes de ativar o agente.
