// Spec 009 US6 — T150
import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { PaginatorModule, PaginatorState } from 'primeng/paginator';
import { ContactsService, ContactTicket } from '../services/contacts.service';

@Component({
  selector: 'app-contact-tickets-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, RouterModule, PaginatorModule],
  template: `
    @if (loading()) {
      <p class="list-status">Carregando tickets...</p>
    } @else if (tickets().length === 0) {
      <p class="list-status">Nenhum ticket encontrado.</p>
    } @else {
      <ul class="ticket-list">
        @for (t of tickets(); track t.id) {
          <li class="ticket-item">
            <a [routerLink]="['/tickets', t.id]" class="ticket-link">
              <span class="protocol">{{ t.protocol }}</span>
              <span class="subject">{{ t.subject }}</span>
            </a>
            <span class="status status-{{ t.status }}">{{ t.status }}</span>
            <span class="date">{{ t.created_at | date:'dd/MM/yyyy' }}</span>
          </li>
        }
      </ul>
      <p-paginator
        [rows]="perPage"
        [totalRecords]="total()"
        (onPageChange)="onPageChange($event)"
      ></p-paginator>
    }
  `,
  styles: [`
    .list-status { color: var(--color-text-muted, #7a7a7a); font-size: 13px; }
    .ticket-list { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 8px; }
    .ticket-item { display: flex; align-items: center; gap: 12px; padding: 8px; border-radius: 6px;
      background: var(--color-surface-50, #f4f1ec); }
    .ticket-link { display: flex; gap: 8px; flex: 1; text-decoration: none; color: inherit; }
    .protocol { font-size: 12px; color: var(--color-text-muted, #7a7a7a); white-space: nowrap; }
    .subject { font-size: 13px; font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .status { font-size: 11px; padding: 2px 6px; border-radius: 4px; background: #e0e0e0; white-space: nowrap; }
    .date { font-size: 12px; color: var(--color-text-muted, #7a7a7a); white-space: nowrap; }
  `],
})
export class ContactTicketsListComponent implements OnInit {
  @Input({ required: true }) contactId!: string;

  private readonly contactsService = inject(ContactsService);

  readonly loading = signal(true);
  readonly tickets = signal<ContactTicket[]>([]);
  readonly total   = signal(0);
  readonly perPage = 20;
  private page = 1;

  ngOnInit(): void {
    void this.load();
  }

  async onPageChange(event: PaginatorState): Promise<void> {
    this.page = (event.page ?? 0) + 1;
    await this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const { items, total } = await this.contactsService.listTickets(this.contactId, this.page, this.perPage);
      this.tickets.set(items);
      this.total.set(total);
    } finally {
      this.loading.set(false);
    }
  }
}
