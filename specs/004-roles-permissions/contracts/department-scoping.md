# Contract: Department Scoping (filtro horizontal do `attendant`)

Define a primitiva única usada para aplicar o escopo por departamento em consultas IQueryable que retornam **conversas** ou **tickets** para um `attendant`. Outras roles (`tenant_admin`, `supervisor`) bypass o filtro.

---

## Sinatura

```csharp
namespace OmniDesk.Infrastructure.Authorization;

public static class DepartmentScopeFilter
{
    /// <summary>
    /// Aplica o escopo de departamento do usuário corrente quando ele é attendant.
    /// Para tenant_admin/supervisor, retorna a query inalterada.
    /// </summary>
    public static IQueryable<T> ForCurrentUserScope<T>(
        this IQueryable<T> query,
        ICurrentUser currentUser,
        Func<T, Guid> departmentSelector)
        where T : class
    {
        if (currentUser.Role != Roles.Attendant)
            return query;

        var allowed = currentUser.DepartmentIds; // populado pela IClaimsTransformation
        return query.Where(x => allowed.Contains(departmentSelector(x)));
    }
}
```

`ICurrentUser` é o serviço já disponível desde a Spec 002 (escopo `Scoped`), agora enriquecido com a propriedade `DepartmentIds: IReadOnlyList<Guid>` populada pela `IClaimsTransformation` (R3).

---

## Uso

### Conversas (Spec 06)

```csharp
public Task<List<Conversation>> GetForUserAsync(CancellationToken ct) =>
    db.Conversations
      .ForCurrentUserScope(currentUser, c => c.DepartmentId)
      .OrderByDescending(c => c.UpdatedAt)
      .Take(100)
      .ToListAsync(ct);
```

### Tickets (Spec 08)

```csharp
public Task<List<Ticket>> SearchAsync(TicketFilter filter, CancellationToken ct) =>
    db.Tickets
      .ForCurrentUserScope(currentUser, t => t.DepartmentId)
      .ApplyFilter(filter)
      .ToListAsync(ct);
```

---

## Regras

1. **Sempre explícito**: toda consulta de tickets/conversas que retorne resultados filtráveis por departamento deve invocar `ForCurrentUserScope` ou ter um comentário justificando a ausência (ex.: query usada apenas em endpoint protegido por `Tickets.ViewAll` que é `tenant_admin`/`supervisor` only).
2. **Sem global query filter**: violar essa regra (R2) é defeito de PR.
3. **Cláusula adicional `AssignedToUser`**: tickets/conversas atribuídas a um `attendant` específico **sempre** são visíveis para ele, mesmo que o departamento mude — esse caso é coberto adicionando OR explícito quando aplicável:

```csharp
db.Tickets
  .Where(t => allowed.Contains(t.DepartmentId) || t.AssignedToUserId == currentUser.Id)
```

   Para esses casos, criar overload nominal — não tentar generalizar:

```csharp
public static IQueryable<Ticket> ForCurrentUserScopeOrAssignment(
    this IQueryable<Ticket> query, ICurrentUser u) { ... }
```

4. **Sem departamento vinculado**: `attendant` com `DepartmentIds.Count == 0` retorna **zero** tickets/conversas (edge case da spec). É comportamento esperado.

---

## Performance

- O filtro adiciona um único `WHERE department_id = ANY(@allowed)` à query SQL — índice composto `(tenant_id, department_id, updated_at DESC)` em `tickets` e `conversations` recomendado (Spec 08/Spec 06 cuidam dessa indexação).
- A lista `DepartmentIds` é cacheada em Redis (cache de claims, R3) — sem hit no Postgres por request.

---

## Testes

`DepartmentScopeFilterTests.cs`:

- attendant com 1 dept → vê apenas tickets desse dept.
- attendant com 2 depts → vê tickets dos dois.
- attendant sem depts → vê zero.
- supervisor → vê tudo (filter é no-op).
- tenant_admin → vê tudo.
- attendant com ticket atribuído em outro dept → variant `OrAssignment` expõe; variant default não.

Todos com Testcontainers (Postgres real).
