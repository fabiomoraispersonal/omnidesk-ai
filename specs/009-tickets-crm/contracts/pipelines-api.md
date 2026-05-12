# Contract — Pipelines API (CRM)

Endpoints REST autenticados. Visibilidade:

- `tenant_admin` — pode editar colunas (rename/reorder/color).
- `supervisor` — leitura.
- `tenant_attendant` — leitura (necessário para renderizar Kanban com nomes/cores configurados).

---

## `GET /api/pipelines`

Lista todos os pipelines do tenant (1 por departamento).

### Response

```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "name": "Atendimento Comercial",
      "department": { "id": "uuid", "name": "Comercial" },
      "columns": [
        { "id": "uuid", "name": "Na Fila", "status_mapping": "new", "order": 1, "color": null },
        { "id": "uuid", "name": "Em Andamento", "status_mapping": "in_progress", "order": 2, "color": "#7A9E7E" },
        { "id": "uuid", "name": "Aguardando Cliente", "status_mapping": "waiting_client", "order": 3, "color": "#C09A4D" }
      ],
      "created_at": "2026-04-01T10:00:00Z"
    }
  ]
}
```

---

## `GET /api/pipelines/{id}`

Detalhe de um pipeline com suas colunas.

### Response

Mesmo shape do item de lista acima (sem array).

### Erros

- `404 PIPELINE_NOT_FOUND`.

---

## `PUT /api/pipelines/{id}/columns`

Reordena, renomeia e/ou colore colunas. **Não permite** adicionar/remover colunas. Apenas `tenant_admin`.

### Request

Lista completa das 3 colunas (substituição total):

```json
{
  "columns": [
    { "id": "uuid", "name": "Aguardando atribuição", "status_mapping": "new", "order": 1, "color": null },
    { "id": "uuid", "name": "Em andamento", "status_mapping": "in_progress", "order": 2, "color": "#7A9E7E" },
    { "id": "uuid", "name": "Aguardando cliente", "status_mapping": "waiting_client", "order": 3, "color": "#C09A4D" }
  ]
}
```

### Response

`200 OK` com o pipeline atualizado.

### Validações

- **Exatamente 3 colunas** no array — não menos, não mais.
- Cada `status_mapping` deve ser único: `{new, in_progress, waiting_client}` exatamente uma vez.
- `name` ≤ 100 chars, não vazio.
- `order` valores únicos.
- `color`: hex `#RRGGBB` ou `null`.
- Todos os `id` devem pertencer ao pipeline alvo (não pode misturar IDs de outros pipelines).

### Erros

- `400 VALIDATION_ERROR` — schema inválido.
- `400 DUPLICATE_STATUS_MAPPING` — duas colunas com mesmo `status_mapping`.
- `400 INVALID_COLUMN_COUNT` — diferente de 3 colunas.
- `400 INVALID_COLUMN_ID` — id não pertence ao pipeline.
- `403 FORBIDDEN_ROLE` — não é `tenant_admin`.
- `404 PIPELINE_NOT_FOUND`.

### Side-effects

- Update transacional (`BEGIN; UPDATE...; COMMIT`).
- Não emite evento WebSocket — refresh do Kanban no front é leitura sob demanda.
