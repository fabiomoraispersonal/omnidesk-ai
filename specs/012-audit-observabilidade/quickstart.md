# Quickstart: Verificação Local — Auditoria e Observabilidade

**Feature**: `012-audit-observabilidade`
**Date**: 2026-05-13

---

## Pré-requisitos

- Stack local rodando (`docker-compose up -d`)
- API rodando em `http://localhost:5000`
- Token JWT de `tenant_admin` disponível (via login)

---

## QS-1: Verificar registro de evento de autenticação

```bash
# 1. Fazer login (gera auth.login_success)
curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@clinica-abc.test","password":"senha123"}' \
  | jq '.data.access_token'

# Salvar token
TOKEN="<access_token_acima>"

# 2. Verificar se o log foi criado
curl -s http://localhost:5000/api/audit-logs?event=auth.login_success \
  -H "Authorization: Bearer $TOKEN" \
  | jq '.data[0] | {event, actor, timestamp}'
```

**Resultado esperado**: objeto com `event: "auth.login_success"` e `actor.role: "tenant_admin"`.

---

## QS-2: Verificar registro de mudança de status de ticket

```bash
# Mudar status de um ticket (pegar ID de um ticket existente)
TICKET_ID="<uuid>"

curl -s -X PATCH "http://localhost:5000/api/tickets/$TICKET_ID/status" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"status":"resolved"}'

# Verificar o log
curl -s "http://localhost:5000/api/audit-logs?event=ticket.status_changed" \
  -H "Authorization: Bearer $TOKEN" \
  | jq '.data[0] | {event, target, metadata}'
```

**Resultado esperado**: `metadata.from` com status anterior e `metadata.to: "resolved"`.

---

## QS-3: Criar e usar uma API Key

```bash
# 1. Criar API Key
RESPONSE=$(curl -s -X POST http://localhost:5000/api/api-keys \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"QS Test Key"}')

echo $RESPONSE | jq '.data.key'
API_KEY=$(echo $RESPONSE | jq -r '.data.key')

# 2. Usar a API Key para acessar logs
curl -s http://localhost:5000/api/audit-logs \
  -H "X-Api-Key: $API_KEY" \
  | jq '{total: .meta.total, first_event: .data[0].event}'

# 3. Verificar que key_hash não está exposta em GET
curl -s http://localhost:5000/api/api-keys \
  -H "Authorization: Bearer $TOKEN" \
  | jq '.data[0] | keys'
# Resultado: NÃO deve conter "key" ou "key_hash"
```

**Resultado esperado**: API Key autentica com sucesso, log lista retorna dados do tenant.

---

## QS-4: Verificar limite de 5 API Keys

```bash
# Criar 5 chaves (já pode ter 1 do QS-3)
for i in 2 3 4 5; do
  curl -s -X POST http://localhost:5000/api/api-keys \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json" \
    -d "{\"name\":\"QS Key $i\"}" | jq '.success'
done

# Tentar criar a 6ª (deve falhar)
curl -s -X POST http://localhost:5000/api/api-keys \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"Key Extra"}' \
  | jq '.error.code'
# Resultado esperado: "API_KEY_LIMIT_REACHED"
```

---

## QS-5: Verificar revogação de API Key

```bash
KEY_ID=$(curl -s http://localhost:5000/api/api-keys \
  -H "Authorization: Bearer $TOKEN" \
  | jq -r '.data[0].id')

# Revogar a chave
curl -s -X DELETE "http://localhost:5000/api/api-keys/$KEY_ID" \
  -H "Authorization: Bearer $TOKEN" \
  -o /dev/null -w "%{http_code}"
# Resultado esperado: 204

# Tentar usar a chave revogada (usar API_KEY salva do QS-3 se for a mesma)
curl -s http://localhost:5000/api/audit-logs \
  -H "X-Api-Key: $API_KEY" \
  | jq '.error.code'
# Resultado esperado: "UNAUTHORIZED"
```

---

## QS-6: Verificar isolamento de tenant

```bash
# Logar como admin de outro tenant
TOKEN_OUTRO="<token_de_outro_tenant>"

# Verificar que logs do tenant-abc não aparecem para tenant-xyz
curl -s http://localhost:5000/api/audit-logs \
  -H "Authorization: Bearer $TOKEN_OUTRO" \
  | jq '.meta.total'
# Resultado: total deve refletir apenas os eventos do tenant-xyz, não do tenant-abc
```

---

## QS-7: Verificar imutabilidade dos logs

```bash
LOG_ID=$(curl -s "http://localhost:5000/api/audit-logs" \
  -H "Authorization: Bearer $TOKEN" \
  | jq -r '.data[0].id')

# Tentar UPDATE (deve retornar 405 Method Not Allowed)
curl -s -X PUT "http://localhost:5000/api/audit-logs/$LOG_ID" \
  -H "Authorization: Bearer $TOKEN" \
  -o /dev/null -w "%{http_code}"

# Tentar DELETE (deve retornar 405 Method Not Allowed)
curl -s -X DELETE "http://localhost:5000/api/audit-logs/$LOG_ID" \
  -H "Authorization: Bearer $TOKEN" \
  -o /dev/null -w "%{http_code}"
# Resultados esperados: 405 em ambos
```
