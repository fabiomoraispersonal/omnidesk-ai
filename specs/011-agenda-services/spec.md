# Feature Specification: Agenda e Catálogo de Serviços

**Feature Branch**: `011-agenda-services`
**Created**: 2026-05-12
**Status**: Draft
**Input**: User description: "Spec 11 — Agenda e Catálogo de Serviços: módulo dual que cobre o catálogo de serviços (procedimentos, consultas, exames, avaliações com nome, duração e preço, vinculados aos profissionais que os executam) e a agenda (disponibilidade semanal dos profissionais, bloqueios e agendamentos de clientes — 1 profissional × 1 cliente × 1 serviço por agendamento na V1). A IA consulta disponibilidade e cria agendamentos via tool calls. Clientes cancelam respondendo 'NÃO' ao lembrete WhatsApp. Lembretes automáticos 24h antes são entregues pela Spec 010 (Notifications)."

---

## User Scenarios & Testing *(mandatory)*

> Atores principais: **tenant_admin** (configura catálogo, profissionais, disponibilidade e política de cancelamento), **Atendente** (`tenant_attendant`, gerencia agendamentos do seu departamento, confirma `pending_confirmation`, marca no-show), **Cliente** (paciente/contato que recebe lembretes WhatsApp e pode cancelar respondendo "NÃO"), **Agente de IA** (Spec 006, consulta disponibilidade e cria agendamentos pelas tool calls `check_availability` e `create_appointment`), **Sistema** (calcula `end_at`, determina `client_type`, dispara `appointment_confirmation`, processa cancelamentos via webhook WhatsApp, gera notificações in-app via Spec 010).

### User Story 1 — Tenant configura catálogo de serviços (Priority: P1) 🎯 MVP

Um `tenant_admin` da clínica acessa **CRM → Configurações → Serviços** e cadastra os serviços oferecidos (consultas, procedimentos, exames, avaliações). Cada serviço tem nome, descrição opcional, categoria livre, duração em minutos (define o tamanho do slot na agenda), preço opcional (`null` = a combinar) e flag `requires_confirmation` para forçar confirmação manual mesmo de clientes de retorno. Serviços podem ser ativados/desativados (soft delete) — desativar um serviço apenas o esconde de novos agendamentos; os agendamentos existentes são preservados.

**Why this priority**: Sem catálogo nenhum agendamento pode ser criado — o serviço define a duração do slot (`end_at = start_at + duration_minutes`). Toda a cadeia (profissional → serviços vinculados → IA sugere → agendamento) parte daqui. É o gate de configuração inicial.

**Independent Test**: Logado como `tenant_admin`, criar 3 serviços ("Consulta de Avaliação" 45min R$ 200, "Sessão de Fisioterapia" 60min sem preço, "Exame X" 30min com `requires_confirmation = true`). Editar um deles. Desativar o segundo. Verificar: (a) listagem mostra ativos/inativos com filtro; (b) serviço inativo não aparece para novos agendamentos; (c) duration_minutes é obrigatório > 0; (d) preço pode ficar vazio.

**Acceptance Scenarios**:

1. **Given** `tenant_admin` autenticado, **When** acessa Configurações → Serviços e clica em "Novo serviço", **Then** vê formulário com campos: nome (obrigatório, 1–100 chars), descrição (opcional, texto livre), categoria (opcional, 1–100 chars), duração em minutos (obrigatório, inteiro > 0), preço (opcional, decimal com 2 casas), `requires_confirmation` (toggle, default `false`).
2. **Given** formulário preenchido corretamente, **When** clica "Salvar", **Then** o serviço é criado com `is_active = true`, aparece imediatamente na listagem ativa e fica disponível para vinculação aos profissionais.
3. **Given** serviço existente, **When** `tenant_admin` clica "Desativar", **Then** `is_active` vira `false`, o serviço some da listagem de "Ativos" mas permanece na listagem de "Inativos"; agendamentos passados e futuros com este serviço continuam acessíveis e exibíveis no CRM.
4. **Given** serviço ativo com agendamentos confirmados no futuro, **When** desativado, **Then** os agendamentos não são alterados (status, datas, vínculos preservados) — soft delete.
5. **Given** `tenant_attendant` (sem permissão de admin), **When** tenta acessar Configurações → Serviços, **Then** o acesso é negado (403 ou redirecionamento).
6. **Given** duração inválida (0, negativo ou texto), **When** tenta salvar, **Then** validação rejeita com mensagem de erro semântica (`SERVICE_DURATION_INVALID`).

---

### User Story 2 — Tenant configura profissionais com serviços e disponibilidade (Priority: P1) 🎯 MVP

Um `tenant_admin` acessa **CRM → Configurações → Profissionais** e cadastra os profissionais que executam atendimentos (médicos, fisioterapeutas, dentistas, esteticistas, etc.). Cada profissional tem nome, especialidade opcional, departamento de referência opcional e — crítico — pode opcionalmente ser vinculado a um atendente do CRM (caso o próprio profissional use o sistema). Profissionais sem vínculo com atendente também são válidos (V1 não exige login do profissional). Em seguida, o admin associa quais serviços do catálogo cada profissional executa (`professional_services`) e configura a disponibilidade semanal (turnos por dia da semana — pode ser múltiplos turnos por dia) e os bloqueios pontuais futuros (férias, congressos, ausências).

**Why this priority**: Profissional + serviços vinculados + disponibilidade são os pré-requisitos para qualquer agendamento. Sem eles, a IA não pode chamar `check_availability`, e o atendente não pode criar agendamentos manualmente. Forma o MVP junto com US1 (catálogo) e US3 (criação manual).

**Independent Test**: Logado como `tenant_admin`, criar a profissional "Dra. Ana Lima" (especialidade "Fisioterapeuta", sem vínculo com atendente), associá-la aos serviços "Sessão de Fisioterapia" e "Consulta de Avaliação", definir disponibilidade Seg-Sex 08:00–12:00 e 14:00–18:00, criar um bloqueio "Férias 10/06 a 17/06". Verificar: (a) profissional aparece na listagem; (b) seleção de serviços é persistida e exibida; (c) grade de disponibilidade exibe os 10 turnos (5 dias × 2 turnos); (d) bloqueio aparece na listagem de bloqueios futuros e remove o intervalo da disponibilidade.

**Acceptance Scenarios**:

1. **Given** `tenant_admin` autenticado, **When** cadastra um profissional preenchendo apenas nome (campos opcionais em branco), **Then** o profissional é criado com `is_active = true`, sem `specialty`, sem `department_id` e sem `attendant_id` — vínculo com atendente é opcional na V1 (P1).
2. **Given** profissional criado, **When** `tenant_admin` opcionalmente vincula a um atendente existente do mesmo tenant, **Then** `attendant_id` é preenchido e o atendente passa a ver os agendamentos do profissional na agenda pessoal (quando aplicável).
3. **Given** profissional existente, **When** `tenant_admin` acessa "Serviços do profissional" e seleciona/desseleciona itens do catálogo, **Then** as entradas em `professional_services` são atualizadas atomicamente (delete+insert ou diff) e o profissional só pode ser agendado para esses serviços.
4. **Given** profissional vinculado ao serviço "Consulta de Avaliação", **When** o serviço é desativado pelo admin, **Then** o vínculo `professional_services` permanece mas o profissional não aparece para novos agendamentos desse serviço (consulta de disponibilidade filtra serviço inativo).
5. **Given** profissional sem nenhum serviço vinculado, **When** a IA consulta disponibilidade para qualquer serviço, **Then** este profissional nunca aparece nos resultados.
6. **Given** profissional ativo, **When** `tenant_admin` configura disponibilidade semanal preenchendo Segunda 08:00–12:00 e 14:00–18:00 (dois turnos), **Then** ambas as entradas são persistidas em `weekly_schedules` e exibidas como dois blocos distintos na grade.
7. **Given** profissional com disponibilidade configurada, **When** `tenant_admin` cria um bloqueio para "10/06 09:00 a 17/06 18:00" com motivo "Férias", **Then** o bloqueio é persistido em `schedule_blocks`, aparece na listagem de bloqueios futuros e o intervalo é subtraído da disponibilidade efetiva.
8. **Given** profissional ativo é desativado, **When** o status muda, **Then** ele some de `check_availability` e de qualquer listagem para novos agendamentos; agendamentos futuros já confirmados permanecem visíveis e gerenciáveis no CRM.
9. **Given** `start_time >= end_time` em um turno semanal, **When** o admin tenta salvar, **Then** a validação rejeita com erro semântico (`WEEKLY_SCHEDULE_INVALID_RANGE`).
10. **Given** dois turnos no mesmo dia que se sobrepõem (ex.: 08:00–12:00 e 11:00–14:00), **When** o admin tenta salvar, **Then** a validação rejeita com `WEEKLY_SCHEDULE_OVERLAP`.

---

### User Story 3 — Atendente cria e gerencia agendamentos manualmente no CRM (Priority: P1) 🎯 MVP

Um atendente acessa **CRM → Agenda**, vê a grade semanal por profissional, e clica em um slot vazio (ou em um botão "Novo Agendamento") para abrir o formulário: escolhe profissional, escolhe serviço (filtrado pelos serviços vinculados ao profissional), escolhe data/horário (sistema valida disponibilidade), informa contato (busca ou cria novo). Sistema calcula `end_at = start_at + service.duration_minutes`, determina `client_type` pelo histórico (`new_client` ou `returning_client`), aplica a regra de status (cliente novo ou serviço `requires_confirmation = true` → `pending_confirmation`; senão → `confirmed`). Cria o agendamento com `created_by = attendant`. Ao virar `confirmed`, dispara `appointment_confirmation` WhatsApp automaticamente. O atendente também consegue: confirmar agendamentos `pending_confirmation` (aba "Pendentes"), editar dados, cancelar com motivo, marcar `no_show`, reenviar lembrete manualmente. A grade semanal e a lista cronológica suportam filtros (profissional, serviço, status, período).

**Why this priority**: É a interface humana central — todo o ciclo de vida do agendamento (criar → confirmar → lembrar → cancelar/no-show) precisa funcionar via CRM antes de a IA assumir parte do trabalho. Junto com US1 e US2 forma o MVP funcional para a clínica operar sem IA.

**Independent Test**: Atendente autenticado, com US1 e US2 já configurados (catálogo + profissional + disponibilidade), abre a agenda, cria manualmente: (a) agendamento para cliente novo "João Silva" → status vira `pending_confirmation`, aparece na aba "Pendentes"; (b) confirma o `pending_confirmation` → status vira `confirmed`, `appointment_confirmation` WhatsApp é disparado; (c) cria outro agendamento para o mesmo "João Silva" → agora classificado como `returning_client` → status `confirmed` direto; (d) cancela um agendamento informando motivo; (e) marca outro como `no_show`. Verificar `end_at` calculado corretamente em todos.

**Acceptance Scenarios**:

1. **Given** atendente acessa Agenda, **When** clica em um slot vazio da grade semanal de um profissional, **Then** abre o formulário de novo agendamento pré-preenchido com profissional e horário.
2. **Given** formulário aberto, **When** atendente seleciona profissional, **Then** o seletor de serviços lista apenas os serviços vinculados em `professional_services` (e ativos).
3. **Given** atendente preenche o formulário com cliente novo (contato sem agendamentos prévios `confirmed` ou `no_show`), **When** salva, **Then** o sistema cria o agendamento com `client_type = new_client`, `status = pending_confirmation`, `created_by = attendant`, `end_at = start_at + service.duration_minutes`.
4. **Given** atendente preenche o formulário com cliente que já teve agendamentos `confirmed` ou `no_show`, **When** salva, **Then** o sistema cria com `client_type = returning_client`, `status = confirmed` (a menos que o serviço tenha `requires_confirmation = true`), `created_by = attendant`, e dispara `appointment_confirmation` WhatsApp imediatamente.
5. **Given** serviço com `requires_confirmation = true`, **When** atendente cria agendamento para cliente de retorno, **Then** o status ainda assim vai para `pending_confirmation` (a flag do serviço sobrescreve a regra de cliente).
6. **Given** agendamento em `pending_confirmation` na aba "Pendentes", **When** atendente clica "Confirmar", **Then** status vira `confirmed`, `appointment_confirmation` WhatsApp é disparado, e o agendamento sai da aba "Pendentes".
7. **Given** agendamento `confirmed`, **When** atendente clica "Cancelar" e informa motivo opcional, **Then** status vira `cancelled`, `cancelled_by = attendant`, `cancelled_at = now()`, `cancellation_reason` preenchido se fornecido.
8. **Given** agendamento `confirmed` cujo `start_at` já passou e o cliente não compareceu, **When** atendente clica "Marcar não compareceu", **Then** status vira `no_show`; este agendamento conta como histórico válido para futuras classificações de `client_type` (igual a `confirmed`).
9. **Given** agendamento `confirmed`, **When** atendente clica "Reenviar lembrete", **Then** o sistema dispara novamente o template `appointment_reminder` via Spec 010 e atualiza `reminder_sent_at = now()` (zerando a janela de 26h).
10. **Given** dois atendentes tentam criar agendamentos para o mesmo profissional no mesmo horário simultaneamente, **When** ambos clicam "Salvar", **Then** apenas um dos requests cria com sucesso; o outro recebe erro semântico `APPOINTMENT_SLOT_CONFLICT` (validação atômica protegida contra race condition).
11. **Given** atendente tenta criar agendamento fora da disponibilidade semanal do profissional ou dentro de um bloqueio, **When** salva, **Then** erro `APPOINTMENT_OUTSIDE_AVAILABILITY` é retornado.
12. **Given** atendente abre uma aba "Pendentes", **When** a aba é renderizada, **Then** todos os agendamentos com `status = pending_confirmation` do tenant aparecem listados, ordenados por `start_at` crescente, com botões "Confirmar", "Editar" e "Cancelar" para cada item.
13. **Given** grade semanal de um profissional, **When** renderizada, **Then** cada slot é colorido por status (`pending_confirmation`, `confirmed`, `cancelled`, `no_show`) e o card exibe cliente (com badge "Novo" ou "Retorno"), serviço + preço, horário e status; clique abre o detalhe.

---

### User Story 4 — IA consulta disponibilidade e cria agendamento via chat (Priority: P2)

Um cliente conversa pelo Live Chat ou WhatsApp e pede para marcar consulta. A IA pergunta serviço e/ou profissional, consulta a tool `check_availability` (filtrando pelos profissionais que executam o serviço solicitado, pelos turnos do `weekly_schedule`, subtraindo `schedule_blocks` e slots já ocupados por agendamentos `confirmed` ou `pending_confirmation`), oferece horários disponíveis, recebe a escolha do cliente, e chama `create_appointment` com `created_by = ai`. O status segue a mesma regra do fluxo manual (cliente novo ou `requires_confirmation = true` → `pending_confirmation`; senão → `confirmed`). Cliente de retorno em serviço sem `requires_confirmation` recebe a confirmação WhatsApp imediatamente; cliente novo (ou serviço que exige) fica em `pending_confirmation` aguardando confirmação manual do atendente.

**Why this priority**: É o diferencial competitivo do produto — a IA agendando sozinha reduz drasticamente o tempo de resposta e libera o atendente. Mas depende de US1 (catálogo), US2 (profissionais) e US3 (a UI para o atendente confirmar pendências) já operacionais.

**Independent Test**: Com tenant configurado (US1+US2+US3), iniciar uma conversa via Live Chat solicitando "Quero marcar uma Sessão de Fisioterapia com a Dra. Ana para sexta-feira". A IA chama `check_availability(professional_id=<ana>, service_id=<fisio>, date=<sexta>)`, recebe pelo menos 1 slot livre, propõe ao cliente. Cliente escolhe "09:00". IA chama `create_appointment(...)`. Verificar: (a) agendamento criado em `pending_confirmation` (cliente novo) ou `confirmed` (cliente de retorno); (b) `created_by = ai`; (c) `conversation_id` e `ticket_id` (se houver) preenchidos; (d) `client_type` classificado corretamente.

**Acceptance Scenarios**:

1. **Given** conversa ativa onde cliente solicita um serviço específico, **When** a IA chama `check_availability(professional_id, service_id, date)`, **Then** a resposta inclui slots livres calculados como: `weekly_schedule` do profissional naquele dia da semana, subtraindo `schedule_blocks` que se sobreponham, subtraindo slots já ocupados por agendamentos `confirmed` ou `pending_confirmation`; cada slot tem `start_at` e `end_at = start_at + service.duration_minutes`.
2. **Given** o profissional consultado não oferece o serviço (não está em `professional_services`), **When** a IA chama `check_availability`, **Then** a resposta retorna lista vazia ou erro semântico `PROFESSIONAL_DOES_NOT_OFFER_SERVICE`.
3. **Given** o profissional consultado está inativo (`is_active = false`) ou o serviço está inativo, **When** a IA chama `check_availability`, **Then** a resposta retorna lista vazia.
4. **Given** slots disponíveis retornados, **When** a IA chama `create_appointment(professional_id, service_id, start_at, client_name, client_phone, client_type)`, **Then** o sistema cria o agendamento com `created_by = ai`, vinculando `conversation_id` e `contact_id` (criando contato se phone não existe); status seguindo as regras de cliente novo/retorno e `requires_confirmation`.
5. **Given** dois clientes pedem o mesmo slot quase ao mesmo tempo via chat, **When** ambas as tool calls de `create_appointment` chegam ao backend, **Then** apenas uma cria com sucesso; a segunda recebe `APPOINTMENT_SLOT_CONFLICT` e a IA é instruída a oferecer outros horários.
6. **Given** cliente de retorno (já tem agendamento `confirmed` ou `no_show` no histórico do tenant pelo mesmo telefone) e serviço sem `requires_confirmation`, **When** a IA cria, **Then** status vai direto para `confirmed` e `appointment_confirmation` WhatsApp é disparado imediatamente.
7. **Given** cliente novo (nenhum agendamento prévio) ou serviço com `requires_confirmation = true`, **When** a IA cria, **Then** status fica `pending_confirmation` e o agendamento aparece na aba "Pendentes" do CRM para confirmação manual do atendente.
8. **Given** `create_appointment` chamado com `start_at` fora da disponibilidade semanal ou dentro de bloqueio, **When** o backend valida, **Then** rejeita com `APPOINTMENT_OUTSIDE_AVAILABILITY` e a IA é instruída a re-consultar disponibilidade.
9. **Given** `client_type` informado pela IA diverge do histórico real do contato, **When** o backend processa, **Then** o backend usa o histórico real (autoritativo) para determinar o status final, ignorando o `client_type` da chamada se conflitante — protege contra alucinação da IA.

---

### User Story 5 — Cliente cancela agendamento respondendo "NÃO" no WhatsApp (Priority: P2)

24h antes do agendamento, o cliente recebe o lembrete WhatsApp (entregue pela Spec 010 — `AppointmentReminderJob`). O cliente pode cancelar simplesmente respondendo "NÃO" na mesma conversa WhatsApp. O sistema: (1) detecta a resposta textual "NÃO" no webhook WhatsApp; (2) verifica se a conversa tem um agendamento `confirmed` com `reminder_sent_at` preenchido nas últimas 26h (janela de resposta ao lembrete); (3) se encontra, cancela automaticamente (`status = cancelled`, `cancelled_by = client`, `cancelled_at = now()`); (4) responde ao cliente com confirmação do cancelamento + texto de política de cancelamento configurado pelo tenant; (5) se o cancelamento ocorre dentro da janela de cancelamento tardio (configurável, default 24h antes do `start_at`), inclui texto adicional de aviso sobre taxa/multa (configurável pelo tenant — sem cobrança automática na V1); (6) gera notificação in-app via Spec 010 para o atendente responsável.

**Why this priority**: Reduz no-shows e libera o slot para outro paciente. Mas depende de US4 (agendamento existir) e Spec 010 estar entregando o lembrete WhatsApp — por isso P2, abaixo do core scheduling.

**Independent Test**: Criar agendamento confirmado para amanhã, simular o envio do lembrete (setar `reminder_sent_at = now() - 1h`), simular webhook WhatsApp com mensagem de texto "NÃO" vinda do número do contato. Verificar: (a) agendamento muda para `cancelled`, `cancelled_by = client`, `cancelled_at = now()`; (b) resposta WhatsApp enviada ao cliente contém o texto de política configurado; (c) se `start_at < now() + janela_cancelamento_tardio`, a resposta inclui o aviso de taxa/multa; (d) notificação in-app criada para o atendente responsável.

**Acceptance Scenarios**:

1. **Given** agendamento `confirmed` com `reminder_sent_at = now() - 2h`, **When** o webhook WhatsApp entrega uma mensagem de texto "NÃO" do número do contato vinculado, **Then** o sistema cancela o agendamento: `status = cancelled`, `cancelled_by = client`, `cancelled_at = now()`.
2. **Given** o mesmo cliente respondeu "NÃO" e seu agendamento foi cancelado, **When** o sistema responde via WhatsApp, **Then** a mensagem inclui o texto "Seu agendamento foi cancelado." + o texto de política de cancelamento configurado pelo tenant em **CRM → Configurações → Agenda**.
3. **Given** `start_at` está mais próximo de `now()` do que a janela de cancelamento tardio configurada (default: 24h), **When** o cancelamento via WhatsApp é processado, **Then** a resposta inclui adicionalmente o "texto de aviso de cancelamento tardio" configurado pelo tenant (texto livre — sistema não cobra nada automaticamente).
4. **Given** notificação in-app criada via Spec 010, **When** o atendente responsável (ou supervisores do departamento) está logado no CRM, **Then** vê a notificação "Cliente cancelou agendamento via WhatsApp" com link direto para o detalhe do agendamento.
5. **Given** `reminder_sent_at` está há mais de 26h ou é `null`, **When** o cliente envia "NÃO" no WhatsApp, **Then** o sistema NÃO cancela o agendamento automaticamente — a mensagem é processada como mensagem normal pela IA/atendente (fora da janela de resposta ao lembrete).
6. **Given** agendamento já está em status `cancelled` ou `no_show`, **When** o cliente envia "NÃO", **Then** o sistema não tenta cancelar novamente e processa a mensagem normalmente.
7. **Given** o texto da mensagem é uma variação aceita ("NÃO", "Não", "não", "NAO", "nao") — comparação normalizada (case-insensitive + sem acentos), **When** processado, **Then** todas essas variações disparam o fluxo de cancelamento.
8. **Given** existem 2+ agendamentos `confirmed` com `reminder_sent_at` na janela de 26h vinculados à mesma conversa, **When** o cliente envia "NÃO", **Then** o sistema cancela apenas o agendamento mais próximo no tempo (`start_at` mais cedo) e responde mencionando a data/hora do que foi cancelado.

---

### User Story 6 — Tenant configura política de cancelamento tardio (Priority: P3)

Um `tenant_admin` acessa **CRM → Configurações → Agenda** e configura: (a) janela de cancelamento tardio em horas (default 24h) — quantas horas antes do `start_at` o cancelamento começa a ser considerado "tardio"; (b) texto de aviso de cancelamento tardio (textarea de texto livre, default: "Cancelamentos com menos de 24h poderão ser cobrados."). Essa configuração afeta apenas o texto adicional incluído nas respostas WhatsApp de cancelamento — não há cobrança automática na V1.

**Why this priority**: Útil mas não bloqueante — o sistema funciona com os defaults. Pode entrar depois do core. P3.

**Independent Test**: Logado como `tenant_admin`, acessar Configurações → Agenda, alterar janela para 12h e texto para "Cancelamentos com menos de 12h sujeitos à cobrança de R$ 50.". Cancelar um agendamento via WhatsApp com `start_at - now() = 8h`. Verificar que a resposta inclui o novo texto configurado.

**Acceptance Scenarios**:

1. **Given** `tenant_admin` autenticado, **When** acessa Configurações → Agenda, **Then** vê a janela de cancelamento tardio (input numérico em horas, com default 24) e o texto de aviso (textarea com default).
2. **Given** valores alterados (janela = 12, texto = "Texto custom"), **When** clica "Salvar", **Then** a configuração é persistida no tenant.
3. **Given** configuração ativa de janela = 12h e texto custom, **When** um cliente cancela via WhatsApp com `start_at - now() = 8h`, **Then** a resposta WhatsApp inclui o texto custom (porque 8h < 12h, está dentro da janela tardia).
4. **Given** mesma configuração, **When** cliente cancela com `start_at - now() = 24h`, **Then** a resposta NÃO inclui o texto de cancelamento tardio (porque 24h > 12h, fora da janela tardia).
5. **Given** janela inválida (≤ 0 ou texto), **When** admin tenta salvar, **Then** validação rejeita com erro semântico.
6. **Given** `tenant_attendant` (sem permissão de admin), **When** tenta acessar Configurações → Agenda, **Then** o acesso é negado.

---

### Edge Cases

- **Profissional sem disponibilidade configurada**: cadastrado mas sem entradas em `weekly_schedules` → `check_availability` retorna lista vazia (não erro). UI exibe "Sem disponibilidade configurada" na grade.
- **Cliente sem telefone**: `create_appointment` exige `client_phone` (E.164). Sem telefone, o agendamento manual via CRM pode ser criado, mas não recebe `appointment_confirmation` nem `appointment_reminder` (Spec 010 trata graciosamente — fallback in-app para atendente).
- **Edição de `start_at` em agendamento existente**: ao mudar `start_at`, o sistema recalcula `end_at`, revalida disponibilidade e re-checa conflito de slot. Se a edição leva ao status `confirmed`, dispara `appointment_confirmation` novamente.
- **Mudança de serviço em agendamento existente**: ao trocar o `service_id`, recalcula `end_at`, re-checa conflito, re-checa se o profissional ainda oferece esse serviço.
- **Agendamento no passado**: criação rejeitada (`start_at` deve estar no futuro). Edição: permitida apenas para `notes` e `cancellation_reason`; status pode ser alterado para `cancelled` ou `no_show`.
- **Bloqueio sobreposto a agendamentos existentes**: ao criar um bloqueio que cobre slots já agendados, o sistema rejeita com `BLOCK_OVERLAPS_APPOINTMENTS` listando os IDs afetados — o admin deve cancelar/realocar antes.
- **Múltiplos turnos no mesmo dia**: aceita 2+ entradas em `weekly_schedules` para o mesmo `day_of_week`, desde que não se sobreponham.
- **Soft delete de serviço com agendamentos pendentes**: agendamentos existentes permanecem, mas a IA não consegue criar novos com este serviço e o seletor do CRM esconde o serviço inativo (com flag "Mostrar inativos" para acesso administrativo).
- **Profissional deletado** (não soft delete — deleção real): bloqueado se houver agendamentos vinculados em qualquer status. V1 suporta apenas desativação (`is_active = false`).
- **Webhook WhatsApp duplicado** (mesma mensagem entregue 2×): idempotência via `wa_message_id` — segunda entrega é ignorada (responsabilidade da Spec 008, mas relevante aqui pois evita dupla tentativa de cancelamento).
- **Reenvio manual de lembrete**: atualiza `reminder_sent_at` e reinicia a janela de 26h. Se o cliente já havia respondido "NÃO" fora da janela anterior, o reenvio reativa a janela.
- **Diferença de timezone**: todos os `timestamptz` armazenados em UTC; UI exibe no timezone do tenant (configurado em Spec 002 / 005). `check_availability` interpreta `date` (`YYYY-MM-DD`) no timezone do tenant.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Catálogo de Serviços

- **FR-001**: Sistema MUST permitir `tenant_admin` criar, editar e desativar serviços com os campos: `name` (obrigatório, 1–100 chars), `description` (opcional), `category` (opcional, 1–100 chars), `duration_minutes` (obrigatório, inteiro > 0), `price` (opcional, decimal 10,2), `requires_confirmation` (obrigatório, default `false`), `is_active` (default `true`).
- **FR-002**: Sistema MUST suportar soft delete de serviço (`is_active = false`) preservando todos os agendamentos vinculados (passados e futuros).
- **FR-003**: Sistema MUST esconder serviços inativos de novos agendamentos (CRM e tool calls da IA) mas exibí-los em listagem administrativa quando o filtro "incluir inativos" estiver ativo.
- **FR-004**: Sistema MUST rejeitar criação/edição de serviço com `duration_minutes <= 0` com erro semântico `SERVICE_DURATION_INVALID`.
- **FR-005**: Sistema MUST restringir endpoints de catálogo de serviços a usuários com role `tenant_admin`.

#### Profissionais

- **FR-006**: Sistema MUST permitir `tenant_admin` cadastrar profissional com: `name` (obrigatório), `specialty` (opcional), `department_id` (FK opcional → `departments`), `attendant_id` (FK opcional → `attendants`), `is_active` (default `true`).
- **FR-007**: Sistema MUST permitir profissional sem vínculo com atendente do CRM (V1 não exige login do profissional).
- **FR-008**: Sistema MUST permitir vincular um único atendente por profissional (relação 1-1 opcional). Sistema MUST rejeitar vínculo a atendente de outro tenant.
- **FR-009**: Sistema MUST permitir desativar (`is_active = false`) profissional. Profissional desativado NÃO deve aparecer em `check_availability` nem em seletores de novos agendamentos; agendamentos existentes permanecem visíveis e gerenciáveis.
- **FR-010**: Sistema MUST gerenciar a tabela de vínculo `professional_services` permitindo associar/desassociar múltiplos serviços por profissional via endpoint `PUT /api/professionals/{id}/services`.
- **FR-011**: Sistema MUST restringir endpoints de criação/edição de profissionais e vínculos a usuários com role `tenant_admin`.

#### Disponibilidade Semanal e Bloqueios

- **FR-012**: Sistema MUST permitir definir 0+ turnos por dia da semana por profissional via `weekly_schedules` (`day_of_week` 0–6, `start_time`, `end_time`).
- **FR-013**: Sistema MUST rejeitar turno com `start_time >= end_time` (erro `WEEKLY_SCHEDULE_INVALID_RANGE`) e dois turnos do mesmo dia que se sobreponham (erro `WEEKLY_SCHEDULE_OVERLAP`).
- **FR-014**: Sistema MUST permitir `tenant_admin` criar, listar e remover bloqueios em `schedule_blocks` (`start_at`, `end_at`, `reason` opcional).
- **FR-015**: Sistema MUST rejeitar criação de bloqueio que se sobreponha a agendamentos existentes (status `confirmed` ou `pending_confirmation`) com erro `BLOCK_OVERLAPS_APPOINTMENTS` listando os IDs afetados.

#### Cálculo de Disponibilidade (Tool Call `check_availability`)

- **FR-016**: Sistema MUST expor endpoint `GET /api/availability?professional_id=&service_id=&date=` que retorna slots livres calculados como: turnos de `weekly_schedules` do profissional naquele `day_of_week` (interpretando `date` no timezone do tenant), subtraindo intervalos cobertos por `schedule_blocks` cujo `[start_at, end_at]` intersecta o dia, subtraindo intervalos ocupados por agendamentos do mesmo profissional em status `confirmed` ou `pending_confirmation`. Cada slot tem `start_at` (timestamptz) e `end_at = start_at + service.duration_minutes`.
- **FR-017**: Sistema MUST retornar lista vazia em `check_availability` se: profissional ou serviço estão inativos, profissional não oferece o serviço (não está em `professional_services`), ou não há slots livres no dia.
- **FR-018**: Sistema MUST disponibilizar a mesma lógica de cálculo como tool call OpenAI `check_availability(professional_id, service_id, date)` (Spec 006), com a resposta no formato `[ { start_at, end_at }, ... ]`.

#### Agendamentos — Criação e Validação

- **FR-019**: Sistema MUST calcular `end_at = start_at + service.duration_minutes` automaticamente em toda criação e edição; `end_at` NÃO é aceito como input no payload.
- **FR-020**: Sistema MUST determinar `client_type` na criação: se o `contact_id` tem ≥ 1 agendamento prévio em status `confirmed` ou `no_show` → `returning_client`; senão → `new_client`. Cliente determinado pelo backend (não confiado da IA).
- **FR-021**: Sistema MUST determinar `status` inicial: se `client_type = new_client` OU `service.requires_confirmation = true` → `pending_confirmation`; senão → `confirmed`.
- **FR-022**: Sistema MUST registrar `created_by` (`ai` para tool call, `attendant` para criação via CRM) e — quando aplicável — `conversation_id`, `ticket_id` (origem) e `contact_id`.
- **FR-023**: Sistema MUST garantir atomicidade na verificação de conflito de slot — duas requisições simultâneas para o mesmo `professional_id + start_at` resultam em exatamente uma criação com sucesso; a outra retorna erro semântico `APPOINTMENT_SLOT_CONFLICT`.
- **FR-024**: Sistema MUST rejeitar criação com `start_at` no passado, fora da disponibilidade semanal do profissional ou dentro de um bloqueio com erro `APPOINTMENT_OUTSIDE_AVAILABILITY`.
- **FR-025**: Sistema MUST rejeitar criação se o profissional não oferece o serviço (não está em `professional_services`) com erro `PROFESSIONAL_DOES_NOT_OFFER_SERVICE`.

#### Agendamentos — Ciclo de Vida

- **FR-026**: Sistema MUST permitir ao atendente confirmar agendamento `pending_confirmation` via `PATCH /api/appointments/{id}/confirm`. Transição: `pending_confirmation → confirmed`.
- **FR-027**: Ao status virar `confirmed` (criação direta ou confirmação manual), sistema MUST disparar template WhatsApp `appointment_confirmation` via Spec 010 (`INotificationService`), se o contato tem telefone vinculado.
- **FR-028**: Sistema MUST permitir ao atendente cancelar agendamento via `PATCH /api/appointments/{id}/cancel` com `cancellation_reason` opcional. Transições permitidas: `pending_confirmation → cancelled`, `confirmed → cancelled`. Sistema persiste `cancelled_by = attendant`, `cancelled_at = now()`.
- **FR-029**: Sistema MUST permitir ao atendente marcar `no_show` via `PATCH /api/appointments/{id}/no-show` somente para agendamentos `confirmed` cujo `start_at` já passou.
- **FR-030**: Sistema MUST permitir ao atendente reenviar lembrete via `POST /api/appointments/{id}/resend-reminder`, atualizando `reminder_sent_at = now()` e re-disparando o template WhatsApp.
- **FR-031**: Sistema MUST proibir transições de status inválidas (ex.: `cancelled → confirmed`, `no_show → cancelled`) com erro semântico `APPOINTMENT_INVALID_STATUS_TRANSITION`.

#### Cancelamento via WhatsApp

- **FR-032**: Sistema MUST detectar resposta textual ao lembrete WhatsApp interpretando como cancelamento quando o texto, após normalização (lowercase + remoção de acentos), é igual a `"nao"`.
- **FR-033**: Sistema MUST processar a intenção de cancelamento apenas se houver, vinculado à conversa de origem, um agendamento com `status = confirmed` AND `reminder_sent_at` preenchido AND `reminder_sent_at >= now() - 26h`. Se múltiplos agendamentos qualificam, MUST cancelar apenas o de `start_at` mais cedo.
- **FR-034**: Sistema MUST setar `status = cancelled`, `cancelled_by = client`, `cancelled_at = now()` no agendamento qualificado.
- **FR-035**: Sistema MUST enviar mensagem de confirmação ao cliente via WhatsApp imediatamente após o cancelamento, com conteúdo: prefixo padrão "Seu agendamento foi cancelado." + texto de política de cancelamento configurado pelo tenant (campo `cancellation_policy_text` em `agenda_settings`) + (se aplicável) texto de cancelamento tardio.
- **FR-036**: Sistema MUST avaliar "cancelamento tardio" como `(start_at - now()) < late_cancel_window_hours` (configuração do tenant, default 24h). Se verdadeiro, inclui o texto de cancelamento tardio (configurado em `agenda_settings`) na mensagem de confirmação.
- **FR-037**: Sistema MUST gerar notificação in-app via Spec 010 (`INotificationService.NotifyAsync`) para o atendente responsável pelo ticket vinculado (e/ou supervisores do departamento) com evento `appointment.cancelled_by_client` ao processar cancelamento via WhatsApp.
- **FR-038**: Sistema MUST ignorar a mensagem "NÃO" (processando-a como mensagem normal pela IA/atendente) se nenhum agendamento elegível for encontrado.
- **FR-039**: Sistema NÃO MUST cobrar nenhuma taxa automaticamente na V1 — o texto de cancelamento tardio é puramente informativo, configurado livremente pelo tenant.

#### Configurações de Agenda (Tenant)

- **FR-040**: Sistema MUST permitir `tenant_admin` configurar via `PUT /api/agenda-settings`: `late_cancel_window_hours` (inteiro > 0, default 24), `late_cancel_text` (texto livre, default "Cancelamentos com menos de 24h poderão ser cobrados."), `cancellation_policy_text` (texto livre, default vazio).
- **FR-041**: Sistema MUST restringir endpoints de `agenda-settings` à role `tenant_admin`.

#### Interface CRM (Visualização e Gestão)

- **FR-042**: CRM MUST oferecer view "Grade semanal" exibindo agendamentos do profissional selecionado em formato calendário, com slots coloridos por status (`pending_confirmation`, `confirmed`, `cancelled`, `no_show`).
- **FR-043**: CRM MUST oferecer view "Lista" exibindo agendamentos em ordem cronológica com filtros: profissional, serviço, status, período (data início, data fim).
- **FR-044**: CRM MUST oferecer aba "Pendentes" exibindo todos os agendamentos com `status = pending_confirmation` ordenados por `start_at` crescente, com ações inline: Confirmar, Editar, Cancelar.
- **FR-045**: Card de agendamento (em qualquer view) MUST exibir: nome do cliente, badge "Novo" / "Retorno" (refletindo `client_type`), nome do serviço + preço (ou "A combinar" se `price = null`), horário (`start_at` formatado no timezone do tenant), status (com cor), nome do profissional.
- **FR-046**: Tela de detalhe MUST exibir todos os campos do agendamento (editáveis quando aplicável), histórico de ações (created/confirmed/cancelled/no-show com timestamps e autor), link para o ticket de origem (se houver), link para a conversa de origem (se houver), link para o perfil do contato; e ações: Confirmar / Cancelar / No-show / Reenviar lembrete / Editar.
- **FR-047**: CRM MUST restringir a visibilidade dos agendamentos por role: `tenant_admin` vê todos; `tenant_attendant` vê apenas agendamentos cujo ticket vinculado pertença a seu(s) departamento(s) (mesma regra de Spec 009) ou cujo profissional esteja vinculado a si (`attendant_id` no profissional).

#### Endpoints da API

- **FR-048**: API MUST expor todos os endpoints listados na seção 7 do documento de entrada com response envelope padrão (Spec 001) e paginação `?page=&per_page=&sort=&order=` quando aplicável.
- **FR-049**: API MUST exigir autenticação JWT em todos os endpoints e checar `tenant_slug` do token contra o subdomínio (multi-tenant — Spec 003).

### Key Entities

- **Serviço (`services`)**: representa um item do catálogo (consulta, procedimento, exame, avaliação). Atributos: `id`, `name`, `description`, `category`, `duration_minutes` (define tamanho do slot), `price` (opcional), `requires_confirmation` (sobrescreve regra de cliente de retorno), `is_active`. Soft delete preserva agendamentos.
- **Profissional (`professionals`)**: representa quem executa o atendimento (médico, fisioterapeuta, etc.). Atributos: `id`, `name`, `specialty`, `department_id` (FK opcional → `departments` da Spec 005), `attendant_id` (FK opcional → `attendants` — vínculo só se também usa o CRM), `is_active`. Profissional inativo não aparece em novos agendamentos.
- **Vínculo Profissional × Serviço (`professional_services`)**: tabela de junção. Cada profissional só aparece para agendamento dos serviços listados aqui.
- **Disponibilidade Semanal (`weekly_schedules`)**: turnos recorrentes por profissional. Atributos: `professional_id`, `day_of_week` (0–6), `start_time`, `end_time`. Múltiplas entradas por dia permitidas (turnos), sem sobreposição.
- **Bloqueio (`schedule_blocks`)**: intervalos específicos de indisponibilidade (férias, congressos). Atributos: `professional_id`, `start_at`, `end_at`, `reason` opcional. Subtraído da disponibilidade efetiva.
- **Agendamento (`appointments`)**: o registro central. Atributos relacionais: `professional_id`, `service_id`, `contact_id` (FK → `contacts` da Spec 009), `ticket_id` (FK opcional → `tickets`), `conversation_id` (FK opcional → `conversations` das Specs 007/008). Atributos temporais: `start_at`, `end_at` (calculado), `reminder_sent_at`, `cancelled_at`. Atributos de estado: `status` (`pending_confirmation` / `confirmed` / `cancelled` / `no_show`), `client_type` (`new_client` / `returning_client`), `created_by` (`ai` / `attendant`), `cancelled_by` (`client` / `attendant` / `system`), `cancellation_reason`, `notes`.
- **Configurações de Agenda (`agenda_settings`)**: configuração por tenant. Atributos: `late_cancel_window_hours` (default 24), `late_cancel_text`, `cancellation_policy_text`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `tenant_admin` consegue cadastrar um profissional completo (dados, serviços vinculados e disponibilidade semanal de 5 dias × 2 turnos) em menos de 5 minutos sem ajuda externa.
- **SC-002**: Atendente cria um agendamento manual via CRM (selecionando profissional, serviço, slot, contato existente) em menos de 90 segundos.
- **SC-003**: IA consegue, em uma conversa típica de agendamento, consultar disponibilidade e criar o agendamento em menos de 30 segundos do momento em que o cliente confirma a intenção ("Quero marcar X").
- **SC-004**: 100% dos slots oferecidos por `check_availability` estão de fato livres no momento da resposta — zero "falsos positivos" de slots já ocupados.
- **SC-005**: 100% das tentativas concorrentes de agendar o mesmo slot para o mesmo profissional resultam em exatamente um sucesso e um `APPOINTMENT_SLOT_CONFLICT` — zero overbookings.
- **SC-006**: 100% dos cancelamentos via WhatsApp ("NÃO" dentro da janela de 26h) são processados em menos de 5 segundos (do recebimento do webhook até a resposta de confirmação ao cliente).
- **SC-007**: Soft delete de serviço preserva 100% dos agendamentos vinculados sem alteração de status, datas ou vínculos.
- **SC-008**: Tempo de resposta do endpoint `GET /api/availability` é < 500ms no percentil 95 para tenants com até 50 profissionais e até 1.000 agendamentos futuros.
- **SC-009**: Texto de política de cancelamento configurado pelo tenant aparece em 100% das respostas de cancelamento WhatsApp; texto de cancelamento tardio aparece em 100% dos cancelamentos dentro da janela configurada e em 0% dos cancelamentos fora dela.
- **SC-010**: Em fluxo de cliente novo, 100% dos agendamentos criados (manual ou via IA) entram em `pending_confirmation` e ficam visíveis na aba "Pendentes" do CRM em menos de 2 segundos.
- **SC-011**: 95% dos atendentes (em pesquisa pós-implantação) conseguem identificar visualmente, sem treinamento, o status de um agendamento na grade semanal (cores distintas e claras para os 4 status).
- **SC-012**: Redução de no-shows medida em ≥ 25% no primeiro mês de uso comparado ao baseline pré-implantação (combinando lembrete 24h + facilidade de cancelamento via "NÃO").

## Assumptions

- **Variações de "NÃO" no WhatsApp**: o sistema aceita variações com normalização (lowercase + remoção de acentos): `"NÃO"`, `"Não"`, `"não"`, `"NAO"`, `"nao"`. Não aceita variações mais amplas como `"n"`, `"cancelar"`, `"desmarcar"` na V1 — pode evoluir em iterações futuras se houver demanda.
- **Múltiplos agendamentos elegíveis para cancelamento via "NÃO"**: quando 2+ agendamentos qualificam (mesmo contato, mesma janela de 26h), o sistema cancela apenas o mais próximo no tempo (`start_at` mais cedo) e responde mencionando explicitamente data/hora do que foi cancelado.
- **`start_at` no passado**: rejeitado para criação de novo agendamento (manual ou via IA). Status pode ser alterado em agendamentos passados (ex.: marcar `no_show` após o horário).
- **Histórico de `client_type`**: a regra é avaliada por **tenant** (mesmo telefone em tenants diferentes é considerado cliente novo em cada um). Determinada pelo telefone normalizado E.164.
- **Timezone**: todos os `timestamptz` armazenados em UTC; UI exibe e tool calls interpretam datas no timezone do tenant (configurável em Spec 005 ou tenant-level).
- **Quantum mínimo de slot**: o sistema não impõe quantum (15min, 30min, etc.) — qualquer `duration_minutes > 0` é aceito. Slots são gerados em sequência a partir do início do turno (não há padding entre slots).
- **WhatsApp Business**: requer canal WhatsApp configurado e operacional (Spec 008) para `appointment_confirmation`, `appointment_reminder` e o fluxo de cancelamento via "NÃO". Sem canal ativo, agendamentos podem ser criados mas os templates não são enviados.
- **Spec 010 (Notifications)**: dependência obrigatória. `appointment_confirmation` e `appointment_reminder` são disparados via `INotificationService`. `AppointmentReminderJob` da Spec 010 popula `reminder_sent_at` ao enviar com sucesso.
- **Spec 006 (AI Agents)**: dependência obrigatória para US4. Os contratos das tool calls `check_availability` e `create_appointment` devem ser registrados no orquestrador antes de a US4 ser entregue.
- **Spec 009 (Tickets)**: dependência opcional. Quando o agendamento é criado via IA durante uma conversa que originou um ticket, `ticket_id` e `conversation_id` são preenchidos automaticamente. Sem Spec 009 operacional, agendamentos manuais funcionam (ticket é opcional).
- **Sem reagendamento direto na V1**: para "reagendar", o atendente cancela o agendamento atual e cria um novo. UI pode adicionar atalho "Reagendar" como conveniência (cancelar + criar novo pré-preenchido) em versão futura.
- **Sem `appointment_reminder` configurável**: o intervalo de 24h antes é fixo na V1 — a Spec 010 não expõe configuração de antecedência por tenant. Pode evoluir.
- **Sem capacidade simultânea por profissional**: V1 = 1 profissional × 1 cliente × 1 serviço por agendamento. Não suporta "grupos" ou "consulta com 2 profissionais" ou "salas com capacidade > 1".
- **Sem integração com sistemas externos de agenda** (Google Calendar, Outlook) na V1.
- **Sem cobrança automática** de taxa de cancelamento tardio na V1 — apenas texto informativo configurável pelo tenant.
- **Permissão para gerenciar profissionais e catálogo**: restrita a `tenant_admin`. `tenant_attendant` apenas gerencia agendamentos (criar, confirmar, cancelar, no-show, reenviar lembrete) dentro do escopo de visibilidade (FR-047).
