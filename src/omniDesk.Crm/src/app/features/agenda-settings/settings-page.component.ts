import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { TextareaModule } from 'primeng/textarea';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { AgendaSettingsService } from './agenda-settings.service';

/** Spec 011 T137 — CRM → Configurações → Agenda (tenant_admin only). */
@Component({
  selector: 'app-agenda-settings-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, ButtonModule, InputTextModule, InputNumberModule, TextareaModule, ToastModule],
  providers: [MessageService],
  template: `
    <p-toast />
    <div class="settings-page">
      <div class="settings-page__header">
        <h1>Configurações da Agenda</h1>
      </div>

      <form [formGroup]="form" (ngSubmit)="onSubmit()" class="settings-form">
        <div class="field">
          <label for="lateCancelWindow">Janela de cancelamento tardio (horas)</label>
          <p-inputNumber
            inputId="lateCancelWindow"
            formControlName="late_cancel_window_hours"
            [min]="1" [max]="168"
            [showButtons]="true" />
          <small>Cancelamentos dentro deste prazo exibem o aviso de taxa.</small>
        </div>

        <div class="field">
          <label for="lateCancelText">Texto de aviso de cancelamento tardio</label>
          <textarea
            pTextarea
            id="lateCancelText"
            formControlName="late_cancel_text"
            rows="3"
            placeholder="Ex.: Cancelamentos com menos de 24h poderão ser cobrados."></textarea>
        </div>

        <div class="field">
          <label for="policyText">Política de cancelamento (exibida em todo cancelamento via WhatsApp)</label>
          <textarea
            pTextarea
            id="policyText"
            formControlName="cancellation_policy_text"
            rows="3"
            placeholder="Deixe em branco para não exibir."></textarea>
        </div>

        <div class="actions">
          <p-button
            type="submit"
            label="Salvar configurações"
            icon="pi pi-save"
            [loading]="saving()"
            [disabled]="form.invalid" />
        </div>
      </form>
    </div>
  `,
  styles: [`
    .settings-page { display: flex; flex-direction: column; gap: 1.5rem; }
    .settings-page__header h1 { margin: 0; font-size: 1.5rem; font-weight: 700; }
    .settings-form { display: flex; flex-direction: column; gap: 1.25rem; max-width: 640px; }
    .field { display: flex; flex-direction: column; gap: 0.4rem; }
    label { font-weight: 600; font-size: 0.875rem; }
    small { color: var(--color-text-muted); font-size: 0.8rem; }
    .actions { display: flex; justify-content: flex-end; padding-top: 0.5rem; }
  `],
})
export class AgendaSettingsPageComponent implements OnInit {
  private readonly svc = inject(AgendaSettingsService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);

  readonly saving = signal(false);

  readonly form = this.fb.nonNullable.group({
    late_cancel_window_hours: [24, [Validators.required, Validators.min(1)]],
    late_cancel_text: ['Cancelamentos com menos de 24h poderão ser cobrados.', Validators.maxLength(500)],
    cancellation_policy_text: ['', Validators.maxLength(500)],
  });

  ngOnInit(): void {
    this.svc.get().subscribe({
      next: s => this.form.patchValue(s),
      error: () => this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao carregar configurações.' }),
    });
  }

  onSubmit(): void {
    if (this.form.invalid) return;
    this.saving.set(true);
    this.svc.update(this.form.getRawValue()).subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.add({ severity: 'success', summary: 'Salvo', detail: 'Configurações da agenda atualizadas.' });
      },
      error: () => {
        this.saving.set(false);
        this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao salvar configurações.' });
      },
    });
  }
}
