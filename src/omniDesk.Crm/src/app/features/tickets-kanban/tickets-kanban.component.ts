// Spec 009 US2 — Main Kanban board. Orchestrates columns, filters, WS events.
import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import {
  CdkDragDrop,
  CdkDropListGroup,
  moveItemInArray,
  transferArrayItem,
} from '@angular/cdk/drag-drop';
import { DropdownModule } from 'primeng/dropdown';
import { InputTextModule } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import {
  TicketFilters,
  TicketStatus,
  TicketSummary,
  TicketsService,
} from './services/tickets.service';
import { KanbanWebSocketService } from './services/kanban-websocket.service';
import { KanbanColumn, KanbanColumnComponent } from './components/kanban-column.component';
import { NewTicketDialogComponent } from './components/new-ticket-dialog.component';
import { SearchBarComponent } from './components/search-bar.component';
import { KanbanFiltersComponent, KanbanFilterState } from './components/kanban-filters.component';

interface FilterState {
  department_id: string | null;
  attendant_id: string | null;
  channel: string | null;
  priority: string | null;
  q: string;
}

const DEFAULT_COLUMNS: Omit<KanbanColumn, 'tickets'>[] = [
  { id: 'col-new',            name: 'Na Fila',              status: 'new',            color: '#6F7D5C' },
  { id: 'col-in_progress',    name: 'Em Andamento',         status: 'in_progress',    color: '#C09A4D' },
  { id: 'col-waiting_client', name: 'Aguardando Cliente',   status: 'waiting_client', color: '#B85C5C' },
];

// Status transitions allowed via drag-drop
const ALLOWED_TRANSITIONS: Record<TicketStatus, TicketStatus[]> = {
  new:            ['in_progress', 'cancelled'],
  in_progress:    ['waiting_client', 'resolved', 'cancelled'],
  waiting_client: ['in_progress', 'resolved', 'cancelled'],
  resolved:       [],
  cancelled:      [],
};

@Component({
  selector: 'app-tickets-kanban',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    CdkDropListGroup,
    DropdownModule,
    InputTextModule,
    ButtonModule,
    ToastModule,
    KanbanColumnComponent,
    NewTicketDialogComponent,
    SearchBarComponent,
    KanbanFiltersComponent,
  ],
  providers: [MessageService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tickets-kanban.component.html',
  styleUrls: ['./tickets-kanban.component.scss'],
})
export class TicketsKanbanComponent implements OnInit, OnDestroy {
  @ViewChild('newTicketDialog') private newTicketDialog!: NewTicketDialogComponent;

  private readonly ticketsService = inject(TicketsService);
  private readonly kanbanWs = inject(KanbanWebSocketService);
  private readonly router = inject(Router);
  private readonly messageService = inject(MessageService);

  readonly loading = this.ticketsService.loading;

  readonly filters = signal<FilterState>({
    department_id: null,
    attendant_id: null,
    channel: null,
    priority: null,
    q: '',
  });

  readonly searchActive = computed(() => this.filters().q.length >= 3);

  readonly searchResults = computed<TicketSummary[]>(() => {
    if (!this.searchActive()) return [];
    return this.ticketsService.tickets();
  });

  readonly columns = computed<KanbanColumn[]>(() => {
    const tickets = this.ticketsService.tickets();
    return DEFAULT_COLUMNS.map((col) => ({
      ...col,
      tickets: tickets.filter((t) => t.status === col.status),
    }));
  });

  private wsEffectRef: ReturnType<typeof effect> | null = null;

  ngOnInit(): void {
    this.kanbanWs.connect();
    void this.reload();

    // React to WS ticket events
    this.wsEffectRef = effect(() => {
      const ev = this.kanbanWs.ticketEvents();
      if (!ev) return;
      this.ticketsService.applyWsEvent(ev);

      // Toast for SLA alerts (T120)
      if (ev.type === 'ticket.sla_warning') {
        const p = ev.payload as import('./services/tickets.service').TicketSlaWarningPayload;
        const typeLabel = p.sla_type === 'first_response' ? 'Primeira Resposta' : 'Resolução';
        this.messageService.add({
          severity: 'warn',
          summary: `Alerta de SLA — ${typeLabel}`,
          detail: `Ticket próximo do prazo SLA (80%)`,
          life: 6000,
        });
      }
      if (ev.type === 'ticket.sla_breached') {
        const p = ev.payload as import('./services/tickets.service').TicketSlaBreadchedPayload;
        const typeLabel = p.sla_type === 'first_response' ? 'Primeira Resposta' : 'Resolução';
        this.messageService.add({
          severity: 'error',
          summary: `SLA Violado — ${typeLabel}`,
          detail: `Prazo de SLA ultrapassado`,
          life: 10000,
        });
      }
    });
  }

  ngOnDestroy(): void {
    // effect handles its own cleanup; nothing to unsubscribe manually
  }

  async reload(): Promise<void> {
    const f = this.filters();
    const apiFilters: TicketFilters = {
      department_id: f.department_id ?? undefined,
      attendant_id: f.attendant_id ?? undefined,
      channel: (f.channel as TicketFilters['channel']) ?? undefined,
      priority: (f.priority as TicketFilters['priority']) ?? undefined,
      q: f.q || undefined,
      include_terminal: f.q.length >= 3,
    };
    await this.ticketsService.load(apiFilters);
  }

  onFilterChange(): void {
    void this.reload();
  }

  onAdvancedFilterChange(state: KanbanFilterState): void {
    this.filters.update((f) => ({
      ...f,
      department_id: state.department_id,
      attendant_id:  state.attendant_id,
      channel:       state.channel,
      priority:      state.priority,
    }));
    void this.reload();
  }

  onSearchChange(q: string): void {
    this.filters.update((f) => ({ ...f, q }));
    void this.reload();
  }

  onTicketClicked(ticketId: string): void {
    void this.router.navigate(['/tickets', ticketId]);
  }

  openNewTicketDialog(): void {
    this.newTicketDialog.open();
  }

  onTicketCreated(result: { id: string; protocol: string }): void {
    this.messageService.add({
      severity: 'success',
      summary: 'Ticket criado',
      detail: `Protocolo ${result.protocol}`,
      life: 4000,
    });
    void this.router.navigate(['/tickets', result.id]);
  }

  async onDrop(event: CdkDragDrop<TicketSummary[]>): Promise<void> {
    if (event.previousContainer === event.container) {
      // Reorder within same column — no API call needed
      moveItemInArray(event.container.data, event.previousIndex, event.currentIndex);
      return;
    }

    const ticket: TicketSummary = event.item.data;
    const targetColumnId = event.container.id; // 'column-{status}'
    const newStatus = targetColumnId.replace('column-', '') as TicketStatus;

    const allowed = ALLOWED_TRANSITIONS[ticket.status];
    if (!allowed.includes(newStatus)) {
      this.messageService.add({
        severity: 'warn',
        summary: 'Transição inválida',
        detail: `Não é possível mover um ticket de "${ticket.status}" para "${newStatus}"`,
        life: 4000,
      });
      return;
    }

    // Optimistic update
    transferArrayItem(
      event.previousContainer.data,
      event.container.data,
      event.previousIndex,
      event.currentIndex,
    );

    try {
      await this.ticketsService.patchStatus(ticket.id, newStatus);
    } catch {
      // Rollback: move card back
      transferArrayItem(
        event.container.data,
        event.previousContainer.data,
        event.currentIndex,
        event.previousIndex,
      );
      this.messageService.add({
        severity: 'error',
        summary: 'Erro ao mover ticket',
        detail: 'Não foi possível alterar o status. Tente novamente.',
        life: 5000,
      });
    }
  }
}
