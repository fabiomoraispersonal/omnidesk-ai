import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  output,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { CheckboxModule } from 'primeng/checkbox';
import { MessageModule } from 'primeng/message';
import {
  UpdateWhatsAppConfigRequest,
  WhatsAppConfig,
} from '../services/whatsapp-config.types';

/**
 * Spec 008 US2 — formulário de credenciais Meta (Phone Number ID, WABA ID,
 * Access Token, App Secret, Display Name + business hours).
 *
 * Lógica de "manter o existente": access_token/app_secret são exibidos como
 * mascarados quando já configurados. Toggle "Alterar" expõe o input em texto;
 * deixar vazio = não enviar (backend mantém o valor cifrado existente).
 *
 * RBAC: supervisor recebe `readOnly=true` → todos os campos desabilitados.
 */
@Component({
  selector: 'app-credentials-form',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    InputTextModule,
    PasswordModule,
    CheckboxModule,
    MessageModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="onSubmit()" class="credentials-form">
      <div class="field">
        <label for="phone_number_id">Phone Number ID</label>
        <input
          id="phone_number_id"
          pInputText
          formControlName="phone_number_id"
          placeholder="Ex: 123456789012345"
        />
      </div>

      <div class="field">
        <label for="waba_id">WABA ID</label>
        <input
          id="waba_id"
          pInputText
          formControlName="waba_id"
          placeholder="Ex: 987654321098765"
        />
      </div>

      <div class="field">
        <label for="phone_number">Phone Number (E.164)</label>
        <input
          id="phone_number"
          pInputText
          formControlName="phone_number"
          placeholder="Ex: +5511999999999"
        />
      </div>

      <div class="field">
        <label for="display_name">Display Name</label>
        <input
          id="display_name"
          pInputText
          formControlName="display_name"
          placeholder="Ex: Clínica ABC Saúde"
        />
      </div>

      <div class="field secret-field">
        <label for="access_token">
          Access Token
          @if (accessTokenConfigured()) {
            <span class="hint">— já configurado</span>
          }
        </label>
        @if (!showAccessToken() && accessTokenConfigured()) {
          <div class="masked-row">
            <input pInputText [value]="'•••••••••••••••••••••• (configurado)'" readonly />
            @if (!readOnly()) {
              <button
                pButton
                type="button"
                label="Alterar"
                severity="secondary"
                (click)="enableAccessTokenEdit()"
              ></button>
            }
          </div>
        } @else {
          <input
            id="access_token"
            pInputText
            type="text"
            formControlName="access_token"
            placeholder="EAA... (Access Token permanente da Meta)"
          />
        }
      </div>

      <div class="field secret-field">
        <label for="app_secret">
          App Secret
          @if (appSecretConfigured()) {
            <span class="hint">— já configurado</span>
          }
        </label>
        @if (!showAppSecret() && appSecretConfigured()) {
          <div class="masked-row">
            <input pInputText [value]="'•••••••••••••••••••••• (configurado)'" readonly />
            @if (!readOnly()) {
              <button
                pButton
                type="button"
                label="Alterar"
                severity="secondary"
                (click)="enableAppSecretEdit()"
              ></button>
            }
          </div>
        } @else {
          <input
            id="app_secret"
            pInputText
            type="text"
            formControlName="app_secret"
            placeholder="32–64 caracteres hex"
          />
        }
      </div>

      <div class="field-inline">
        <p-checkbox
          formControlName="business_hours_enabled"
          [binary]="true"
          inputId="business_hours"
        ></p-checkbox>
        <label for="business_hours">Respeitar horário comercial do departamento</label>
      </div>

      @if (form.dirty && !form.valid && form.touched) {
        <p-message severity="warn" text="Corrija os campos destacados antes de salvar."></p-message>
      }

      <div class="actions">
        <button
          pButton
          type="submit"
          label="Salvar credenciais"
          icon="pi pi-save"
          [disabled]="readOnly() || saving() || form.invalid"
          [loading]="saving()"
        ></button>
      </div>
    </form>
  `,
  styles: [`
    .credentials-form {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }
    .field-inline {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .secret-field .masked-row {
      display: flex;
      gap: 0.5rem;
    }
    .secret-field .masked-row input {
      flex: 1 1 auto;
      font-family: var(--font-family-mono, monospace);
    }
    .hint {
      color: var(--color-text-muted, #7a7a7a);
      font-size: 0.875rem;
      font-weight: normal;
    }
    .actions {
      display: flex;
      gap: 0.5rem;
      margin-top: 1rem;
    }
  `],
})
export class CredentialsFormComponent {
  readonly config = input.required<WhatsAppConfig | null>();
  readonly readOnly = input(false);
  readonly saving = input(false);

  readonly submitted = output<UpdateWhatsAppConfigRequest>();

  private readonly fb = new FormBuilder();
  protected readonly showAccessToken = signal(false);
  protected readonly showAppSecret = signal(false);

  protected readonly accessTokenConfigured = computed(
    () => this.config()?.access_token_configured ?? false,
  );
  protected readonly appSecretConfigured = computed(
    () => this.config()?.app_secret_configured ?? false,
  );

  protected readonly form: FormGroup = this.fb.group({
    phone_number: [''],
    display_name: [''],
    waba_id: [''],
    phone_number_id: [''],
    access_token: ['', [Validators.minLength(100), Validators.maxLength(500)]],
    app_secret: ['', [Validators.minLength(32), Validators.maxLength(64)]],
    business_hours_enabled: [false],
  });

  constructor() {
    effect(() => {
      const cfg = this.config();
      if (cfg) {
        this.form.patchValue({
          phone_number: cfg.phone_number ?? '',
          display_name: cfg.display_name ?? '',
          waba_id: cfg.waba_id ?? '',
          phone_number_id: cfg.phone_number_id ?? '',
          access_token: '',
          app_secret: '',
          business_hours_enabled: cfg.business_hours_enabled,
        });
        // Reset edit toggles ao receber config nova.
        this.showAccessToken.set(!cfg.access_token_configured);
        this.showAppSecret.set(!cfg.app_secret_configured);
      }
    });

    effect(() => {
      if (this.readOnly()) {
        this.form.disable({ emitEvent: false });
      } else {
        this.form.enable({ emitEvent: false });
      }
    });
  }

  enableAccessTokenEdit(): void {
    this.showAccessToken.set(true);
    this.form.patchValue({ access_token: '' });
  }

  enableAppSecretEdit(): void {
    this.showAppSecret.set(true);
    this.form.patchValue({ app_secret: '' });
  }

  onSubmit(): void {
    if (this.form.invalid || this.readOnly()) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.value;
    // Strings vazias significam "manter o existente" — passamos apenas o
    // que foi efetivamente alterado.
    const payload: UpdateWhatsAppConfigRequest = {
      phone_number: v.phone_number || null,
      display_name: v.display_name || null,
      waba_id: v.waba_id || null,
      phone_number_id: v.phone_number_id || null,
      business_hours_enabled: !!v.business_hours_enabled,
    };

    if (v.access_token) payload.access_token = v.access_token;
    if (v.app_secret) payload.app_secret = v.app_secret;

    this.submitted.emit(payload);
  }
}
