# Spec 03 — Tenants (Provisionamento)
**Versão:** 1.0
**Status:** Aprovado
**Última atualização:** 2026-05

---

## 1. Visão Geral

O módulo de Tenants é gerenciado exclusivamente pelo operador do SaaS via painel admin. Cada tenant representa uma empresa cliente (sempre pessoa jurídica). O provisionamento cria automaticamente todos os recursos isolados do tenant (schema Postgres, bucket MinIO, database MongoDB, prefixos Redis) e envia um e-mail de boas-vindas ao responsável técnico com as credenciais de acesso.

---

## 2. Atores

| Ator | Descrição |
|---|---|
| Operador SaaS | Único usuário do painel admin. Cria e gerencia todos os tenants. |
| Super Admin do Tenant | Usuário criado automaticamente no provisionamento. Acessa o CRM do próprio tenant. |

---

## 3. Fluxo de Provisionamento

```
Operador preenche formulário de novo tenant
  ↓
Validações (CNPJ único, slug único, e-mail único)
  ↓
API cria registro em public.tenants
  ↓
Worker executa provisionamento:
  ├── Cria schema Postgres: tenant_{slug}
  ├── Executa migrations no novo schema
  ├── Cria bucket MinIO: tenant-{slug}
  ├── Cria database MongoDB: tenant_{slug}
  └── Reserva prefixo Redis: {slug}:*
  ↓
Cria usuário Super Admin do tenant
  ├── Email: responsável técnico cadastrado
  ├── Senha: gerada automaticamente (12 chars, forte)
  └── Role: tenant_admin
  ↓
Envia e-mail de boas-vindas ao responsável técnico
  ↓
Tenant fica com status "Ativo"
```

---

## 4. Entidades

### 4.1 Tenant (`public.tenants`)

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK gerado automaticamente |
| `slug` | varchar(50) | sim | Prefixo único. Usado no subdomínio e em todos os recursos (schema, bucket, database, Redis). Apenas letras minúsculas, números e hífen. Ex: `clinica-abc` |
| `razao_social` | varchar(255) | sim | Razão social da empresa |
| `nome_fantasia` | varchar(255) | não | Nome fantasia |
| `cnpj` | varchar(18) | sim | CNPJ formatado. Único no sistema. |
| `status` | enum | sim | `active`, `blocked`, `provisioning`, `error` |
| `openai_api_key` | varchar(255) | não | API Key da OpenAI (criptografada em repouso) |
| `openai_organization` | varchar(255) | não | Organization ID da OpenAI |
| `openai_project` | varchar(255) | não | Project/Workspace ID da OpenAI |
| `timezone` | varchar(50) | sim | Fuso horário do tenant. Default: `America/Sao_Paulo`. Formato IANA. Opções V1: `America/Sao_Paulo`, `America/Manaus`, `America/Belem`, `America/Fortaleza`, `America/Recife`, `America/Noronha`, `America/Porto_Velho`, `America/Boa_Vista`, `America/Rio_Branco`. |
| `locale` | varchar(10) | sim | Locale BCP 47. V1: fixo `pt-BR`. |
| `currency` | varchar(3) | sim | Código de moeda ISO 4217. V1: fixo `BRL`. |
| `date_format` | varchar(20) | sim | Formato de data para exibição. V1: fixo `dd/MM/yyyy`. |
| `created_at` | timestamptz | sim | — |
| `updated_at` | timestamptz | sim | — |
| `blocked_at` | timestamptz | não | Preenchido ao bloquear acesso |

### 4.2 Contatos do Tenant (`public.tenant_contacts`)

Cada tenant tem exatamente dois contatos obrigatórios: financeiro e responsável técnico.

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `id` | UUID | sim | PK |
| `tenant_id` | UUID | sim | FK → tenants |
| `type` | enum | sim | `financial`, `technical` |
| `name` | varchar(255) | sim | Nome completo |
| `email` | varchar(255) | sim | E-mail |
| `phone` | varchar(20) | sim | Telefone com DDD |

---

## 5. Regras de Negócio

### 5.1 Slug

- Apenas letras minúsculas, números e hífen (`[a-z0-9-]`)
- Mínimo 3, máximo 50 caracteres
- Único no sistema — validado antes de salvar
- **Imutável após criação** — alterar o slug quebraria subdomínio, schema, bucket e todos os recursos. Não expor opção de edição.
- Gera automaticamente os recursos:
  - Subdomínio: `{slug}.omnideskcr m.com.br`
  - Schema Postgres: `tenant_{slug}` (hífens viram underscore)
  - Bucket MinIO: `tenant-{slug}`
  - Database MongoDB: `tenant_{slug}` (hífens viram underscore)
  - Prefixo Redis: `{slug}:`

### 5.2 CNPJ

- Validação de formato e dígitos verificadores
- Único no sistema

### 5.3 OpenAI

- Os campos de OpenAI são opcionais no cadastro — podem ser configurados depois
- Se não configurado, o sistema usa as credenciais globais do operador (definidas em variável de ambiente da API)
- A API Key é **criptografada** antes de salvar no banco (AES-256) e **nunca retornada em texto plano** nas respostas da API — apenas um indicador de que está configurada

### 5.4 Status do Tenant

| Status | Descrição | Comportamento |
|---|---|---|
| `provisioning` | Provisionamento em andamento | CRM inacessível. Admin exibe progresso. |
| `active` | Operacional | Acesso normal ao CRM |
| `blocked` | Bloqueado pelo operador | CRM exibe tela de acesso suspenso. API retorna 403 em todas as rotas do tenant. |
| `error` | Falha no provisionamento | Admin exibe log de erro. Operador pode retentar. |

### 5.5 Bloqueio de Acesso

- Operador clica em "Bloquear acesso" no admin
- Status muda para `blocked`, `blocked_at` é preenchido
- Todas as sessões ativas do tenant são invalidadas (Redis: delete `{slug}:session:*`)
- CRM do tenant exibe página: "Acesso suspenso. Entre em contato com o suporte."
- Operador pode desbloquear com um clique (status volta para `active`)

### 5.6 Redefinição de Senha do Super Admin

- Operador clica em "Redefinir senha" no admin
- Nova senha forte é gerada automaticamente
- Senha é enviada por e-mail ao responsável técnico
- Sessões ativas do Super Admin são invalidadas

### 5.7 Impersonation (Acesso do Operador ao CRM do Tenant)

- No admin, o operador clica em "Acessar ambiente" em qualquer tenant
- A API gera um token JWT de curta duração (15 min, não renovável) com claims:
  - `role: saas_admin`
  - `impersonating: true`
  - `tenant_slug: {slug}`
- O operador é redirecionado para `{slug}.omnicare.ia.br` já autenticado
- O token de impersonation **não pode ser trocado por refresh token**
- Uma barra de aviso visível no topo do CRM indica: "Você está acessando como operador SaaS"
- Nenhuma senha do tenant é compartilhada ou necessária

---

## 6. Templates de Agentes de IA (pré-configurados)

O admin permite criar **templates globais de agentes** que são aplicados automaticamente no provisionamento de cada novo tenant.

### 6.1 Templates padrão criados na instalação do sistema

| Nome | Tipo | Descrição para Orchestrator |
|---|---|---|
| Agente Principal | `orchestrator` | Ponto de entrada. Faz saudação, qualifica o cliente e decide qual agente acionar. |
| Recepção | `sub_agent` | Responsável por informações gerais, localização, horários de funcionamento e primeiro contato. |
| Vendas | `sub_agent` | Responsável por apresentar procedimentos/serviços, passar valores iniciais e conduzir o lead ao agendamento de avaliação. |
| Pós-Vendas | `sub_agent` | Responsável por clientes que já realizaram procedimentos: dúvidas, retornos e satisfação. |
| Suporte | `sub_agent` | Responsável por problemas, reclamações e situações que exigem atenção especial. |

### 6.2 Comportamento no Provisionamento

- Ao criar um novo tenant, todos os templates ativos são copiados para o schema do tenant como agentes reais
- O tenant pode editar nome, descritivo e prompt de cada agente copiado
- Alterações nos templates globais **não afetam** tenants já provisionados (cópia, não referência)

### 6.3 Gestão de Templates no Admin

- Operador pode criar, editar e desativar templates
- Template marcado como inativo não é aplicado em novos provisionamentos
- Templates não podem ser deletados se já foram usados em algum provisionamento (soft delete)

---

## 7. E-mail de Boas-Vindas

Enviado via SendGrid ao e-mail do responsável técnico após provisionamento bem-sucedido.

**Conteúdo:**
```
Assunto: Seu ambiente OmniDesk está pronto — {Nome Fantasia}

Olá, {Nome do Responsável Técnico}!

Seu ambiente OmniDesk foi configurado com sucesso.

Acesse em: https://{slug}.omnicare.ia.br

Usuário: {email do responsável técnico}
Senha:   {senha gerada}

Recomendamos que você altere sua senha no primeiro acesso.

Qualquer dúvida, entre em contato com o suporte.
```

---

## 8. Dashboard de Saúde (Admin)

Painel de monitoramento no admin, atualizado a cada 60 segundos (polling) ou sob demanda.

### 8.1 Visão Global

Lista de todos os tenants com indicadores de saúde em linha:

| Coluna | Descrição |
|---|---|
| Tenant | Nome fantasia + slug |
| Status | Badge: Ativo / Bloqueado / Provisionando / Erro |
| Postgres | ✅ Conectado / ❌ Erro — tamanho do schema em MB |
| Redis | ✅ Conectado / ❌ Erro — memória usada (prefixo do tenant) |
| MongoDB | ✅ Conectado / ❌ Erro — tamanho do database em MB |
| Chats | Total de conversas nos últimos 30 dias |
| Tickets | Total de tickets abertos |
| Usuários | Total de atendentes cadastrados |
| OpenAI | ✅ Configurado (própria key) / ⚙️ Usando key global |
| Ações | Acessar / Bloquear / Redefinir senha |

### 8.2 Visão Individual do Tenant

Ao clicar em um tenant, painel detalhado com:
- Dados cadastrais completos
- Contatos (financeiro e técnico)
- Configuração OpenAI (indicador se está configurada, sem expor a key)
- Métricas detalhadas:
  - Postgres: tamanho por tabela principal
  - Redis: total de chaves, memória
  - MongoDB: total de documentos, tamanho
  - MinIO: total de arquivos, tamanho em MB
  - Conversas: total, por canal (WhatsApp / Live Chat), por período
  - Tickets: por status, por departamento
  - Agendamentos: total, por período
- Log dos últimos eventos de provisionamento/bloqueio

### 8.3 Coleta de Métricas

- As métricas são coletadas por um **background job** (Hangfire) que roda a cada 5 minutos
- Resultados são cacheados no Redis com TTL de 5 minutos: `saas:metrics:{tenant_slug}`
- O admin lê sempre do cache — nunca executa queries pesadas em tempo real

---

## 9. Endpoints da API

Todos protegidos por `role: saas_admin`.

```
GET    /api/admin/tenants                    → listar tenants com métricas resumidas
GET    /api/admin/tenants/{id}               → detalhar tenant
POST   /api/admin/tenants                    → criar tenant (inicia provisionamento)
PUT    /api/admin/tenants/{id}               → editar dados cadastrais
POST   /api/admin/tenants/{id}/block         → bloquear acesso
POST   /api/admin/tenants/{id}/unblock       → desbloquear acesso
POST   /api/admin/tenants/{id}/reset-password → redefinir senha do super admin
POST   /api/admin/tenants/{id}/impersonate   → gerar token de impersonation
GET    /api/admin/tenants/{id}/metrics       → métricas detalhadas do tenant
POST   /api/admin/tenants/{id}/retry-provisioning → retentar provisionamento com erro

GET    /api/admin/agent-templates            → listar templates de agentes
POST   /api/admin/agent-templates            → criar template
PUT    /api/admin/agent-templates/{id}       → editar template
DELETE /api/admin/agent-templates/{id}       → desativar template (soft delete)
```

---

## 10. Critérios de Aceite

- [ ] Não é possível criar dois tenants com o mesmo slug
- [ ] Não é possível criar dois tenants com o mesmo CNPJ
- [ ] O slug só aceita letras minúsculas, números e hífen
- [ ] Após provisionamento, o schema Postgres existe e migrations foram aplicadas
- [ ] Após provisionamento, o bucket MinIO existe
- [ ] Após provisionamento, o database MongoDB existe
- [ ] O e-mail de boas-vindas é enviado apenas após provisionamento bem-sucedido
- [ ] Se o provisionamento falhar, o status fica como `error` e nenhum e-mail é enviado
- [ ] Tenant bloqueado recebe 403 em todas as rotas do CRM
- [ ] Tenant bloqueado tem todas as sessões ativas invalidadas imediatamente
- [ ] O token de impersonation expira em 15 minutos e não pode ser renovado
- [ ] A API Key da OpenAI nunca é retornada em texto plano na API
- [ ] As métricas do dashboard são lidas do cache Redis, não do banco em tempo real
- [ ] Templates de agentes são copiados (não referenciados) no provisionamento
