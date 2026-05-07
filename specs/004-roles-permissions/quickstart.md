# Quickstart — Roles e Permissões

Como **adicionar uma nova ação protegida** no OmniDesk e como **verificar comportamento** dos fluxos chave desta spec.

---

## A) Adicionar uma nova ação protegida (checklist obrigatório)

Suponha que a Spec 06 vá adicionar a ação "Exportar configuração do widget em JSON". Passos:

### 1. Atualize a matriz na spec (esta spec, seção 4.x)

Edite [spec.md](spec.md), localize a tabela 4.3 (Live Chat — Widget), adicione a linha:

```markdown
| Exportar configuração do widget em JSON | ✅ | ✅ | ❌ |
```

> Sem esta linha, o PR é rejeitado em revisão. **Fonte única de verdade primeiro.**

### 2. Atualize o contract de policies

Edite [contracts/authorization-policies.md](contracts/authorization-policies.md), seção 4.3:

```markdown
| `Widget.ExportConfig` | `supervisor` | FR-014 |
```

### 3. Adicione a constante

Em `src/omniDesk.Api/Domain/Authorization/Permissions.cs`:

```csharp
public const string CanExportWidgetConfig = "Widget.ExportConfig";
```

### 4. Registre a policy

Em `src/omniDesk.Api/Features/Authorization/Policies/AuthorizationPoliciesRegistration.cs`:

```csharp
options.AddPolicy(Policies.CanExportWidgetConfig,
    p => p.AddRequirements(new RoleRequirement(Roles.Supervisor)));
// hierarquia automática: tenant_admin também recebe acesso
```

### 5. Aplique no endpoint

```csharp
group.MapGet("/widget/export", ExportWidgetConfig)
     .RequireAuthorization(Policies.CanExportWidgetConfig);
```

### 6. Adicione caso ao teste paramétrico

Em `src/omniDesk.Api/tests/omniDesk.Api.Tests/Features/Authorization/Policies/PolicyMatrixTests.cs`:

```csharp
yield return new object[] { Policies.CanExportWidgetConfig, Roles.TenantAdmin, 200, "GET", "/widget/export" };
yield return new object[] { Policies.CanExportWidgetConfig, Roles.Supervisor, 200, "GET", "/widget/export" };
yield return new object[] { Policies.CanExportWidgetConfig, Roles.Attendant,  403, "GET", "/widget/export" };
```

### 7. Frontend (CRM)

Esconda o botão na UI usando a diretiva (não precisa lógica condicional):

```html
<button *omniHasRole="['tenant_admin', 'supervisor']" (click)="export()">
  Exportar JSON
</button>
```

> A diretiva é apenas UX. A verdade é o backend (defense-in-depth).

### 8. Rode os testes

```bash
dotnet test src/omniDesk.Api/tests/omniDesk.Api.Tests --filter "FullyQualifiedName~PolicyMatrixTests"
```

Se algum caso falhar, corrija — não comente o teste.

---

## B) Verificar fluxo de impersonation (manual)

### Pré-condições

- API rodando local (`dotnet run --project src/omniDesk.Api`).
- Painel Admin rodando (`ng serve --project omniDesk.Admin`).
- CRM rodando (`ng serve --project omniDesk.Crm`).
- Tenant `clinica-x` provisionado e ativo (Spec 03).

### Passos

1. Login no painel admin como `saas_admin`.
2. Em "Tenants", clique em **Impersonar** na linha de `clinica-x`.
3. Validar: nova aba abre em `clinica-x.omnicare.ia.br/?impersonation=1`.
4. Validar: barra fixa vermelha no topo com texto "Modo impersonation — você está acessando o CRM de **clinica-x**...".
5. Validar: navegação livre como `tenant_admin` (criar departamento, ver tickets, etc.).
6. Validar: tentar **convidar novo usuário** ⇒ 403 com mensagem "Esta ação não é permitida em modo impersonation."
7. Aguarde 5 minutos sem interação. Validar: próxima ação retorna 401 e redireciona ao painel admin.
8. Em `mongo` collection `omniDesk_logs`:

   ```javascript
   db.omniDesk_logs.find({ "Impersonating": true, "TenantSlug": "clinica-x" }).limit(5)
   ```

   Validar: campo `ImpersonatedBy: "saas_admin"` presente em cada documento.

---

## C) Verificar invalidação imediata na desativação

### Passos

1. Logue como `tenant_admin` no CRM.
2. Em outra aba/navegador, logue como `attendant.maria@clinica-x.com.br`.
3. Como `tenant_admin`, desative `attendant.maria`.
4. Na aba de Maria, faça qualquer ação (ex.: clicar em "Tickets").
5. Validar: erro 401 em ≤ 1 segundo; redireciona para login.
6. Tentar logar de novo com Maria ⇒ "Usuário inativo".
7. Reativar Maria como `tenant_admin`.
8. Maria loga normalmente; sessão antiga **não** é restaurada — login limpo.

### Métrica em CI

`DeactivationFlowTests.cs` verifica em Testcontainers:

- Latência da invalidação: assertion `< 1000ms` (SC-005).

---

## D) Verificar bloqueio do último `tenant_admin`

### Passos

1. Em um tenant com **um único** `tenant_admin` ativo, tente desativá-lo.
2. Validar: 422 com mensagem "Não é possível desativar o último Administrador ativo do tenant. Promova outro usuário a Administrador antes."
3. Promova outro usuário a `tenant_admin`.
4. Tente desativar o original ⇒ aceito.

---

## E) Verificar escopo do attendant

### Passos

1. Crie tenant com 2 departamentos (A, B).
2. Crie um `attendant` vinculado **apenas** ao Departamento A.
3. Crie tickets em A e B.
4. Login como o `attendant`. Listagem de tickets retorna **apenas** os de A.
5. Tente abrir um ticket de B via URL direta (`/tickets/{id-de-B}`) ⇒ 403/404.
6. Adicione o `attendant` ao Departamento B.
7. Faça novo login (claim cache atualizado em ≤ 60 s ou após relogin). Listagem agora mostra tickets de A e B.

---

## F) Ambiente de desenvolvimento

| Variável | Valor recomendado em dev |
|---|---|
| `IMPERSONATION_JWT_TTL_SECONDS` | `300` (default) |
| `ASPNETCORE_ENVIRONMENT` | `Development` (mensagens 403 detalhadas) |

Em **produção**, garantir:

- `IMPERSONATION_JWT_TTL_SECONDS` ≤ `600` (aplicação rejeita inicialização caso contrário).
- `ASPNETCORE_ENVIRONMENT=Production` para mensagens 403 genéricas (R7).

---

## Como esta spec é consumida pelas demais

Para **qualquer feature nova** das specs 01–11:

1. Identifique a ação na matriz (esta spec, seção 4.x).
2. Use a constante `Policies.*` correspondente em `[Authorize(Policy = ...)]`.
3. Para queries que retornam dados filtráveis por departamento, invoque `.ForCurrentUserScope(currentUser, x => x.DepartmentId)`.
4. No frontend, use `*omniHasRole="['role-a', 'role-b']"` para esconder controles.
5. Adicione caso ao `PolicyMatrixTests.cs`.

Pronto. Sem necessidade de re-implementar autorização localmente.
