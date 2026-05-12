// Spec 009 US6 — T151
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
import { ContactsService, ContactConversation } from '../services/contacts.service';

@Component({
  selector: 'app-contact-conversations-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, RouterModule, PaginatorModule],
  template: `
    @if (loading()) {
      <p class="list-status">Carregando conversas...</p>
    } @else if (conversations().length === 0) {
      <p class="list-status">Nenhuma conversa encontrada.</p>
    } @else {
      <ul class="conv-list">
        @for (c of conversations(); track c.id) {
          <li class="conv-item">
            <span class="channel channel-{{ c.channel }}">{{ c.channel }}</span>
            <span class="status">{{ c.status }}</span>
            @if (c.ticket_id) {
              <a [routerLink]="['/tickets', c.ticket_id]" class="ticket-link">Ver ticket</a>
            }
            <span class="date">{{ c.created_at | date:'dd/MM/yyyy' }}</span>
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
    .conv-list { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 8px; }
    .conv-item { display: flex; align-items: center; gap: 12px; padding: 8px; border-radius: 6px;
      background: var(--color-surface-50, #f4f1ec); }
    .channel { font-size: 11px; padding: 2px 6px; border-radius: 4px; background: #e0e0e0; white-space: nowrap; }
    .status { font-size: 13px; color: var(--color-text-muted, #7a7a7a); }
    .ticket-link { font-size: 12px; color: var(--color-primary-500, #6f7d5c); text-decoration: none; }
    .date { font-size: 12px; color: var(--color-text-muted, #7a7a7a); margin-left: auto; }
  `],
})
export class ContactConversationsListComponent implements OnInit {
  @Input({ required: true }) contactId!: string;

  private readonly contactsService = inject(ContactsService);

  readonly loading = signal(true);
  readonly conversations = signal<ContactConversation[]>([]);
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
      const { items, total } = await this.contactsService.listConversations(this.contactId, this.page, this.perPage);
      this.conversations.set(items);
      this.total.set(total);
    } finally {
      this.loading.set(false);
    }
  }
}
