# Quickstart — Agenda e Catálogo de Serviços (Spec 011)

**Branch**: `011-agenda-services`
**Audience**: developer running the feature locally for the first time.

Cobre setup local (Postgres + Redis + Mongo via Docker Compose), aplicação de migrations, seed de dados mínimo (1 profissional + 2 serviços + disponibilidade) e smoke tests dos endpoints/WS críticos.

---

## 1. Pré-requisitos

- .NET 10 SDK
- Node 22+ (Angular 21)
- Docker (Postgres 16 + Redis 7 + Mongo 7)
- `dotnet ef` global tool (`dotnet tool install -g dotnet-ef`)
- Tenant local provisionado pela Spec 003 (default: `clinica-abc`). Se ainda não tem, ver `specs/003-tenant-provisioning/quickstart.md`.

---

## 2. Subir infra local

```bash
cd infra
docker compose up -d postgres redis mongo

# Verifica
docker compose ps
docker compose logs postgres | tail -20
```

PostgreSQL precisa do extension `btree_gist`. A migration `Add_Agenda_SchedulesAndBlocks.sql` habilita idempotentemente — sem ação manual.

---

## 3. Aplicar migrations

As 4 migrations Spec 011 rodam automaticamente no boot da API via `TenantMigrationsRunner`. Para aplicar manualmente:

```bash
cd src/omniDesk.Api

# Aplica em todos os tenants ativos
dotnet run -- migrate-tenants

# Ou aplica em um tenant específico
dotnet run -- migrate-tenant --slug clinica-abc
```

Verifica:

```bash
psql -U postgres -d omnidesk -c "\dt tenant_clinica_abc.*" | grep -E "services|professionals|weekly_schedules|schedule_blocks|appointments|agenda_settings"
```

Esperado: as 7 tabelas listadas (`services`, `professionals`, `professional_services`, `weekly_schedules`, `schedule_blocks`, `appointments`, `agenda_settings`).

---

## 4. Seed mínimo (manual via psql ou SQL helper)

Para o quickstart, criar via SQL direto. Em ambiente de teste integrado, usar `TestDataBuilder` do projeto de tests.

```sql
-- 4.1 Catálogo
INSERT INTO tenant_clinica_abc.services (id, name, category, duration_minutes, price, requires_confirmation)
VALUES
    ('11111111-1111-1111-1111-111111111111', 'Consulta de Avaliação', 'Consulta',     45, 200.00, false),
    ('22222222-2222-2222-2222-222222222222', 'Sessão de Fisioterapia', 'Procedimento', 60, 150.00, false);

-- 4.2 Profissional (sem atendente vinculado)
INSERT INTO tenant_clinica_abc.professionals (id, name, specialty)
VALUES ('aaaa1111-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'Dra. Ana Lima', 'Fisioterapeuta');

-- 4.3 Vínculos
INSERT INTO tenant_clinica_abc.professional_services (id, professional_id, service_id)
VALUES
    (gen_random_uuid(), 'aaaa1111-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '11111111-1111-1111-1111-111111111111'),
    (gen_random_uuid(), 'aaaa1111-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '22222222-2222-2222-2222-222222222222');

-- 4.4 Disponibilidade: Seg-Sex 08-12 e 14-18
INSERT INTO tenant_clinica_abc.weekly_schedules (id, professional_id, day_of_week, start_time, end_time)
SELECT gen_random_uuid(), 'aaaa1111-aaaa-aaaa-aaaa-aaaaaaaaaaaa', dow, t.start_time::time, t.end_time::time
FROM generate_series(1, 5) AS dow
CROSS JOIN (VALUES ('08:00','12:00'), ('14:00','18:00')) AS t(start_time, end_time);

-- 4.5 Agenda settings (já criada pela migration; só confirmando)
SELECT * FROM tenant_clinica_abc.agenda_settings;
```

---

## 5. Subir a API

```bash
cd src/omniDesk.Api
dotnet run
```

API em `http://localhost:5180`. Para subdomínio do tenant em local, usar `http://clinica-abc.localhost:5180` (configurar `/etc/hosts` se necessário).

JWT para os testes manuais:

```bash
# Login via Spec 002
curl -X POST http://clinica-abc.localhost:5180/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@clinica-abc.local","password":"Admin@123"}' \
  -c cookies.txt | jq -r '.data.access_token'
```

Exportar token:

```bash
export TOKEN="<colar access_token aqui>"
```

---

## 6. Smoke tests REST

### 6.1 Listar serviços

```bash
curl -s http://clinica-abc.localhost:5180/api/services \
  -H "Authorization: Bearer $TOKEN" | jq
# Esperado: 2 serviços ativos
```

### 6.2 Listar profissionais com serviços

```bash
curl -s http://clinica-abc.localhost:5180/api/professionals/aaaa1111-aaaa-aaaa-aaaa-aaaaaaaaaaaa/services \
  -H "Authorization: Bearer $TOKEN" | jq
# Esperado: 2 vínculos
```

### 6.3 Consultar disponibilidade (amanhã)

```bash
TOMORROW=$(date -v+1d +%Y-%m-%d 2>/dev/null || date -d tomorrow +%Y-%m-%d)
curl -s "http://clinica-abc.localhost:5180/api/availability?professional_id=aaaa1111-aaaa-aaaa-aaaa-aaaaaaaaaaaa&service_id=11111111-1111-1111-1111-111111111111&date=$TOMORROW" \
  -H "Authorization: Bearer $TOKEN" | jq
# Esperado: lista de slots de 45min entre 08-12 e 14-18 do dia, se for dia útil. Sábado/Domingo → []
```

### 6.4 Criar agendamento manual (cliente novo)

Primeiro, criar/buscar contato (Spec 009):

```bash
curl -s -X POST http://clinica-abc.localhost:5180/api/contacts \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"João Teste","email":"joao@test.local","phone":"+5511999998888"}' | jq -r '.data.id'
```

Exportar `CONTACT_ID`. Depois:

```bash
SLOT_START="${TOMORROW}T09:00:00-03:00"
curl -s -X POST http://clinica-abc.localhost:5180/api/appointments \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"professional_id\":\"aaaa1111-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",
    \"service_id\":\"11111111-1111-1111-1111-111111111111\",
    \"contact_id\":\"$CONTACT_ID\",
    \"start_at\":\"$SLOT_START\"
  }" | jq
# Esperado: status="pending_confirmation", client_type="new_client", created_by="attendant"
```

### 6.5 Confirmar agendamento

```bash
APPT_ID="<id do agendamento>"
curl -s -X PATCH "http://clinica-abc.localhost:5180/api/appointments/$APPT_ID/confirm" \
  -H "Authorization: Bearer $TOKEN" | jq
# Esperado: status="confirmed"
```

### 6.6 Listar aba "Pendentes" (após criar um novo cliente novo)

```bash
curl -s "http://clinica-abc.localhost:5180/api/appointments?status=pending_confirmation" \
  -H "Authorization: Bearer $TOKEN" | jq
```

### 6.7 Verificar race condition (FR-023)

```bash
# Dois POSTs simultâneos com o mesmo slot:
SLOT_START="${TOMORROW}T11:00:00-03:00"

(curl -s -X POST http://clinica-abc.localhost:5180/api/appointments \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"professional_id\":\"aaaa1111-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"service_id\":\"11111111-1111-1111-1111-111111111111\",\"contact_id\":\"$CONTACT_ID\",\"start_at\":\"$SLOT_START\"}" &)
(curl -s -X POST http://clinica-abc.localhost:5180/api/appointments \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{\"professional_id\":\"aaaa1111-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"service_id\":\"11111111-1111-1111-1111-111111111111\",\"contact_id\":\"$CONTACT_ID\",\"start_at\":\"$SLOT_START\"}" &)

wait
# Esperado: 1 success (201), 1 fail (409 APPOINTMENT_SLOT_CONFLICT)
```

### 6.8 Cancelamento via "NÃO" (simulação)

Cenário completo manual: ver `quickstart-evidences.md` (após primeira execução; arquivo gerado pelo `/speckit-implement`).

```bash
# Simular: setar reminder_sent_at no passado próximo
psql -U postgres -d omnidesk -c \
  "UPDATE tenant_clinica_abc.appointments SET reminder_sent_at = now() - interval '1 hour' WHERE id = '$APPT_ID';"

# Simular webhook WhatsApp inbound com texto "NÃO" — ver Spec 008 quickstart §6 para shape exato do payload.
# Esperado:
# - status do appointment vira "cancelled"
# - cancelled_by = "client"
# - resposta WhatsApp enfileirada em {slug}:outgoing_messages contendo texto de política
# - notificação in-app criada em tenant_clinica_abc.notifications
# - evento appointment.cancelled gravado em {slug}_appointment_events (Mongo)
```

---

## 7. Frontend CRM (Angular 21)

```bash
cd src/omniDesk.Crm
npm install
npm run start
```

CRM em `http://localhost:4200`. Navegar:

- **Configurações → Serviços** (`/configuracoes/servicos`) — listagem, criar, editar, desativar.
- **Configurações → Profissionais** (`/configuracoes/profissionais`) — listagem, criar, editar, sub-páginas (serviços / horários / bloqueios).
- **Agenda** (`/agenda`) — 3 abas: grade semanal por profissional, lista cronológica, pendentes.
- **Configurações → Agenda** (`/configuracoes/agenda`) — janela + textos de cancelamento.

Permissões esperadas:

- `tenant_admin` vê tudo.
- `tenant_attendant` vê **Agenda** (visibility filter aplicado) mas NÃO vê as 3 telas de Configurações (sidebar esconde via `*ngIf="canManageAgenda$ | async"`).

---

## 8. WebSocket smoke test

```bash
# Em um terminal:
wscat -c "ws://clinica-abc.localhost:5180/ws/crm?token=$TOKEN"

# Em outro terminal, criar/confirmar/cancelar um agendamento.
# Esperado no wscat: receber payload `appointment.changed` com o `action` correspondente.
```

---

## 9. Rodar suite de testes

### 9.1 Backend (xUnit + Testcontainers)

```bash
cd src/omniDesk.Api/tests/omniDesk.Api.Tests
dotnet test --filter "FullyQualifiedName~Features.Agenda"
# ~50 testes (ServicesEndpoint, ProfessionalsEndpoint, ProfessionalServicesEndpoint,
#  WeeklyScheduleEndpoint, ScheduleBlocksEndpoint, AvailabilityCalculator, AvailabilityEndpoint,
#  CreateAppointmentCommand, AppointmentLifecycle, AppointmentVisibilityPolicy,
#  ConcurrentAppointmentCreation, ReminderResponseInterpreter, CancelAppointmentByClient,
#  AgendaSettingsEndpoint, CheckAvailabilityTool, CreateAppointmentTool)

# Subset crítico para racing:
dotnet test --filter "FullyQualifiedName~ConcurrentAppointmentCreation"
```

### 9.2 Frontend (Karma + Jasmine)

```bash
cd src/omniDesk.Crm
npm test -- --watch=false --browsers=ChromeHeadless --include="src/app/features/{services-catalog,professionals,agenda,agenda-settings}/**/*.spec.ts"
```

---

## 10. Métricas locais

```bash
# Expor /metrics (Prometheus format)
curl -s http://localhost:5180/metrics | grep -E "appointment|availability|reminder_response"

# Esperado após smoke tests:
# appointments_created_total{tenant="clinica-abc",source="attendant",status_inicial="pending_confirmation"} 1
# appointment_cancellations_total{tenant="clinica-abc",by="client",channel="whatsapp"} 1
# availability_query_duration_seconds_bucket{tenant="clinica-abc",le="0.5"} 1
# reminder_response_no_total{tenant="clinica-abc",outcome="cancelled"} 1
# appointment_slot_conflict_total{tenant="clinica-abc",layer="redis"} 1  (do teste 6.7)
```

---

## 11. Troubleshooting

| Sintoma | Causa provável | Resolução |
|---|---|---|
| `extension btree_gist does not exist` | Postgres < 14 ou imagem sem contrib | Usar imagem oficial `postgres:16`; migration cria a extensão idempotentemente. |
| `APPOINTMENT_SLOT_CONFLICT` em todos os POSTs | Redis offline ou key não expirou | `docker compose restart redis` ou `FLUSHDB` no Redis. |
| `[Warn] AppointmentReadRepository: tenant ... has no appointments table yet` | Migration Spec 011 não aplicada | Rodar `dotnet run -- migrate-tenants`. |
| `appointment.changed` não chega no wscat | JWT expirou ou subscrição em outro canal | Re-login; verificar canal `{slug}:ws:crm:dept:{deptId}`. |
| `WaWebhookProcessorJob` ignorando "NÃO" | `reminder_sent_at` fora da janela ou status != confirmed | Verificar status e timestamp da última atualização do appointment. |
| Frontend: `Configurações → Serviços` não aparece no menu | Usuário não é `tenant_admin` | Re-login com `tenant_admin`. |

---

## 12. Próximos passos

- `/speckit-tasks` para gerar `tasks.md`.
- `/speckit-implement` para executar as tarefas.
- Após implementação, rodar `quickstart` completo + gerar `quickstart-evidences.md` (padrão Spec 009/010).
