// Spec 009 US2 — Inline priority picker with debounced emit.
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  input,
  output,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DropdownModule } from 'primeng/dropdown';
import { TicketPriority } from '../../tickets-kanban/services/tickets.service';

interface PriorityOption {
  label: string;
  value: TicketPriority;
}

const PRIORITY_OPTIONS: PriorityOption[] = [
  { label: 'Baixa', value: 'low' },
  { label: 'Normal', value: 'normal' },
  { label: 'Alta', value: 'high' },
  { label: 'Urgente', value: 'urgent' },
];

@Component({
  selector: 'app-inline-priority-editor',
  standalone: true,
  imports: [CommonModule, FormsModule, DropdownModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="priority-editor">
      <span class="field-label">Prioridade</span>
      <p-dropdown
        [options]="priorityOptions"
        [(ngModel)]="selected"
        optionLabel="label"
        optionValue="value"
        (onChange)="onSelect($event.value)"
        [style]="{ 'min-width': '150px' }"
        appendTo="body"
      />
    </div>
  `,
  styles: [`
    .priority-editor { display: flex; flex-direction: column; gap: 4px; }
    .field-label { font-size: 11px; font-weight: 600; color: #7A7A7A; text-transform: uppercase; letter-spacing: 0.5px; }
  `],
})
export class InlinePriorityEditorComponent implements OnInit {
  readonly currentPriority = input.required<TicketPriority>();
  readonly priorityChange = output<TicketPriority>();

  readonly priorityOptions = PRIORITY_OPTIONS;

  selected: TicketPriority = 'normal';

  private debounceTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    this.selected = this.currentPriority();
  }

  onSelect(priority: TicketPriority): void {
    if (this.debounceTimer) clearTimeout(this.debounceTimer);
    this.debounceTimer = setTimeout(() => {
      this.priorityChange.emit(priority);
    }, 300);
  }
}
