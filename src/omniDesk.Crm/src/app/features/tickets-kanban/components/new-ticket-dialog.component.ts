// Spec 009 US5 — T137
// Dialog for creating a manual ticket: contact autocomplete + form + ticket fields.
import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Output,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { map } from 'rxjs/operators';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { InputTextareaModule } from 'primeng/inputtextarea';
import { DropdownModule } from 'primeng/dropdown';
import { AutoCompleteModule } from 'primeng/autocomplete';
import { CheckboxModule } from 'primeng/checkbox';
import { environment } from '../../../../../environments/environment';

interface DeptOption { id: string; name: string; }
interface ContactSuggestion { id: string; name: string; email: string; phone?: string; }

export interface NewTicketPayload {
  department_id: string;
  subject: string;
  priority: string;
  tags: string[];
  assign_to_me: boolean;
  contact_id?: string | null;
  contact_name?: string | null;
  contact_email?: string | null;
  contact_phone?: string | null;
  note?: string | null;
}

@Component({
  selector: 'app-new-ticket-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    FormsModule,
    DialogModule,
    ButtonModule,
    InputTextModule,
    InputTextareaModule,
    DropdownModule,
    AutoCompleteModule,
    CheckboxModule,
  ],
  template: `
    <p-dialog
      header="Novo Ticket"
      [visible]="visible()"
      [modal]="true"
      [style]="{ width: '560px' }"
      (onHide)="onCancel()"
    >
      <div class="ticket-form">

        <!-- Contact search -->
        <div class="field">
          <label>Contato</label>
          <p-autoComplete
            [(ngModel)]="selectedContact"
            [suggestions]="contactSuggestions()"
            (completeMethod)="onContactSearch($event)"
            (onSelect)="onContactSelect($event)"
            (onClear)="onContactClear()"
            field="name"
            [forceSelection]="false"
            placeholder="Buscar por nome, email ou telefone..."
          ></p-autoComplete>
        </div>

        <!-- New contact fields (shown when no existing contact selected) -->
        @if (!selectedContactId()) {
          <div class="field-group new-contact">
            <span class="group-label">Novo contato</span>
            <div class="field">
              <label>Nome <span class="req">*</span></label>
              <input pInputText [(ngModel)]="contactName" placeholder="Nome completo" />
            </div>
            <div class="field-row">
              <div class="field">
                <label>Email</label>
                <input pInputText [(ngModel)]="contactEmail" type="email" placeholder="email@exemplo.com" />
              </div>
              <div class="field">
                <label>Telefone</label>
                <input pInputText [(ngModel)]="contactPhone" placeholder="+55 11 99999-9999" />
              </div>
            </div>
          </div>
        }

        <hr class="divider" />

        <!-- Ticket fields -->
        <div class="field">
          <label>Departamento <span class="req">*</span></label>
          <p-dropdown
            [options]="deptOptions()"
            [(ngModel)]="selectedDeptId"
            optionLabel="name"
            optionValue="id"
            placeholder="Selecione o departamento..."
            [loading]="loadingDepts()"
          ></p-dropdown>
        </div>

        <div class="field">
          <label>Assunto <span class="req">*</span></label>
          <input pInputText [(ngModel)]="subject" maxlength="500" placeholder="Descreva o motivo do atendimento" />
        </div>

        <div class="field-row">
          <div class="field">
            <label>Prioridade</label>
            <p-dropdown
              [options]="priorityOptions"
              [(ngModel)]="priority"
              optionLabel="label"
              optionValue="value"
            ></p-dropdown>
          </div>
          <div class="field">
            <label>Atribuir a mim?</label>
            <p-checkbox [(ngModel)]="assignToMe" [binary]="true" label="Sim"></p-checkbox>
          </div>
        </div>

        <div class="field">
          <label>Nota inicial <span class="optional">(opcional)</span></label>
          <textarea pInputTextarea [(ngModel)]="note" rows="2" maxlength="10000"
            placeholder="Contexto inicial do atendimento..."></textarea>
        </div>

      </div>

      <ng-template pTemplate="footer">
        <p-button label="Cancelar" severity="secondary" (onClick)="onCancel()"></p-button>
        <p-button
          label="Criar Ticket"
          [disabled]="!canSubmit()"
          [loading]="submitting()"
          (onClick)="onSubmit()"
        ></p-button>
      </ng-template>
    </p-dialog>
  `,
  styles: [`
    .ticket-form { display: flex; flex-direction: column; gap: 14px; }
    .field { display: flex; flex-direction: column; gap: 4px; }
    .field-row { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
    .field-group { display: flex; flex-direction: column; gap: 10px; padding: 12px;
      border: 1px dashed #c8c8c8; border-radius: 8px; background: #fafafa; }
    .group-label { font-size: 11px; color: #7a7a7a; font-weight: 600; text-transform: uppercase; }
    label { font-size: 13px; font-weight: 500; }
    .req { color: var(--color-danger, #b85c5c); }
    .optional { font-weight: 400; color: #7a7a7a; }
    .divider { border: none; border-top: 1px solid #e0e0e0; margin: 4px 0; }
  `],
})
export class NewTicketDialogComponent {
  @Output() created = new EventEmitter<{ id: string; protocol: string }>();
  @Output() cancelled = new EventEmitter<void>();

  private readonly http = inject(HttpClient);
  private readonly apiBase = environment.apiUrl;

  readonly visible  = signal(false);
  readonly loadingDepts = signal(false);
  readonly submitting   = signal(false);
  readonly deptOptions  = signal<DeptOption[]>([]);
  readonly contactSuggestions = signal<ContactSuggestion[]>([]);
  readonly selectedContactId  = signal<string | null>(null);

  selectedContact: ContactSuggestion | null = null;
  contactName  = signal('');
  contactEmail = signal('');
  contactPhone = signal('');
  selectedDeptId = signal<string | null>(null);
  subject = signal('');
  priority = signal('normal');
  assignToMe = signal(false);
  note = signal('');

  readonly priorityOptions = [
    { label: 'Normal', value: 'normal' },
    { label: 'Baixa', value: 'low' },
    { label: 'Alta', value: 'high' },
    { label: 'Urgente', value: 'urgent' },
  ];

  readonly canSubmit = () => {
    if (this.submitting()) return false;
    if (!this.selectedDeptId()) return false;
    if (!this.subject() || this.subject().trim().length < 3) return false;
    if (!this.selectedContactId() && !this.contactName() && !this.contactEmail() && !this.contactPhone()) return false;
    return true;
  };

  open(): void {
    this.visible.set(true);
    this.loadDepts();
  }

  onCancel(): void {
    this.visible.set(false);
    this.cancelled.emit();
  }

  async onContactSearch(event: { query: string }): Promise<void> {
    if (event.query.length < 2) { this.contactSuggestions.set([]); return; }
    try {
      const results = await firstValueFrom(
        this.http.get<{ data: ContactSuggestion[] }>(`${this.apiBase}/api/contacts?q=${encodeURIComponent(event.query)}&per_page=10`)
          .pipe(map((r) => r.data ?? [])),
      );
      this.contactSuggestions.set(results);
    } catch { this.contactSuggestions.set([]); }
  }

  onContactSelect(contact: ContactSuggestion): void {
    this.selectedContactId.set(contact.id);
  }

  onContactClear(): void {
    this.selectedContactId.set(null);
    this.selectedContact = null;
  }

  async onSubmit(): Promise<void> {
    this.submitting.set(true);
    try {
      const payload: NewTicketPayload = {
        department_id:  this.selectedDeptId()!,
        subject:        this.subject().trim(),
        priority:       this.priority(),
        tags:           [],
        assign_to_me:   this.assignToMe(),
        contact_id:     this.selectedContactId() ?? null,
        contact_name:   this.selectedContactId() ? null : (this.contactName() || null),
        contact_email:  this.selectedContactId() ? null : (this.contactEmail() || null),
        contact_phone:  this.selectedContactId() ? null : (this.contactPhone() || null),
        note:           this.note() || null,
      };

      const result = await firstValueFrom(
        this.http.post<{ success: boolean; data: { ticket_id: string; protocol: string } }>(
          `${this.apiBase}/api/tickets`, payload,
        ).pipe(map((r) => r.data)),
      );

      this.created.emit({ id: result.ticket_id, protocol: result.protocol });
      this.visible.set(false);
    } catch {
      // Error handled by parent via toast
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadDepts(): Promise<void> {
    this.loadingDepts.set(true);
    try {
      const depts = await firstValueFrom(
        this.http.get<{ data: DeptOption[] }>(`${this.apiBase}/api/departments`)
          .pipe(map((r) => r.data ?? [])),
      );
      this.deptOptions.set(depts);
    } catch { this.deptOptions.set([]); }
    finally { this.loadingDepts.set(false); }
  }
}
