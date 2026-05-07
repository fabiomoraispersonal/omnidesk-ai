# Quickstart: Verificação de Provisionamento de Tenants

**Branch**: `003-tenant-provisioning` | **Data**: 2026-05-06

> Cenários de verificação para validar que a implementação satisfaz os critérios de aceite.
> Execute cada cenário após o `/speckit-implement` antes de abrir o PR.

---

## Pré-requisitos

- API rodando em `http://localhost:5000`
- PostgreSQL, Redis, MongoDB e MinIO disponíveis e configurados
- `saas_admin` seed criado no banco (ver abaixo)
- Variáveis de ambiente configuradas: `AES_ENCRYPTION_KEY`, `JWT_PRIVATE_KEY_PEM`, `MINIO_*`, `MONGODB_*`, `SENDGRID_API_KEY`

**Seed de teste — saas_admin**:
```sql
-- senha: Admin@12345 (hash Argon2id)
INSERT INTO public.users (id, email, password_hash, role, is_active, email_verified)
VALUES (
  'b0000000-0000-0000-0000-000000000001',
  'admin@omnidesk.dev',
  '<hash-gerado>',
  'saas_admin',
  true,
  true
);
```

**Token de saas_admin** (reutilizado em todos os cenários):
```bash
ADMIN_TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@omnidesk.dev","password":"Admin@12345","remember_me":false,"turnstile_token":"XXXX.DUMMY.TOKEN.XXXX"}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['access_token'])")
echo "Token: $ADMIN_TOKEN"
```

---

## Cenário 1 — Criação de Tenant e Provisionamento Completo

```bash
# 1. Criar o tenant
RESP=$(curl -s -w "\n%{http_code}" -X POST http://localhost:5000/api/admin/tenants \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "slug": "clinica-teste",
    "razao_social": "Clínica Teste Ltda",
    "nome_fantasia": "Clínica Teste",
    "cnpj": "11.222.333/0001-44",
    "timezone": "America/Sao_Paulo",
    "financial_contact": {
      "name": "Financeiro Teste",
      "email": "fin@clinicateste.com.br",
      "phone": "(11) 91111-1111"
    },
    "technical_contact": {
      "name": "Técnico Teste",
      "email": "tec@clinicateste.com.br",
      "phone": "(11) 92222-2222"
    }
  }')
HTTP_CODE=$(echo "$RESP" | tail -1)
BODY=$(echo "$RESP" | head -1)
echo "HTTP: $HTTP_CODE"
echo "Body: $BODY"
```

**Esperado**: HTTP `202`, body com `{"id":"<uuid>","slug":"clinica-teste","status":"provisioning"}`

```bash
# 2. Aguardar provisionamento (polling)
TENANT_ID=$(echo $BODY | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])")
for i in {1..30}; do
  STATUS=$(curl -s http://localhost:5000/api/admin/tenants/$TENANT_ID \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    | python3 -c "import sys,json; print(json.load(sys.stdin)['status'])")
  echo "Status: $STATUS"
  [ "$STATUS" = "active" ] && break
  sleep 5
done
```

**Esperado**: Status muda para `active` em menos de 3 minutos (SC-001)

```bash
# 3. Verificar recursos criados no Postgres
psql $DATABASE_URL -c "\dn tenant_clinica_teste"
# Deve retornar o schema

psql $DATABASE_URL -c "
  SELECT table_name FROM information_schema.tables
  WHERE table_schema = 'tenant_clinica_teste';"
# Deve retornar as tabelas criadas pelas migrations
```

```bash
# 4. Verificar bucket MinIO
mc ls minio/tenant-clinica-teste
# Deve listar o bucket (vazio)
```

```bash
# 5. Verificar database MongoDB
mongosh $MONGODB_URL --eval "db.adminCommand({listDatabases:1})" | grep tenant_clinica_teste
# Deve aparecer na lista

mongosh $MONGODB_URL/tenant_clinica_teste --eval "show collections"
# Deve mostrar __metadata
```

```bash
# 6. Verificar Super Admin criado
psql $DATABASE_URL -c "
  SELECT email, role, is_active, email_verified
  FROM public.users
  WHERE email = 'tec@clinicateste.com.br';"
# email_verified deve ser TRUE
```

---

## Cenário 2 — Validações de Slug e CNPJ

```bash
# Slug inválido (maiúsculas)
curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:5000/api/admin/tenants \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"slug":"ClinicaABC","razao_social":"X","cnpj":"00.000.000/0001-00","timezone":"America/Sao_Paulo","financial_contact":{"name":"A","email":"a@b.com","phone":"11"},"technical_contact":{"name":"B","email":"b@c.com","phone":"22"}}'
# Esperado: 400
```

```bash
# CNPJ duplicado (usar o slug anterior já criado com CNPJ 11.222.333/0001-44)
curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:5000/api/admin/tenants \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"slug":"outra-clinica","razao_social":"Outra","cnpj":"11.222.333/0001-44","timezone":"America/Sao_Paulo","financial_contact":{"name":"A","email":"a@b.com","phone":"11"},"technical_contact":{"name":"B","email":"b@c.com","phone":"22"}}'
# Esperado: 409
```

```bash
# Slug duplicado
curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:5000/api/admin/tenants \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"slug":"clinica-teste","razao_social":"Outra","cnpj":"22.333.444/0001-55","timezone":"America/Sao_Paulo","financial_contact":{"name":"A","email":"a@b.com","phone":"11"},"technical_contact":{"name":"B","email":"b@c.com","phone":"22"}}'
# Esperado: 409
```

---

## Cenário 3 — API Key OpenAI Nunca Exposta

```bash
# Criar tenant com OpenAI key
curl -s -X POST http://localhost:5000/api/admin/tenants \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "slug": "clinica-openai",
    "razao_social": "Clínica OpenAI Ltda",
    "cnpj": "33.444.555/0001-66",
    "timezone": "America/Sao_Paulo",
    "financial_contact": {"name":"A","email":"a@oai.com","phone":"11"},
    "technical_contact": {"name":"B","email":"b@oai.com","phone":"22"},
    "openai_api_key": "sk-test-key-should-never-appear"
  }'

# Aguardar provisionamento, então buscar detalhes
sleep 10
OAI_ID=$(curl -s http://localhost:5000/api/admin/tenants \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  | python3 -c "import sys,json; [print(t['id']) for t in json.load(sys.stdin) if t['slug']=='clinica-openai']")

DETAIL=$(curl -s http://localhost:5000/api/admin/tenants/$OAI_ID \
  -H "Authorization: Bearer $ADMIN_TOKEN")
echo $DETAIL | python3 -m json.tool | grep -i "openai_api_key\|sk-test"
# Esperado: NENHUMA ocorrência de "sk-test" ou "openai_api_key" com valor real
echo $DETAIL | python3 -c "import sys,json; print(json.load(sys.stdin).get('has_openai_key'))"
# Esperado: True
```

---

## Cenário 4 — Bloqueio e Desbloqueio

```bash
# Pré-requisito: ter sessão ativa do Super Admin do tenant clinica-teste
# (fazer login com as credenciais recebidas por e-mail)

# 1. Bloquear
BLOCK_RESP=$(curl -s -w "\n%{http_code}" -X POST \
  http://localhost:5000/api/admin/tenants/$TENANT_ID/block \
  -H "Authorization: Bearer $ADMIN_TOKEN")
echo "$BLOCK_RESP"
# Esperado: 200, status=blocked

# 2. Verificar que sessões foram invalidadas
# (tentar usar o token do Super Admin em qualquer rota do CRM)
TENANT_TOKEN="<token-do-super-admin>"
curl -s -o /dev/null -w "%{http_code}" \
  http://localhost:5000/api/v1/tenant-resource \
  -H "Authorization: Bearer $TENANT_TOKEN"
# Esperado: 403

# 3. Verificar sessões Redis invalidadas
redis-cli KEYS "clinica-teste:session:*"
# Esperado: (empty array)

# 4. Desbloquear
curl -s -w "\n%{http_code}" -X POST \
  http://localhost:5000/api/admin/tenants/$TENANT_ID/unblock \
  -H "Authorization: Bearer $ADMIN_TOKEN"
# Esperado: 200, status=active
```

---

## Cenário 5 — Impersonation

```bash
# 1. Solicitar impersonation
IMP_RESP=$(curl -s -X POST \
  http://localhost:5000/api/admin/tenants/$TENANT_ID/impersonate \
  -H "Authorization: Bearer $ADMIN_TOKEN")
IMP_TOKEN=$(echo $IMP_RESP | python3 -c "import sys,json; print(json.load(sys.stdin)['impersonation_token'])")
echo "Impersonation token: $IMP_TOKEN"

# 2. Decodificar e verificar claims
echo "$IMP_TOKEN" | cut -d. -f2 | base64 -d 2>/dev/null | python3 -m json.tool
# Deve conter: "impersonating": true, "role": "tenant_admin", "tenant_slug": "clinica-teste"
# exp = now + 900 (15 minutos)

# 3. Verificar que NÃO é renovável
curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:5000/api/auth/refresh \
  -H "Cookie: refresh_token=qualquer-valor"
# O impersonation token não está em cookie de refresh — não gera novo token
# (nenhum refresh token foi emitido)
```

---

## Cenário 6 — Templates de Agentes

```bash
# 1. Listar templates padrão (criados no seed inicial do sistema)
curl -s http://localhost:5000/api/admin/agent-templates \
  -H "Authorization: Bearer $ADMIN_TOKEN" | python3 -m json.tool
# Deve mostrar: Agente Principal, Recepção, Vendas, Pós-Vendas, Suporte

# 2. Verificar que foram copiados para o tenant
psql $DATABASE_URL -c "
  SELECT name, type FROM tenant_clinica_teste.agents;"
# Deve mostrar os 5 agentes

# 3. Editar um template global
curl -s -X PUT http://localhost:5000/api/admin/agent-templates/<id-recepcao> \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name": "Recepção Digital"}'

# 4. Verificar que o agente do tenant NÃO foi alterado
psql $DATABASE_URL -c "
  SELECT name FROM tenant_clinica_teste.agents WHERE type = 'sub_agent' LIMIT 1;"
# Deve continuar "Recepção" (nome original)
```

---

## Cenário 7 — Dashboard de Métricas (Cache)

```bash
# Aguardar o job de métricas rodar (até 5 min) ou disparar manualmente via Hangfire dashboard

# 1. Verificar cache Redis
redis-cli GET "saas:metrics:clinica-teste" | python3 -m json.tool
# Deve retornar JSON com connected, sizes, business metrics

# 2. Verificar que a listagem usa o cache (não queries diretas)
# Acessar http://localhost:5000/api/admin/tenants e confirmar que as métricas aparecem
curl -s http://localhost:5000/api/admin/tenants \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  | python3 -c "import sys,json; [print(t.get('metrics')) for t in json.load(sys.stdin)]"
```

---

## Checklist de Verificação Final

- [ ] Provisionamento completo (schema + MinIO + MongoDB + e-mail) em < 3 min
- [ ] Schema Postgres `tenant_clinica_teste` existe com migrations aplicadas
- [ ] Bucket MinIO `tenant-clinica-teste` existe
- [ ] Database MongoDB `tenant_clinica_teste` existe com `__metadata`
- [ ] Super Admin criado com `email_verified = true`
- [ ] Slug duplicado retorna 409
- [ ] CNPJ duplicado retorna 409
- [ ] Slug com maiúsculas retorna 400
- [ ] OpenAI key nunca aparece na resposta da API (apenas `has_openai_key: bool`)
- [ ] Bloqueio invalida todas as sessões Redis do tenant
- [ ] Tenant bloqueado retorna 403 em rotas do CRM
- [ ] Desbloqueio restaura acesso
- [ ] Impersonation token: claims corretos, exp = now + 900s
- [ ] Impersonation não gera refresh token
- [ ] Templates copiados para o tenant no provisionamento
- [ ] Edição de template global não afeta agentes do tenant
- [ ] Métricas lidas do cache Redis (`saas:metrics:{slug}`)
- [ ] Dashboard exibe `--` para tenant sem cache populado
