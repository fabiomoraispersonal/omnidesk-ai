// Spec 009 US2 — Kanban column with CDK drop list.
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  output,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  CdkDropList,
  CdkDragDrop,
} from '@angular/cdk/drag-drop';
import { TicketSummary, TicketStatus } from '../services/tickets.service';
import { TicketCardComponent } from './ticket-card.component';

export interface KanbanColumn {
  id: string;
  name: string;
  status: TicketStatus;
  color?: string;
  tickets: TicketSummary[];
}

@Component({
  selector: 'app-kanban-column',
  standalone: true,
  imports: [CommonModule, CdkDropList, TicketCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="column" [style.--column-color]="column().color ?? '#6F7D5C'">
      <!-- Column header -->
      <header class="column-header">
        <span class="column-dot" aria-hidden="true"></span>
        <span class="column-name">{{ column().name }}</span>
        <span class="column-count">{{ column().tickets.length }}</span>
      </header>

      <!-- Drop list -->
      <div
        class="column-body"
        cdkDropList
        [cdkDropListData]="column().tickets"
        [id]="'column-' + column().status"
        (cdkDropListDropped)="dropped.emit($event)"
      >
        @for (ticket of column().tickets; track ticket.id) {
          <app-ticket-card
            [ticket]="ticket"
            (clicked)="ticketClicked.emit($event)"
          />
        } @empty {
          <div class="empty-state" aria-live="polite">
            Nenhum ticket nesta etapa
          </div>
        }
      </div>
    </section>
  `,
  styles: [`
    .column {
      display: flex;
      flex-direction: column;
      width: 300px;
      min-width: 280px;
      background: #F4F1EC;
      border-radius: 10px;
      overflow: hidden;
    }

    .column-header {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 14px;
      background: #EDE7DF;
      font-size: 13px;
      font-weight: 700;
      color: #2F2F2F;
    }
    .column-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: var(--column-color, #6F7D5C);
      flex-shrink: 0;
    }
    .column-name { flex: 1; }
    .column-count {
      background: #fff;
      color: #7A7A7A;
      border-radius: 999px;
      padding: 1px 8px;
      font-size: 11px;
      font-weight: 600;
    }

    .column-body {
      flex: 1;
      min-height: 120px;
      padding: 8px;
      display: flex;
      flex-direction: column;
      gap: 8px;
      overflow-y: auto;
    }

    .empty-state {
      text-align: center;
      color: #7A7A7A;
      font-size: 12px;
      padding: 24px 8px;
    }
  `],
})
export class KanbanColumnComponent {
  readonly column = input.required<KanbanColumn>();
  readonly dropped = output<CdkDragDrop<TicketSummary[]>>();
  readonly ticketClicked = output<string>();
}
