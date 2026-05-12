// Spec 009 US6 — T152
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { TabViewModule } from 'primeng/tabview';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { ContactsService, ContactDetail } from './services/contacts.service';
import { ContactFormComponent } from './components/contact-form.component';
import { ContactTicketsListComponent } from './components/contact-tickets-list.component';
import { ContactConversationsListComponent } from './components/contact-conversations-list.component';

@Component({
  selector: 'app-contact-profile',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    TabViewModule,
    ToastModule,
    ContactFormComponent,
    ContactTicketsListComponent,
    ContactConversationsListComponent,
  ],
  providers: [MessageService],
  template: `
    <p-toast></p-toast>

    <div class="contact-profile-page">
      @if (loading()) {
        <p class="status-text">Carregando...</p>
      } @else if (!contact()) {
        <p class="status-text">Contato não encontrado.</p>
      } @else {
        <header class="profile-header">
          <h2 class="contact-name">{{ contact()!.name ?? 'Sem nome' }}</h2>
          <div class="contact-meta">
            @if (contact()!.email) { <span>{{ contact()!.email }}</span> }
            @if (contact()!.phone) { <span>{{ contact()!.phone }}</span> }
          </div>
        </header>

        <p-tabView>
          <p-tabPanel header="Dados">
            <app-contact-form
              [contact]="contact()"
              (saved)="onContactSaved()"
            ></app-contact-form>
          </p-tabPanel>

          <p-tabPanel [header]="'Tickets (' + (contact()!.tickets_count) + ')'">
            <app-contact-tickets-list [contactId]="contactId()"></app-contact-tickets-list>
          </p-tabPanel>

          <p-tabPanel [header]="'Conversas (' + (contact()!.conversations_count) + ')'">
            <app-contact-conversations-list [contactId]="contactId()"></app-contact-conversations-list>
          </p-tabPanel>
        </p-tabView>
      }
    </div>
  `,
  styles: [`
    .contact-profile-page { padding: 24px; max-width: 900px; margin: 0 auto; }
    .status-text { color: var(--color-text-muted, #7a7a7a); }
    .profile-header { margin-bottom: 20px; }
    .contact-name { font-size: 22px; font-weight: 700; margin: 0 0 6px; }
    .contact-meta { display: flex; gap: 16px; font-size: 13px; color: var(--color-text-muted, #7a7a7a); }
  `],
})
export class ContactProfileComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly contactsService = inject(ContactsService);
  private readonly messageService = inject(MessageService);

  readonly loading  = signal(true);
  readonly contact  = signal<ContactDetail | null>(null);
  readonly contactId = signal('');

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    this.contactId.set(id);
    void this.load(id);
  }

  async onContactSaved(): Promise<void> {
    await this.load(this.contactId());
    this.messageService.add({ severity: 'success', summary: 'Contato atualizado', life: 3000 });
  }

  private async load(id: string): Promise<void> {
    this.loading.set(true);
    const data = await this.contactsService.get(id);
    this.contact.set(data);
    this.loading.set(false);
  }
}
