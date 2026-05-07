# Spec 04 — Roles e Permissões
**Versão:** 1.0
**Status:** Aprovado
**Última atualização:** 2026-05

---

## 1. Visão Geral

O OmniDesk possui dois contextos de acesso completamente separados:

| Contexto | URL | Quem acessa |
|---|---|---|
| **Painel Admin (SaaS)** | `admin.omnicare.ia.br` | Apenas o Operador SaaS |
| **CRM do Tenant** | `{slug}.omnicare.ia.br` | Usuários internos da empresa (tenant) |

As roles são independentes entre contextos — um usuário nunca pertence aos dois ao mesmo tempo, com exceção do token de impersonation (`saas_admin` acessando CRM de um tenant).

---

## 2. Roles Definidas

### 2.1 Roles do Painel Admin (SaaS)

| Role | Nome de Exibição | Descrição |
|---|---|---|
| `saas_admin` | Operador SaaS | Único usuário do painel admin. Gerencia todos os tenants, templates de agentes e saúde do sistema. Não há múltiplos usuários com esta role na V1. |

### 2.2 Roles do CRM do Tenant

| Role | Nome de Exibição | Descrição |
|---|---|---|
| `tenant_admin` | Administrador | Super admin do tenant. Criado automaticamente no provisionamento. Acesso total ao CRM do próprio tenant. |
| `supervisor` | Supervisor | Gerencia departamentos, atendentes, agentes de IA e pipeline Kanban. Acesso operacional completo. Não acessa configurações de sistema (slug, CNPJ, OpenAI, ligar/desligar widget, domínios). |
| `attendant` | Atendente | Operador humano do chat. Acesso restrito ao atendimento: conversas, tickets atribuídos, respostas pré-formadas. |

---

## 3. Hierarquia de Acesso (CRM)

```
tenant_admin
    └── supervisor
            └── attendant
```

- `tenant_admin` pode fazer tudo que `supervisor` e `attendant` fazem
- `supervisor` pode fazer tudo que `attendant` faz, mais configurações operacionais
- `attendant` tem acesso apenas ao necessário para atender clientes

---

## 4. Matriz de Permissões

### 4.1 Painel Admin (Spec 03)

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

### 4.2 Departamentos (Spec 05)

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

### 4.3 Live Chat — Configuração do Widget (Spec 07)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Ver configuração do widget | ✅ | ✅ | ❌ |
| Editar aparência, identificação, comportamento | ✅ | ✅ | ❌ |
| Editar termos de privacidade / LGPD | ✅ | ✅ | ❌ |
| Editar domínios autorizados | ✅ | ❌ | ❌ |
| Ligar / desligar widget globalmente | ✅ | ❌ | ❌ |
| Ver código de instalação | ✅ | ✅ | ❌ |

### 4.4 Live Chat — Conversas (Spec 07)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Ver todas as conversas do tenant | ✅ | ✅ | ❌ |
| Ver conversas do próprio departamento | ✅ | ✅ | ✅ |
| Ver conversas atribuídas a si | ✅ | ✅ | ✅ |
| Encerrar conversa manualmente | ✅ | ✅ | ✅ (apenas as atribuídas) |
| Gerenciar permissões de browser notification | ✅ | ✅ | ✅ (apenas as próprias) |

### 4.5 Agentes de IA (Spec 06)

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

### 4.6 Tickets (Spec 09)

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

### 4.7 Contatos (Spec 09)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Listar / buscar / ver perfil do contato | ✅ | ✅ | ✅ |
| Ver histórico de tickets e conversas | ✅ | ✅ | ✅ |
| Criar / editar contato | ✅ | ✅ | ✅ |

### 4.8 WhatsApp — Configuração e Templates (Spec 08)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Ver status e dados do canal (exceto access_token) | ✅ | ✅ | ❌ |
| Editar configuração do canal (Phone ID, WABA ID, nome) | ✅ | ❌ | ❌ |
| Editar / visualizar Access Token | ✅ | ❌ (só sabe se está configurado) | ❌ |
| Ativar / desativar canal WhatsApp | ✅ | ❌ | ❌ |
| Ver templates | ✅ | ✅ | ❌ |
| Criar / editar template | ✅ | ✅ | ❌ |
| Submeter template para aprovação da Meta | ✅ | ✅ | ❌ |

### 4.9 Notificações (Spec 10)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Ver próprias notificações in-app | ✅ | ✅ | ✅ |
| Marcar notificações como lidas | ✅ | ✅ | ✅ |
| Configurar preferências de push (próprias) | ✅ | ✅ | ✅ |
| Configurar notificações para clientes (tenant) | ✅ | ✅ | ❌ |

### 4.10 Agenda e Catálogo de Serviços (Spec 11)

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

### 4.11 Auditoria (Spec 12)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Ver atividade recente (listagem no CRM) | ✅ | ❌ | ❌ |
| Criar / revogar API Keys de auditoria | ✅ | ❌ | ❌ |

### 4.12 Autenticação (Spec 02)

| Ação | `tenant_admin` | `supervisor` | `attendant` |
|---|---|---|---|
| Enviar convite para novo usuário | ✅ | ✅ (roles attendant/supervisor) | ❌ |
| Desativar / reativar usuário | ✅ | ❌ | ❌ |
| Ver sessões ativas próprias | ✅ | ✅ | ✅ |
| Encerrar própria sessão | ✅ | ✅ | ✅ |
| Ativar / desativar 2FA (próprio) | ✅ | ✅ | ✅ |

---

## 5. Regras Gerais de Acesso

### 5.1 Isolamento Multi-tenant

- Um usuário de um tenant **nunca** pode acessar dados de outro tenant
- Todas as queries da API são resolvidas no contexto do tenant identificado pelo subdomínio
- O middleware de resolução de tenant é executado antes de qualquer autorização de role

### 5.2 Impersonation (saas_admin → CRM do tenant)

- O Operador SaaS pode acessar o CRM de qualquer tenant via token de impersonation
- Token: JWT de 5 minutos, não renovável (conforme Spec 02)
- Claims: `role: saas_admin`, `impersonating: true`, `tenant_slug: {slug}`, `impersonated_by: "saas_admin"`
- Uma barra de aviso visível no topo do CRM indica o modo impersonation
- Todas as ações realizadas durante a impersonation são auditadas com `impersonated_by` (conforme Spec 12)

### 5.3 Attendant — Escopo por Departamento

- Um `attendant` enxerga apenas conversas e tickets dos departamentos aos quais pertence
- Se pertencer a múltiplos departamentos, enxerga tudo dos departamentos vinculados
- Não enxerga conversas de departamentos aos quais não pertence, mesmo dentro do mesmo tenant

### 5.4 Criação de Usuários

- Apenas `tenant_admin` e `supervisor` podem criar novos atendentes
- `tenant_admin` é o único que pode criar um `supervisor`
- Não é possível criar um usuário com role `saas_admin` via CRM — essa role é exclusiva do painel admin e configurada no provisionamento do sistema

### 5.5 Soft Delete vs. Desativação

- Usuários não são deletados fisicamente — apenas desativados (`is_active = false`)
- Usuário desativado perde acesso imediatamente (sessões invalidadas no Redis)
- Histórico de atendimentos do usuário desativado é preservado

---

## 6. Tokens e Autenticação

| Token | Tipo | TTL | Renovável | Escopo |
|---|---|---|---|---|
| Access token (JWT) | Bearer | 15 min | ✅ (via refresh token) | Acesso ao CRM do tenant |
| Refresh token | Cookie HttpOnly | 7d (padrão) / 30d (remember me) | ✅ (rotativo) | Renovação do access token |
| Token de impersonation | Bearer | 5 min | ❌ | Acesso temporário ao CRM por saas_admin |
| `widget_token` | UUID público | Não expira | N/A | Identificação do tenant nas requisições públicas do widget |
| Invite token | URL param | 72h | ❌ | Convite de novo usuário |
| Reset token | URL param | 1h | ❌ | Redefinição de senha |

---

## 7. Escopo V2

O único item de permissões pendente para versões futuras:

| Item | Quando |
|---|---|
| Permissões sobre Billing / Planos (limites de uso, upgrade, downgrade) | Spec V2 |
