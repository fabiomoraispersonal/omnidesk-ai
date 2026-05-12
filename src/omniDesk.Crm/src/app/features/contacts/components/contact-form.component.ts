// Spec 009 US6 — T149
import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  OnChanges,
  Output,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { InputTextareaModule } from 'primeng/inputtextarea';
import { ContactDetail, ContactsService } from '../services/contacts.service';

@Component({
  selector: 'app-contact-form',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, ButtonModule, InputTextModule, InputTextareaModule],
  template: `
    <div class="contact-form">
      <div class="field">
        <label>Nome</label>
        <input pInputText [(ngModel)]="name" placeholder="Nome completo" />
      </div>
      <div class="field">
        <label>Email</label>
        <input pInputText [(ngModel)]="email" type="email" placeholder="email@exemplo.com" />
      </div>
      <div class="field">
        <label>Telefone</label>
        <input pInputText [(ngModel)]="phone" placeholder="+55 11 99999-9999" />
      </div>
      <div class="field">
        <label>Observações</label>
        <textarea pInputTextarea [(ngModel)]="notes" rows="3" maxlength="5000"
          placeholder="Informações adicionais do contato..."></textarea>
      </div>
      @if (errorMessage()) {
        <p class="error-msg">{{ errorMessage() }}</p>
      }
      <div class="form-actions">
        <p-button
          label="Salvar"
          [loading]="saving()"
          [disabled]="saving()"
          (onClick)="onSave()"
        ></p-button>
      </div>
    </div>
  `,
  styles: [`
    .contact-form { display: flex; flex-direction: column; gap: 12px; }
    .field { display: flex; flex-direction: column; gap: 4px; }
    label { font-size: 13px; font-weight: 500; }
    .form-actions { display: flex; justify-content: flex-end; }
    .error-msg { color: var(--color-danger, #b85c5c); font-size: 13px; }
  `],
})
export class ContactFormComponent implements OnChanges {
  @Input() contact: ContactDetail | null = null;
  @Output() saved = new EventEmitter<void>();

  private readonly contactsService = inject(ContactsService);

  name  = '';
  email = '';
  phone = '';
  notes = '';

  readonly saving = signal(false);
  readonly errorMessage = signal('');

  ngOnChanges(): void {
    if (this.contact) {
      this.name  = this.contact.name  ?? '';
      this.email = this.contact.email ?? '';
      this.phone = this.contact.phone ?? '';
      this.notes = this.contact.notes ?? '';
    }
  }

  async onSave(): Promise<void> {
    if (!this.contact) return;
    this.saving.set(true);
    this.errorMessage.set('');

    const result = await this.contactsService.update(this.contact.id, {
      name:  this.name  || null,
      email: this.email || null,
      phone: this.phone || null,
      notes: this.notes || null,
    });

    if (result.success) {
      this.saved.emit();
    } else {
      const msgs: Record<string, string> = {
        EMAIL_CONFLICT: 'Este e-mail já está associado a outro contato.',
        PHONE_CONFLICT: 'Este telefone já está associado a outro contato.',
      };
      this.errorMessage.set(msgs[result.error ?? ''] ?? 'Erro ao salvar. Tente novamente.');
    }
    this.saving.set(false);
  }
}
