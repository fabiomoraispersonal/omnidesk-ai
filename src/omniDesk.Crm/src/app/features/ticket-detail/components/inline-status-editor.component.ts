// Spec 009 US2 — Dropdown showing valid next-status transitions for a ticket.
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  input,
  output,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DropdownModule } from 'primeng/dropdown';
import { TicketStatus } from '../../tickets-kanban/services/tickets.service';

interface StatusOption {
  label: string;
  value: TicketStatus;
}

const STATUS_LABELS: Record<TicketStatus, string> = {
  new:            'Na Fila',
  in_progress:    'Em Andamento',
  waiting_client: 'Aguardando Cliente',
  resolved:       'Resolvido',
  cancelled:      'Cancelado',
};

// Valid transitions (excludes terminal statuses — those go through confirm dialogs)
const VALID_TRANSITIONS: Partial<Record<TicketStatus, TicketStatus[]>> = {
  new:            ['in_progress'],
  in_progress:    ['waiting_client'],
  waiting_client: ['in_progress'],
};

@Component({
  selector: 'app-inline-status-editor',
  standalone: true,
  imports: [CommonModule, FormsModule, DropdownModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="status-editor">
      <span class="current-label">Status</span>
      <p-dropdown
        [options]="options()"
        [(ngModel)]="selected"
        optionLabel="label"
        optionValue="value"
        [placeholder]="currentLabel()"
        [disabled]="options().length === 0"
        (onChange)="onSelect($event.value)"
        [style]="{ 'min-width': '180px' }"
        appendTo="body"
      />
    </div>
  `,
  styles: [`
    .status-editor {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
    .current-label {
      font-size: 11px;
      font-weight: 600;
      color: #7A7A7A;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }
  `],
})
export class InlineStatusEditorComponent implements OnInit {
  readonly currentStatus = input.required<TicketStatus>();
  readonly statusChange = output<TicketStatus>();

  selected: TicketStatus | null = null;

  ngOnInit(): void {
    this.selected = this.currentStatus();
  }

  readonly currentLabel = computed(() => STATUS_LABELS[this.currentStatus()] ?? this.currentStatus());

  readonly options = computed<StatusOption[]>(() => {
    const transitions = VALID_TRANSITIONS[this.currentStatus()] ?? [];
    return transitions.map((s) => ({ label: STATUS_LABELS[s], value: s }));
  });

  onSelect(newStatus: TicketStatus): void {
    this.statusChange.emit(newStatus);
  }
}
