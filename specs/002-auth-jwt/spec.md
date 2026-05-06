# Feature Specification: Autenticação

**Feature Branch**: `002-auth-jwt`
**Created**: 2026-05-05
**Status**: Draft

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Acesso Seguro ao Sistema (Priority: P1)

Qualquer colaborador do tenant (tenant_admin, supervisor ou attendant) ou o administrador SaaS (saas_admin) deve conseguir entrar no sistema com e-mail e senha, manter a sessão ativa sem precisar fazer login novamente a cada poucos minutos, e sair quando quiser. O sistema protege o formulário de login contra bots e ataques automatizados, e bloqueia temporariamente acessos após múltiplas tentativas erradas.

**Why this priority**: Sem login funcional, nenhum outro módulo é acessível. É o pré-requisito absoluto para toda a aplicação.

**Independent Test**: Pode ser testado criando um usuário diretamente no banco, fazendo login pelo formulário, verificando acesso ao painel, navegando por tempo suficiente para expirar o token de acesso, e confirmando que a sessão é renovada automaticamente. Então fazer logout e verificar que o acesso é encerrado.

**Acceptance Scenarios**:

1. **Given** um usuário ativo com e-mail verificado, **When** ele submete e-mail e senha corretos, **Then** o sistema o autentica e concede acesso ao painel correspondente ao seu perfil (Admin SaaS ou CRM do tenant).
2. **Given** um usuário autenticado navegando ativamente, **When** seu token de acesso expira (15 minutos), **Then** o sistema renova a autenticação automaticamente em segundo plano sem exigir novo login.
3. **Given** um usuário autenticado, **When** ele solicita logout, **Then** o sistema encerra a sessão imediatamente e impede o uso dos tokens anteriores para qualquer acesso subsequente.
4. **Given** qualquer pessoa acessando o formulário de login, **When** o formulário é carregado, **Then** um mecanismo de verificação anti-bot está presente e é validado no servidor antes de processar as credenciais — tentativas sem verificação válida são rejeitadas com erro 403.
5. **Given** um usuário tentando login, **When** ele erra as credenciais 5 vezes em 10 minutos, **Then** o sistema bloqueia novas tentativas por 15 minutos para aquele IP e e-mail.
6. **Given** um usuário inativo, **When** ele tenta fazer login com credenciais corretas, **Then** o sistema rejeita o acesso com mensagem de conta inativa.
7. **Given** um usuário com e-mail não verificado (convite não aceito), **When** ele tenta fazer login, **Then** o sistema rejeita informando que o convite precisa ser aceito primeiro.
8. **Given** a opção "lembrar-me" marcada no login, **When** o usuário autentica com sucesso, **Then** a sessão dura 30 dias; sem essa opção, dura 7 dias.

---

### User Story 2 - Recuperação de Senha (Priority: P1)

Usuário que esqueceu sua senha pode solicitar um link de redefinição por e-mail. O link expira em 1 hora. Ao redefinir a senha, todas as sessões ativas são encerradas por segurança. O formulário de recuperação também possui proteção anti-bot.

**Why this priority**: Sem recuperação de senha, usuários que esquecem as credenciais ficam permanentemente bloqueados. É parte essencial do fluxo de acesso.

**Independent Test**: Pode ser testado solicitando recuperação de senha para um e-mail cadastrado, acessando o link recebido, definindo nova senha e verificando que login funciona com a nova senha e que sessões anteriores são invalidadas.

**Acceptance Scenarios**:

1. **Given** um e-mail registrado no sistema, **When** o usuário solicita recuperação de senha, **Then** o sistema envia um e-mail com link de redefinição válido por 1 hora — o mecanismo anti-bot é validado antes de processar.
2. **Given** um link de redefinição válido, **When** o usuário acessa o link e define nova senha, **Then** o sistema atualiza a senha e invalida todas as sessões ativas do usuário.
3. **Given** um link de redefinição expirado (> 1 hora), **When** o usuário tenta utilizá-lo, **Then** o sistema rejeita e orienta a solicitar novo link.
4. **Given** um link de redefinição já utilizado, **When** alguém tenta utilizá-lo novamente, **Then** o sistema rejeita sem processar nova alteração.
5. **Given** um e-mail não cadastrado no sistema, **When** o usuário solicita recuperação de senha, **Then** o sistema retorna a mesma resposta de sucesso (sem revelar se o e-mail existe ou não).

---

### User Story 3 - Convite de Colaboradores (Priority: P2)

Administradores e supervisores de tenant podem convidar novos colaboradores por e-mail. O convidado recebe um link, clica, define nome e senha, e já fica autenticado com o perfil correto. O convite expira em 72 horas. É o único mecanismo de criação de usuários para tenants — não há auto-cadastro.

**Why this priority**: Sem convites, não é possível adicionar novos membros à equipe. É a segunda funcionalidade mais crítica após o login em si.

**Independent Test**: Pode ser testado enviando um convite para um e-mail novo, acessando o link, completando o cadastro e verificando que o usuário está autenticado com o perfil correto.

**Acceptance Scenarios**:

1. **Given** um tenant_admin ou supervisor autenticado, **When** ele envia convite para um e-mail novo com perfil definido, **Then** o convidado recebe e-mail com link de acesso válido por 72 horas.
2. **Given** um link de convite válido, **When** o convidado acessa o link, define nome e senha, **Then** o sistema cria o usuário com e-mail verificado e realiza login automático no tenant correto.
3. **Given** um link de convite expirado (> 72 horas), **When** o convidado tenta acessar, **Then** o sistema informa que o convite expirou e orienta a solicitar novo convite.
4. **Given** um link de convite já aceito, **When** alguém tenta acessá-lo novamente, **Then** o sistema rejeita sem criar novo usuário.
5. **Given** um e-mail já cadastrado no sistema, **When** um convite é enviado para esse e-mail, **Then** o sistema rejeita o envio informando que o usuário já existe.
6. **Given** dois convites enviados para o mesmo e-mail, **When** o segundo é enviado antes do primeiro ser aceito, **Then** o primeiro convite é invalidado e apenas o segundo fica ativo.

---

### User Story 4 - Verificação em Dois Fatores (Priority: P2)

Qualquer usuário pode ativar opcionalmente a verificação em dois fatores (2FA) em seu perfil. Após ativação, o login passa a exigir um código gerado por aplicativo autenticador (Google Authenticator, Authy, etc.). O usuário recebe códigos de recuperação para situações de emergência (perda do dispositivo).

**Why this priority**: Recurso de segurança importante para usuários e tenants que lidam com dados sensíveis de pacientes. Opcional na V1, mas necessário para oferecer proteção adicional.

**Independent Test**: Pode ser testado ativando o 2FA no perfil de um usuário de teste, fazendo logout, fazendo login com e-mail e senha e verificando que o sistema solicita o código do autenticador antes de conceder acesso.

**Acceptance Scenarios**:

1. **Given** um usuário autenticado sem 2FA, **When** ele acessa Perfil → Segurança e inicia ativação do 2FA, **Then** o sistema apresenta um QR Code para configuração no aplicativo autenticador.
2. **Given** o QR Code apresentado, **When** o usuário escaneia no autenticador e confirma com um código válido, **Then** o sistema ativa o 2FA e exibe 8 códigos de recuperação únicos para uso em emergência.
3. **Given** um usuário com 2FA ativo, **When** ele faz login com e-mail e senha corretos, **Then** o sistema solicita o código do aplicativo autenticador antes de conceder acesso.
4. **Given** o prompt de código 2FA, **When** o usuário informa um dos 8 códigos de recuperação, **Then** o sistema aceita o código, o invalida permanentemente para uso futuro, e concede acesso.
5. **Given** um usuário com 2FA ativo, **When** ele desativa o 2FA informando sua senha atual, **Then** o sistema remove a exigência de código no próximo login.

---

### User Story 5 - Gestão de Sessões e Perfil (Priority: P3)

Usuários podem visualizar todos os dispositivos onde estão autenticados, encerrar sessões indesejadas, atualizar dados do perfil (nome, avatar) e trocar a senha quando necessário. Isso permite controle sobre acessos não autorizados e manutenção de dados pessoais.

**Why this priority**: Importante para segurança e autonomia do usuário, mas pode ser entregue após os fluxos de autenticação principais estarem funcionando.

**Independent Test**: Pode ser testado autenticando o mesmo usuário em dois dispositivos, listando sessões, encerrando uma delas e verificando que o acesso naquele dispositivo é negado. Separadamente, atualizar o nome do usuário e confirmar que o dado novo aparece no perfil.

**Acceptance Scenarios**:

1. **Given** um usuário com múltiplas sessões ativas, **When** ele acessa Perfil → Sessões, **Then** o sistema lista todas as sessões com informações de dispositivo, localização aproximada e data de último acesso.
2. **Given** a lista de sessões, **When** o usuário encerra uma sessão específica, **Then** essa sessão é invalidada imediatamente e qualquer acesso posterior com aquele token é rejeitado.
3. **Given** a lista de sessões, **When** o usuário encerra todas as outras sessões, **Then** apenas a sessão atual permanece ativa; todas as demais são invalidadas.
4. **Given** um usuário autenticado, **When** ele atualiza nome ou avatar em Perfil, **Then** os dados são atualizados e refletidos na interface.
5. **Given** um usuário autenticado, **When** ele troca a senha informando a senha atual e a nova senha, **Then** o sistema valida a senha atual antes de aceitar a nova e aplica a alteração.

---

### User Story 6 - Acesso de Suporte pelo Admin SaaS (Priority: P3)

O administrador do SaaS pode acessar temporariamente o painel de um tenant como se fosse um tenant_admin, para fins de suporte e diagnóstico. O acesso dura no máximo 5 minutos, não pode ser renovado, e todas as ações realizadas ficam registradas com identificação clara do admin SaaS.

**Why this priority**: Funcionalidade operacional importante para suporte ao cliente, mas não bloqueia o lançamento do produto.

**Independent Test**: Pode ser testado com o saas_admin iniciando impersonation de um tenant, realizando uma ação no painel do tenant e verificando no log de auditoria que a ação aparece identificada como impersonation do saas_admin.

**Acceptance Scenarios**:

1. **Given** o saas_admin autenticado, **When** ele solicita acesso de suporte a um tenant específico, **Then** o sistema concede acesso temporário ao painel do tenant com duração máxima de 5 minutos.
2. **Given** o acesso temporário ativo, **When** o saas_admin realiza qualquer ação, **Then** o sistema registra a ação no log de auditoria identificando que foi executada via impersonation pelo saas_admin.
3. **Given** o acesso temporário ativo, **When** os 5 minutos expiram, **Then** o acesso é encerrado automaticamente e qualquer requisição subsequente é rejeitada.
4. **Given** o acesso temporário, **When** o saas_admin tenta estender ou renovar o acesso, **Then** o sistema rejeita — tokens de impersonation não são renováveis.

---

### Edge Cases

- O que acontece se o usuário abrir o formulário de login e o token de verificação anti-bot expirar antes de submeter (~5 min)? → O widget gera novo token automaticamente; o botão de login permanece desabilitado até o novo token ser emitido.
- O que acontece se um refresh token comprometido for usado após revogação? → O sistema detecta reutilização, revoga imediatamente todas as sessões ativas do usuário e retorna 401.
- O que acontece se o usuário perder todos os códigos de recuperação 2FA e o dispositivo autenticador? → O saas_admin pode desativar o 2FA manualmente via acesso administrativo direto.
- O que acontece com sessões simultâneas do mesmo usuário? → O sistema suporta múltiplas sessões simultâneas por padrão (cada login em dispositivo diferente gera uma sessão independente).
- O que acontece se a verificação anti-bot falhar por problema de rede com o serviço externo? → O sistema rejeita o login (fail-closed) — nenhuma tentativa é processada sem verificação bem-sucedida.
- O que acontece ao tentar login como saas_admin na URL do tenant e vice-versa? → O sistema rejeita — cada contexto (Admin SaaS, CRM do Tenant) só aceita as roles correspondentes.

## Requirements *(mandatory)*

### Functional Requirements

**Autenticação Base — US1**

- **FR-001**: O sistema DEVE permitir autenticação com e-mail e senha nos dois contextos independentes: Admin SaaS (`admin.omnideskcrm.com.br`) e CRM do Tenant (`{slug}.omnideskcrm.com.br`).
- **FR-002**: Após autenticação bem-sucedida, o sistema DEVE emitir um token de acesso de curta duração (15 minutos) e um token de renovação de longa duração.
- **FR-003**: O token de renovação DEVE ter duração de 7 dias por padrão; quando o usuário marca "lembrar-me", a duração DEVE ser de 30 dias.
- **FR-004**: O sistema DEVE renovar automaticamente o token de acesso usando o token de renovação, sem interromper o fluxo do usuário.
- **FR-005**: A cada renovação de sessão, o token de renovação antigo DEVE ser revogado e um novo emitido (rotação obrigatória).
- **FR-006**: O sistema DEVE detectar a reutilização de um token de renovação já revogado e invalidar imediatamente todas as sessões ativas do usuário.
- **FR-007**: O sistema DEVE validar a verificação anti-bot server-side antes de processar qualquer tentativa de login; ausência ou invalidade do token de verificação DEVE resultar em rejeição com status 403.
- **FR-008**: O sistema DEVE aplicar rate limiting de no máximo 5 tentativas falhas por IP + e-mail em janela de 10 minutos, bloqueando por 15 minutos após atingir o limite.
- **FR-009**: O sistema DEVE rejeitar login de usuários marcados como inativos.
- **FR-010**: O sistema DEVE rejeitar login de usuários com e-mail não verificado.
- **FR-011**: O token de acesso DEVE conter: identificador do usuário, perfil (role), identificador e slug do tenant, e-mail, timestamps de emissão e expiração.
- **FR-012**: Tokens de renovação DEVEM ser armazenados em cookie seguro inacessível ao JavaScript do browser, nunca em armazenamento local.
- **FR-013**: Senhas DEVEM ser armazenadas exclusivamente como hash criptográfico no banco de dados — nunca em texto plano ou formato reversível.
- **FR-014**: O sistema DEVE exigir senhas com no mínimo 8 caracteres.
- **FR-015**: Tokens de renovação DEVEM ser armazenados no banco apenas como hash criptográfico — nunca em texto plano.

**Recuperação de Senha — US2**

- **FR-016**: O sistema DEVE validar a verificação anti-bot server-side antes de processar solicitações de recuperação de senha.
- **FR-017**: O sistema DEVE retornar a mesma resposta de sucesso independentemente de o e-mail informado existir no sistema ou não (prevenção de enumeração de usuários).
- **FR-018**: O token de recuperação de senha DEVE expirar em 1 hora após emissão.
- **FR-019**: O uso bem-sucedido do token de recuperação DEVE invalidar todas as sessões ativas do usuário.
- **FR-020**: Tokens de recuperação já utilizados ou expirados DEVEM ser rejeitados sem processar alteração.

**Convite de Colaboradores — US3**

- **FR-021**: Apenas tenant_admin e supervisor DEVEM poder enviar convites para novos usuários em seu tenant.
- **FR-022**: O convite DEVE especificar o perfil (role) que será atribuído ao convidado ao aceitar.
- **FR-023**: O token de convite DEVE expirar em 72 horas após emissão.
- **FR-024**: Ao aceitar o convite, o usuário DEVE ser criado com e-mail verificado e ser autenticado automaticamente no tenant correspondente.
- **FR-025**: O sistema DEVE rejeitar envio de convites para e-mails já cadastrados no sistema.
- **FR-026**: Apenas um convite ativo por e-mail é permitido por vez; envio de novo convite para o mesmo e-mail DEVE invalidar o anterior.

**Verificação em Dois Fatores — US4**

- **FR-027**: O 2FA TOTP DEVE ser opcional para todas as roles na V1 — nenhuma role tem 2FA obrigatório.
- **FR-028**: A ativação do 2FA DEVE exigir confirmação com código válido do autenticador antes de ser habilitado.
- **FR-029**: Ao ativar o 2FA, o sistema DEVE gerar e exibir 8 códigos de recuperação de uso único.
- **FR-030**: O secret do 2FA DEVE ser armazenado criptografado em repouso — nunca em texto plano.
- **FR-031**: Após ativação do 2FA, o login com e-mail e senha DEVE retornar indicação intermediária exigindo o código TOTP antes de emitir os tokens de sessão.
- **FR-032**: Cada código de recuperação DEVE ser invalidado imediatamente após uso (uso único).

**Gestão de Sessões e Perfil — US5**

- **FR-033**: O usuário DEVE poder listar todas as suas sessões ativas com informações de dispositivo, IP de origem e data de último acesso.
- **FR-034**: O usuário DEVE poder encerrar qualquer sessão específica ou todas as sessões exceto a sessão atual.
- **FR-035**: O usuário DEVE poder atualizar seu nome e avatar no perfil.
- **FR-036**: O usuário DEVE poder alterar sua senha mediante informação da senha atual; a senha atual DEVE ser validada antes de aceitar a nova.

**Acesso de Suporte — US6**

- **FR-037**: O saas_admin DEVE poder gerar acesso temporário a um tenant específico com duração máxima de 5 minutos.
- **FR-038**: Tokens de acesso de suporte NÃO DEVEM ser renováveis — expiração definitiva em 5 minutos.
- **FR-039**: Todas as ações realizadas durante acesso de suporte DEVEM ser registradas em log de auditoria com identificação clara do saas_admin responsável.

### Key Entities

- **Usuário**: Representa qualquer pessoa autenticável no sistema. Possui perfil (role), vínculo com tenant (exceto saas_admin), status ativo/inativo, e-mail verificado/não-verificado, e configuração opcional de 2FA.
- **Sessão (Refresh Token)**: Representa uma sessão ativa de um usuário em um dispositivo específico. Armazena identificação do dispositivo, IP de origem e validade. Pode ser revogada individualmente ou em massa.
- **Convite**: Vínculo temporário (72h) associando um e-mail a um perfil e tenant. Usado para integrar novos colaboradores sem auto-cadastro.
- **Token de Recuperação de Senha**: Artefato temporário (1h) que autoriza redefinição de senha. Uso único; ao ser consumido, invalida todas as sessões ativas do usuário.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Usuários completam o fluxo de login (do formulário ao acesso ao painel) em menos de 5 segundos em condições normais de rede.
- **SC-002**: A renovação automática de sessão ocorre de forma transparente — o usuário não percebe interrupção nem é redirecionado para login durante uso ativo.
- **SC-003**: 100% das tentativas de login e recuperação de senha passam pela verificação anti-bot server-side — nenhuma requisição é processada sem token de verificação válido.
- **SC-004**: Senhas nunca são armazenadas ou transmitidas em texto plano em nenhum ponto do sistema.
- **SC-005**: Tokens de renovação comprometidos (reutilizados após revogação) resultam em invalidação de 100% das sessões do usuário em no máximo 1 segundo após a detecção.
- **SC-006**: O fluxo completo de recuperação de senha (solicitação → recebimento de e-mail → redefinição) é concluível em menos de 3 minutos em condições normais.
- **SC-007**: Novos colaboradores conseguem aceitar convite e acessar o sistema em menos de 2 minutos após receber o e-mail.
- **SC-008**: Usuários com 2FA ativo conseguem fazer login usando código TOTP sem etapas adicionais além do código.
- **SC-009**: Acessos de suporte (impersonation) expiram automaticamente em 5 minutos sem nenhuma ação do saas_admin.
- **SC-010**: 100% das ações realizadas durante acesso de suporte são rastreáveis no log de auditoria com identificação do saas_admin responsável.

## Assumptions

- O serviço de e-mail (SendGrid) está disponível e configurado para envio de convites e links de recuperação de senha.
- A verificação anti-bot (Cloudflare Turnstile, definida na Spec 001 — Padrões Técnicos Globais) já está integrada nos formulários de login e recuperação de senha de ambos os frontends; esta spec assume que a integração existe e define apenas o comportamento esperado.
- O OmniDesk não oferece auto-cadastro — todos os usuários de tenant são criados via convite; saas_admin é provisionado manualmente na implantação inicial.
- O perfil (role) do usuário é definido no momento do convite e não pode ser alterado pelo próprio usuário.
- O log de auditoria referenciado em FR-039 (impersonation) é um sistema existente ou a ser implementado; esta spec define apenas que os registros devem existir, não sua estrutura interna.
- Tokens de acesso são mantidos exclusivamente em memória no frontend (não em localStorage ou sessionStorage), conforme Spec 001.
- O sistema opera com HTTPS obrigatório em produção; o comportamento seguro dos cookies de sessão depende dessa configuração.
- A distinção entre contexto Admin SaaS e CRM do Tenant é feita pelo domínio de acesso — o backend valida que o perfil do usuário é compatível com o contexto solicitado.
