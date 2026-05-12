// Spec 009 US4 — T129
// Transfer dialog: dropdown dept + attendant (dynamic) + optional note.
import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Output,
  inject,
  signal,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { map } from 'rxjs/operators';
import { DialogModule } from 'primeng/dialog';
import { DropdownModule } from 'primeng/dropdown';
import { ButtonModule } from 'primeng/button';
import { InputTextareaModule } from 'primeng/inputtextarea';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { environment } from '../../../../../environments/environment';

interface DeptOption   { id: string; name: string; }
interface AttendantOption { id: string; name: string; }

export interface TransferPayload {
  target_type: 'attendant' | 'department';
  target_attendant_id?: string | null;
  target_department_id?: string | null;
  note?: string | null;
}

@Component({
  selector: 'app-transfer-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    FormsModule,
    DialogModule,
    DropdownModule,
    ButtonModule,
    InputTextareaModule,
    ProgressSpinnerModule,
  ],
  template: `
    <p-dialog
      header="Transferir Ticket"
      [visible]="visible()"
      [modal]="true"
      [style]="{ width: '480px' }"
      (onHide)="onCancel()"
    >
      <div class="transfer-form">

        <!-- Target type -->
        <div class="field">
          <label>Transferir para</label>
          <p-dropdown
            [options]="targetTypeOptions"
            [(ngModel)]="selectedTargetType"
            optionLabel="label"
            optionValue="value"
            placeholder="Selecione..."
            (onChange)="onTargetTypeChange()"
          ></p-dropdown>
        </div>

        <!-- Department selection -->
        @if (selectedTargetType() === 'department') {
          <div class="field">
            <label>Departamento</label>
            <p-dropdown
              [options]="deptOptions()"
              [(ngModel)]="selectedDeptId"
              optionLabel="name"
              optionValue="id"
              placeholder="Selecione o departamento..."
              [loading]="loadingDepts()"
            ></p-dropdown>
          </div>
        }

        <!-- Attendant selection — shown for attendant transfer, or after dept selection -->
        @if (selectedTargetType() === 'attendant') {
          <div class="field">
            <label>Atendente</label>
            <p-dropdown
              [options]="attendantOptions()"
              [(ngModel)]="selectedAttendantId"
              optionLabel="name"
              optionValue="id"
              placeholder="Selecione o atendente..."
              [loading]="loadingAttendants()"
            ></p-dropdown>
          </div>
        }

        <!-- Optional note -->
        <div class="field">
          <label>Contexto / Nota <span class="optional">(opcional)</span></label>
          <textarea
            pInputTextarea
            [(ngModel)]="note"
            rows="3"
            maxlength="5000"
            placeholder="Adicione contexto para o próximo atendente..."
            style="width: 100%;"
          ></textarea>
        </div>

      </div>

      <ng-template pTemplate="footer">
        <p-button
          label="Cancelar"
          severity="secondary"
          (onClick)="onCancel()"
        ></p-button>
        <p-button
          label="Transferir"
          [disabled]="!canSubmit()"
          [loading]="submitting()"
          (onClick)="onSubmit()"
        ></p-button>
      </ng-template>
    </p-dialog>
  `,
  styles: [`
    .transfer-form { display: flex; flex-direction: column; gap: 16px; }
    .field { display: flex; flex-direction: column; gap: 4px; }
    label { font-size: 13px; font-weight: 500; color: var(--color-text-primary, #2f2f2f); }
    .optional { font-weight: 400; color: var(--color-text-muted, #7a7a7a); }
  `],
})
export class TransferDialogComponent {
  @Output() confirmed = new EventEmitter<TransferPayload>();
  @Output() cancelled = new EventEmitter<void>();

  private readonly http = inject(HttpClient);
  private readonly apiBase = environment.apiUrl;

  readonly visible  = signal(false);
  readonly loadingDepts      = signal(false);
  readonly loadingAttendants = signal(false);
  readonly submitting        = signal(false);

  readonly deptOptions      = signal<DeptOption[]>([]);
  readonly attendantOptions = signal<AttendantOption[]>([]);

  selectedTargetType = signal<'attendant' | 'department'>('department');
  selectedDeptId     = signal<string | null>(null);
  selectedAttendantId = signal<string | null>(null);
  note = signal('');

  readonly targetTypeOptions = [
    { label: 'Departamento (fila)', value: 'department' },
    { label: 'Atendente específico', value: 'attendant' },
  ];

  readonly canSubmit = computed(() => {
    if (this.submitting()) return false;
    if (this.selectedTargetType() === 'department') return !!this.selectedDeptId();
    return !!this.selectedAttendantId();
  });

  open(): void {
    this.visible.set(true);
    this.loadDepts();
    this.loadAttendants();
  }

  onTargetTypeChange(): void {
    this.selectedDeptId.set(null);
    this.selectedAttendantId.set(null);
  }

  onCancel(): void {
    this.visible.set(false);
    this.cancelled.emit();
  }

  async onSubmit(): Promise<void> {
    this.submitting.set(true);
    try {
      const payload: TransferPayload =
        this.selectedTargetType() === 'attendant'
          ? { target_type: 'attendant', target_attendant_id: this.selectedAttendantId(), note: this.note() || null }
          : { target_type: 'department', target_department_id: this.selectedDeptId(), note: this.note() || null };

      this.confirmed.emit(payload);
      this.visible.set(false);
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadDepts(): Promise<void> {
    this.loadingDepts.set(true);
    try {
      const depts = await firstValueFrom(
        this.http
          .get<{ success: boolean; data: DeptOption[] }>(`${this.apiBase}/api/departments`)
          .pipe(map((r) => r.data ?? [])),
      );
      this.deptOptions.set(depts);
    } catch {
      this.deptOptions.set([]);
    } finally {
      this.loadingDepts.set(false);
    }
  }

  private async loadAttendants(): Promise<void> {
    this.loadingAttendants.set(true);
    try {
      const attendants = await firstValueFrom(
        this.http
          .get<{ success: boolean; data: AttendantOption[] }>(`${this.apiBase}/api/attendants`)
          .pipe(map((r) => r.data ?? [])),
      );
      this.attendantOptions.set(attendants);
    } catch {
      this.attendantOptions.set([]);
    } finally {
      this.loadingAttendants.set(false);
    }
  }
}
