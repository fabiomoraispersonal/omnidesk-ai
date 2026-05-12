# Contract — Kanban Frontend (Angular CRM)

Comportamento que o front-end (`features/tickets-kanban/`) deve apresentar. Não é um contrato HTTP — é um contrato de **UX/comportamento** que orienta implementação e testes E2E.

---

## 1. Layout do Kanban

```
┌───────────────────────────────────────────────────────────────────────────────┐
│  [Filtros] [Busca...]                                       [+ Novo Ticket]   │
│  ───────────────────────────────────────────────────────────────────────────  │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────────────────────┐   │
│  │  Na Fila (3)   │  │ Em Andamto (7) │  │     Aguardando Cliente (2)     │   │
│  │                │  │                │  │                                │   │
│  │  [card A]      │  │  [card D]      │  │  [card I]                      │   │
│  │  [card B]      │  │  [card E]      │  │  [card J]                      │   │
│  │  [card C]      │  │  [card F]      │  │                                │   │
│  │                │  │  [card G]      │  │                                │   │
│  │                │  │  [card H]      │  │                                │   │
│  └────────────────┘  └────────────────┘  └────────────────────────────────┘   │
└───────────────────────────────────────────────────────────────────────────────┘
```

- 3 colunas fixas (ordem e nomes configuráveis por pipeline — ver `pipelines-api.md`).
- Contagem de tickets na coluna no header.
- Empty state por coluna ("Nenhum ticket nesta etapa").

---

## 2. Anatomia do Card

```
┌──────────────────────────────────┐
│ [💬] TK-20260511-00042   ⚠️ 🟡  │  ← canal + protocolo + badges (alert + SLA)
│ ──────────────────────────────── │
│ João Silva                       │  ← nome do contato (ou "Visitante anônimo")
│                                  │
│ Cliente quer remarcar agendamen… │  ← subject truncado 60 chars
│                                  │
│ 🏷 agendamento  vip   +2         │  ← tags (até 3 + contador)
│                                  │
│ 👤 Maria Silva  ·  há 12 min     │  ← atendente + tempo desde criação
└──────────────────────────────────┘
```

- **Ícones de canal**: 💬 Live Chat, 🟢 WhatsApp (verde Meta), ✏️ Manual.
- **Badge ⚠️**: visível somente se `has_reminder_alert = true`.
- **Badge SLA**: 🟢 verde / 🟡 amarelo (>80%) / 🔴 vermelho (expirado). Calculado pelo front a cada tick visual (1s) com base em prazos + pausa.
- **Atendente**: se null → "Sem atendente" em cinza claro.
- **Drag handle**: o card inteiro é arrastável; hover muda cursor para `move`.

---

## 3. Drag-and-drop

Implementação via `@angular/cdk/drag-drop`:

```ts
@Component({
  imports: [CdkDropList, CdkDrag],
  template: `
    <div cdkDropList [cdkDropListData]="column.tickets"
                     (cdkDropListDropped)="onDrop($event)">
      <div *ngFor="let t of column.tickets" cdkDrag>
        <ticket-card [ticket]="t"></ticket-card>
      </div>
    </div>
  `
})
```

### Comportamento

- **Drop em coluna válida** (`new` ↔ `in_progress` ↔ `waiting_client`):
  1. Optimistic update — card move imediatamente.
  2. Backend: `PATCH /api/tickets/{id}/status` com `{ status: target_column.status_mapping }`.
  3. Em sucesso: nada extra (estado já está correto).
  4. Em erro: rollback do card + Toast vermelho com mensagem.
- **Drop em coluna inválida** (e.g., transição não permitida do estado atual): rejeitado **antes** do request — card volta à coluna de origem com animação curta + Toast informativo ("Use o botão Encerrar para concluir o atendimento.").
- **Drop na mesma coluna**: no-op (reordenação visual não é persistida — sem `order` por ticket; tickets são listados por `created_at` desc).

### Acessibilidade

- Suporte a teclado (CDK provê via `cdkDragKeyboardDragEnabled`).
- Anúncio via aria-live: "Ticket TK-... movido para Em Andamento".

---

## 4. Filtros

Painel acima do Kanban com `Dropdown`/`MultiSelect`/`Calendar` da PrimeNG:

| Filtro | Componente | Multi |
|---|---|---|
| Departamento | Dropdown | ❌ |
| Atendente | Dropdown (com opção "Sem atendente") | ❌ |
| Canal | Dropdown | ❌ |
| Prioridade | MultiSelect | ✅ |
| Tag | AutoComplete | ✅ |
| Período | Calendar (range) ou Atalhos (hoje/semana/mês) | ❌ |

Filtros são aplicados via querystring no `GET /api/tickets` e refetch.

---

## 5. Busca Full-Text

Campo de busca acima dos filtros:

- Min 3 chars para disparar.
- Debounce 300ms.
- Backend: `GET /api/tickets?q=...`.
- Modo **lista** (não Kanban): exibe `<ticket-card>` em coluna única, paginação 20/pg.
- Esc / botão "Limpar" volta ao Kanban.

---

## 6. SLA Countdown (no card e no detalhe)

Cálculo client-side via `signal()` + `interval(1000)`:

```ts
sla = computed(() => {
  const t = this.ticket();
  const now = new Date();
  const pausedMs = t.sla_paused_duration_minutes * 60_000;
  const pendingPauseMs = t.waiting_client_since
    ? now.getTime() - new Date(t.waiting_client_since).getTime()
    : 0;
  const effectiveDeadline = new Date(t.sla_resolution_deadline).getTime() + pausedMs + pendingPauseMs;
  const totalMs = effectiveDeadline - new Date(t.created_at).getTime();
  const consumedMs = now.getTime() - new Date(t.created_at).getTime() - pausedMs - pendingPauseMs;
  const percent = (consumedMs / totalMs) * 100;
  return percent >= 100 ? 'breached' : percent >= 80 ? 'warning' : 'ok';
});
```

- Cor do badge derivada de `sla.status`.
- No detalhe, exibe contador regressivo "Restam: 1h 23min" formatado por `date-fns`.

---

## 7. Notificações em tempo real (Toasts + browser notification)

Reusa toast pattern da Spec 007/008:

- `ticket.created` com `attendant_id = me`: Toast info "Novo ticket atribuído: TK-..." + sound.
- `ticket.sla_warning`: Toast warning ⚠️.
- `ticket.sla_breached`: Toast danger 🔴.
- Browser notification (`Notification API`) se aba CRM em background (consentimento solicitado uma vez).

---

## 8. Estados de carregamento e erro

- Skeleton loader nas 3 colunas do Kanban enquanto `GET /api/tickets` carrega (≤ 1.5s P95).
- Estado vazio do Kanban: "Nenhum ticket no momento. ☕ Aproveite a calmaria!" (humanizado).
- Erro de rede: banner topo "Conexão perdida — tentando reconectar..." + auto-retry de WebSocket.

---

## 9. Performance

- Virtualização **não** implementada V1 (limite ~500 tickets ativos/tenant).
- Refetch automático a cada 30s (`Tickets:KanbanRefreshSeconds`).
- Eventos WebSocket aplicam mutação local em vez de refetch (delta state).

---

## 10. Roteamento

| Rota | Componente | Auth |
|---|---|---|
| `/` | redirect → `/kanban` | required |
| `/kanban` | `TicketsKanbanComponent` (lazy) | required |
| `/tickets/:id` | `TicketDetailComponent` (lazy) | required + check permission |
| `/contacts/:id` | `ContactProfileComponent` (lazy) | required |
| `/settings/pipelines/:departmentId` | `PipelineConfigComponent` (lazy) | required + role `tenant_admin` |
| `/conversations` | `LiveChatInboxComponent` (Spec 007) | required |

Guards: `AuthGuard` (existente) + `RoleGuard` para `pipeline-config`.

---

## 11. Testes do CRM

- **Unit (`.spec.ts` co-localizado)** para cada component: render, signals, events.
- **Integration**: TestBed com mock HTTP + mock WebSocket.
- **Drag-drop**: usar `cdkDragDrop` event simulation utilitário do CDK.
- **E2E (Playwright V1.1)**: fluxo completo cliente Live Chat → IA transfere → atendente vê Kanban → drag para Em Andamento → resolve.
