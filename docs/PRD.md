# PRD — OmniDesk CRM
**Product Requirements Document**
**Versão:** 1.0 — Draft
**Status:** Em revisão
**Última atualização:** 2026-05

---

## 1. Visão Geral

### 1.1 O Produto

OmniDesk é um Mini CRM SaaS focado em atendimento omnichannel para clínicas e pequenas empresas de serviços. O sistema centraliza conversas vindas de Live Chat (widget em site) e WhatsApp, utiliza um Agente de IA como primeiro ponto de contato, e faz a transição fluída para atendentes humanos quando necessário — abrindo tickets e gerenciando o ciclo de atendimento até a resolução.

### 1.2 O Problema que Resolve

Clínicas e empresas de serviços perdem clientes todos os dias por:
- Demora no primeiro atendimento (10 min a 2h é o padrão atual do mercado-alvo)
- Falta de atendimento fora do horário comercial
- Falta de tato ao abordar preço e conduzir o lead para conversão
- Ausência de rastreabilidade dos atendimentos (quem falou, o que foi dito, qual foi o resultado)
- Atendimento descentralizado (cada secretária no próprio celular, sem visibilidade gerencial)

### 1.3 A Solução

Um sistema onde:
1. O cliente entra em contato via WhatsApp ou Live Chat no site
2. Um Agente de IA responde imediatamente, 24/7, com linguagem consultiva e contextualizada
3. Quando o cliente deseja falar com um humano, ou quando a IA detecta a necessidade, o atendimento é transferido para o departamento correto
4. Um ticket é aberto automaticamente, visível no painel CRM
5. O atendente humano assume a conversa diretamente na plataforma
6. Todo o ciclo fica registrado, rastreável e auditável

---

## 2. Personas

### 2.1 Persona Principal — O Gestor da Clínica

**Quem é:** Dono ou gerente de uma clínica de estética médica, consultório odontológico, clínica de exames ou serviços similares.

**Perfil:**
- Pequena empresa, 2 a 15 funcionários
- Usa WhatsApp como principal canal de atendimento
- Tem 1 a 3 secretárias respondendo manualmente
- Recebe 30 a 100 mensagens/dia dependendo de campanhas ativas
- Ticket médio alto (R$ 3.000 a R$ 10.000 por cliente)
- Taxa de conversão atual baixa (5% a 15%) por falhas no atendimento

**Dores:**
- "Perco cliente porque demorei a responder"
- "Minha secretária não sabe vender, só informa preço"
- "Não tenho visibilidade do que está sendo dito para os clientes"
- "Fim de semana e depois das 18h fico sem atendimento"

**O que quer do sistema:**
- Atendimento instantâneo 24/7 sem contratar mais pessoas
- IA que conduz o lead para agendamento de avaliação (não que só informe preço)
- Visão centralizada de todos os atendimentos
- Histórico completo de cada cliente

### 2.2 Persona Secundária — A Secretária / Atendente Humana

**Quem é:** Funcionária que hoje responde WhatsApp e telefone manualmente.

**Perfil:**
- Conhecimento técnico básico
- Acostumada com WhatsApp Web e sistemas simples
- Sobrecarga: responde múltiplos clientes ao mesmo tempo

**O que precisa do sistema:**
- Interface simples, sem curva de aprendizado
- Ver exatamente o histórico da conversa antes de assumir
- Saber o que a IA já disse para não repetir informações
- Receber notificação quando um atendimento precisar dela

### 2.3 Persona do Operador (você — dono do SaaS)

**Quem é:** Você, que vende e opera o OmniDesk para as clínicas.

**O que precisa:**
- Painel admin para provisionar novas clínicas (tenants)
- Configurar canais (WhatsApp, Live Chat) por cliente
- Monitorar saúde do sistema
- Sem complexidade de planos/billing automático na V1 (cobrança é manual/negociada)

---

## 3. Escopo do MVP (V1)

### 3.1 O que ESTÁ no MVP

| Módulo | Descrição |
|---|---|
| Multi-tenant | Provisionamento de clientes (tenants) via painel admin |
| Live Chat | Widget instalável em qualquer site via script JS |
| WhatsApp | Integração com API Oficial Meta (Business API) |
| Agente de IA | Primeiro atendimento automatizado via OpenAI |
| Transbordo | Transferência de conversa da IA para atendente humano |
| Tickets / CRM | Abertura de tickets, pipelines por departamento, kanban |
| Agenda Própria | Agendamento simples com disponibilidade configurável |
| Departamentos | Cadastro de departamentos e atendentes por clínica |
| Notificações | Notificações de ticket no chat, opt-in por atualização |
| Painel Admin | Provisionamento e configuração de tenants (uso interno) |
| Painel CRM | Interface da clínica: atendimentos, tickets, agenda |

### 3.2 O que NÃO está no MVP (V2+)

| Funcionalidade | Justificativa |
|---|---|
| Cobrança automática (Pagar.me) | Billing do SaaS negociado manualmente na V1 |
| Pagamento de avaliação online | Aguarda definição de fluxo de agendamento avançado |
| RAG / Base de conhecimento | Configuração feita diretamente no prompt do agente |
| Remarketing / Campanhas ativas | Fora do escopo de atendimento reativo |
| Integração com agendas de terceiros | Agenda própria resolve o MVP |
| Portal do paciente / Site de vendas | Escopo separado |
| App mobile nativo | Web responsivo atende no MVP |

---

## 4. Funcionalidades Detalhadas

### 4.1 Módulo de Canais — Live Chat

- Widget JavaScript instalável via `<script>` tag em qualquer site
- Personalização básica: cor, nome da empresa, avatar, mensagem de boas-vindas
- Identificação do visitante: nome e e-mail (opcional, solicitado pela IA)
- Histórico de conversa persistido no navegador (localStorage) e no servidor
- Indicador de "digitando..." em tempo real (WebSocket)
- Suporte a envio de imagens e arquivos

### 4.2 Módulo de Canais — WhatsApp

- Integração via API Oficial Meta (WhatsApp Business Platform)
- Número por tenant (cada clínica tem seu próprio número configurado)
- Recepção e envio de mensagens de texto, imagens e documentos
- Webhook para recebimento de mensagens em tempo real
- Gestão de templates aprovados pela Meta (para mensagens ativas)

### 4.3 Módulo de Agente de IA

- Powered by OpenAI (GPT-4o como modelo principal, via OpenAI Agents SDK)
- Arquitetura de **orquestração dinâmica** com dois níveis:

#### Agente Principal (Orchestrator) — obrigatório por tenant
  - Ponto de entrada de todas as conversas
  - Responsável pelo primeiro contato, saudação e qualificação inicial
  - Conhece todos os sub-agentes cadastrados pelo tenant via seus descritivos
  - Decide automaticamente qual sub-agente acionar com base no contexto da conversa
  - Prompt base editável no painel admin pelo operador do SaaS
  - Fallback: se nenhum sub-agente for adequado, o orchestrator mantém a conversa ou aciona transbordo humano

#### Sub-agentes (dinâmicos) — criados pelo tenant
  - Cada tenant cadastra seus próprios sub-agentes no painel CRM
  - Cada sub-agente possui:
    - **Nome** (ex: "Agente Comercial", "Agente de Suporte Pós-Procedimento")
    - **Descritivo curto** — usado pelo orchestrator para decidir quando acionar (ex: "Responsável por tirar dúvidas sobre procedimentos, passar valores iniciais e conduzir o lead ao agendamento de avaliação")
    - **Prompt completo** — instruções detalhadas de comportamento, tom e regras de negócio
    - **Departamento vinculado** — ao acionar transbordo, o ticket vai para o departamento correto
    - **Status** (ativo/inativo)
  - Exemplos de sub-agentes típicos para clínica:
    - Agente Comercial (leads novos, procedimentos, preços)
    - Agente de Retorno (clientes que já fizeram procedimentos)
    - Agente Financeiro (dúvidas sobre pagamento, parcelas)

#### Fluxo de orquestração
  1. Cliente envia mensagem → Orchestrator recebe
  2. Orchestrator analisa intenção + lista de sub-agentes disponíveis
  3. Orquestra handoff para o sub-agente mais adequado
  4. Sub-agente assume a conversa mantendo contexto completo
  5. Se necessário, sub-agente pode devolver ao orchestrator ou acionar transbordo humano

#### Detecção de transbordo para humano
  - Palavras-chave explícitas: "quero falar com alguém", "atendente", "humano", "gerente"
  - Frustração detectada: 3+ mensagens sem resolução satisfatória
  - Solicitação de agendamento quando regras de negócio exigem confirmação humana
  - Ao detectar transbordo:
    1. Agente informa o cliente e confirma o departamento
    2. Ticket aberto automaticamente com histórico completo
    3. Atendente humano do departamento é notificado

### 4.4 Módulo de Tickets / CRM

- Ticket criado automaticamente ao transferir para humano
- Campos do ticket:
  - Protocolo único (gerado automaticamente)
  - Canal de origem (WhatsApp / Live Chat)
  - Cliente (nome, contato)
  - Departamento responsável
  - Atendente responsável
  - Status (Novo, Em Andamento, Aguardando Cliente, Resolvido, Cancelado)
  - Prioridade (Baixa, Normal, Alta, Urgente)
  - Tags livres
  - Histórico completo da conversa (incluindo o que a IA disse)
  - Anotações internas (visíveis só para atendentes)
- Pipeline Kanban por departamento (colunas configuráveis)
- Filtros: por status, atendente, departamento, período, canal, tag
- Busca full-text no histórico de conversas

### 4.5 Módulo de Agenda

- Cadastro de disponibilidade semanal por profissional (dias e horários)
- Bloqueio de datas/horários específicos
- Regras por tipo de cliente:
  - **Novo cliente:** agendamento de avaliação (requer confirmação manual ou pagamento — V2)
  - **Cliente retorno:** agendamento direto pela IA ou pelo atendente
- A IA pode oferecer slots disponíveis e confirmar agendamento no chat
- Confirmação automática por WhatsApp 24h antes
- Visão de agenda no painel CRM (lista e grade semanal)

### 4.6 Módulo de Departamentos e Atendentes

- Cadastro de departamentos por tenant (ex: Comercial, Financeiro, Suporte)
- Cada departamento tem:
  - Nome, descrição
  - Fila de tickets própria
  - Pipeline Kanban próprio (colunas customizáveis)
  - Atendentes vinculados
- Atendentes:
  - Nome, e-mail, senha, foto
  - Status online/ausente/offline (manual ou por horário)
  - Histórico de atendimentos
  - Limite de atendimentos simultâneos (configurável)

### 4.7 Módulo de Notificações

- Notificação no chat ao cliente quando ticket é aberto (protocolo)
- Notificação ao cliente a cada atualização de status do ticket (opt-in)
- Notificação interna para atendente (in-app + e-mail via SendGrid) ao receber ticket
- Cada atualização de ticket tem campo: "Notificar cliente? Sim / Não"
- Mensagem de notificação editável antes de enviar

---

## 5. Arquitetura de Alto Nível

```
┌─────────────────────────────────────────────┐
│              Clientes Finais                 │
│    (WhatsApp / Live Chat no site)            │
└────────────┬────────────────────────────────┘
             │
             ▼
┌─────────────────────────────────────────────┐
│              API Gateway (.NET 10)           │
│         Webhook WA │ WebSocket Chat          │
└────────────┬────────────────────────────────┘
             │
      ┌──────┴──────┐
      ▼             ▼
┌──────────┐  ┌──────────────┐
│  Agente  │  │  Atendente   │
│   IA     │  │   Humano     │
│ (OpenAI) │  │  (CRM Web)   │
└──────────┘  └──────────────┘
             │
      ┌──────┴────────────────┐
      ▼                       ▼
┌──────────┐           ┌──────────┐
│ Postgres │           │  Redis   │
│(schema/  │           │  (fila   │
│ tenant)  │           │  cache)  │
└──────────┘           └──────────┘
      │
      ▼
┌──────────┐  ┌──────────┐  ┌──────────┐
│ MongoDB  │  │  MinIO   │  │SendGrid  │
│  (logs)  │  │ (arquiv.)│  │ (e-mail) │
└──────────┘  └──────────┘  └──────────┘
```

---

## 6. Modelo Multi-tenant

- **Estratégia:** Shared infrastructure com isolamento por schema Postgres
- Cada tenant tem seu próprio schema: `tenant_{slug}`
- Schema `public` contém apenas tabelas de sistema (tenants, planos futuros)
- Todas as queries passam pelo middleware de resolução de tenant (por subdomínio ou header)
- Subdomínio padrão: `{cliente}.omnicare.ia.br`
- Tenant resolvido via subdomínio no request

---

## 7. Integrações (V1)

| Integração | Finalidade | Status |
|---|---|---|
| OpenAI API | Motor dos Agentes de IA | MVP |
| WhatsApp Business API (Meta) | Canal de atendimento | MVP |
| SendGrid | E-mails transacionais e notificações | MVP |

---

## 8. Premissas e Restrições

- O sistema não substitui sistemas de prontuário eletrônico (fora do escopo)
- A base de conhecimento do Agente de IA (RAG) é configurada externamente na V1 — o prompt base é editável no painel admin
- Sem app mobile nativo na V1 — interface web responsiva
- O painel admin é de uso exclusivo do operador do SaaS (não exposto ao cliente final)
- Todos os dados são armazenados em território nacional (LGPD)
- O sistema deve estar em conformidade básica com a LGPD: consentimento de coleta de dados no Live Chat, retenção de dados configurável por tenant

---

## 9. Métricas de Sucesso (pós-lançamento)

| Métrica | Meta inicial |
|---|---|
| Tempo médio de primeira resposta | < 5 segundos (IA) |
| Taxa de resolução pela IA sem transbordo | > 40% |
| Tempo médio de resposta do atendente humano | < 10 minutos |
| Uptime da plataforma | > 99,5% |
| NPS dos tenants (clínicas) | > 60 |

---

## 10. Glossário

| Termo | Definição |
|---|---|
| Tenant | Uma clínica ou empresa cliente do SaaS |
| Canal | Meio de comunicação (WhatsApp, Live Chat) |
| Agente de IA | Bot inteligente que faz o primeiro atendimento |
| Transbordo | Transferência do atendimento da IA para humano |
| Ticket | Registro formal de um atendimento no CRM |
| Protocolo | Número único identificador de um ticket |
| Atendente | Funcionário humano que opera o CRM |
| Departamento | Grupo de atendentes (ex: Comercial, Suporte) |
| Pipeline | Sequência de colunas Kanban de um departamento |
