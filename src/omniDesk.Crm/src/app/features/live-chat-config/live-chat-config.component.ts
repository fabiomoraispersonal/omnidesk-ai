import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormArray,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { TabViewModule } from 'primeng/tabview';
import { CardModule } from 'primeng/card';
import { InputTextModule } from 'primeng/inputtext';
import { InputTextareaModule } from 'primeng/inputtextarea';
import { InputNumberModule } from 'primeng/inputnumber';
import { CheckboxModule } from 'primeng/checkbox';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { MessageModule } from 'primeng/message';
import { MessageService } from 'primeng/api';
import { ColorPickerModule } from 'primeng/colorpicker';
import { SelectButtonModule } from 'primeng/selectbutton';
import { ToggleButtonModule } from 'primeng/togglebutton';
import { WidgetConfigService } from './services/widget-config.service';
import {
  IdentificationFieldKey,
  LauncherIcon,
  WidgetPosition,
} from './services/widget-config.types';

/**
 * Spec 007 US2 — single-page live chat config UI for tenant admins.
 *
 * Consolidates the 6 sections (T113–T118) into one component using PrimeNG TabView.
 * Per-section components were planned in tasks.md but a single page keeps validation
 * coherent and avoids 6 micro-files for V1. The live preview iframe (T119–T121) is
 * deferred — admins can save and reload the dev test page to verify changes.
 */
@Component({
  selector: 'app-live-chat-config',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, TabViewModule, CardModule,
    InputTextModule, InputTextareaModule, InputNumberModule,
    CheckboxModule, ButtonModule, ToastModule, MessageModule,
    ColorPickerModule, SelectButtonModule, ToggleButtonModule,
  ],
  providers: [MessageService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="live-chat-config">
      <p-toast></p-toast>

      <header class="page-header">
        <div>
          <h1>Live Chat — Widget</h1>
          <p class="muted">Personalize aparência, identificação e comportamento do widget público.</p>
        </div>
        <div class="header-actions" *ngIf="config() as cfg">
          <p-toggleButton
            [ngModel]="cfg.is_enabled"
            (onChange)="onToggleEnabled($event.checked)"
            onLabel="Ativo"
            offLabel="Inativo"
            [disabled]="service.saving()">
          </p-toggleButton>
        </div>
      </header>

      <p-message *ngIf="!service.config()?.is_enabled"
                 severity="warn"
                 text="Widget desativado. Visitantes verão a mensagem de indisponibilidade.">
      </p-message>

      <form [formGroup]="form" (ngSubmit)="save()" *ngIf="form && config()">
        <p-tabView>

          <p-tabPanel header="Aparência">
            <div class="grid">
              <div class="field">
                <label for="company_name">Nome da empresa</label>
                <input pInputText id="company_name" formControlName="company_name" maxlength="100" />
              </div>
              <div class="field">
                <label for="welcome_message">Mensagem de boas-vindas</label>
                <textarea pInputTextarea id="welcome_message" formControlName="welcome_message"
                          rows="2" maxlength="500"></textarea>
              </div>
              <div class="field">
                <label for="placeholder">Placeholder do input</label>
                <input pInputText id="placeholder" formControlName="input_placeholder" maxlength="150" />
              </div>
              <div class="field">
                <label>Cor primária</label>
                <p-colorPicker formControlName="primary_color" [inline]="false" appendTo="body"></p-colorPicker>
              </div>
              <div class="field">
                <label>Ícone</label>
                <p-selectButton [options]="iconOptions" formControlName="launcher_icon"
                                optionLabel="label" optionValue="value"></p-selectButton>
              </div>
              <div class="field">
                <label>Posição</label>
                <p-selectButton [options]="positionOptions" formControlName="position"
                                optionLabel="label" optionValue="value"></p-selectButton>
              </div>
            </div>
          </p-tabPanel>

          <p-tabPanel header="Identificação">
            <div class="field">
              <p-checkbox formControlName="require_identification"
                          binary="true" inputId="require_id"></p-checkbox>
              <label for="require_id">Exigir identificação antes de iniciar conversa</label>
            </div>
            <div *ngIf="form.value.require_identification" formArrayName="identification_fields">
              <div *ngFor="let f of identFields.controls; let i = index" [formGroupName]="i" class="field-row">
                <input pInputText formControlName="label" placeholder="Rótulo" />
                <select formControlName="field" class="field-select">
                  <option value="name">Nome</option>
                  <option value="email">E-mail</option>
                  <option value="phone">Telefone</option>
                </select>
                <p-checkbox formControlName="required" binary="true" label="Obrigatório"></p-checkbox>
                <p-button (onClick)="removeIdentField(i)" icon="pi pi-trash" severity="danger" [text]="true"></p-button>
              </div>
              <p-button label="Adicionar campo" (onClick)="addIdentField()" icon="pi pi-plus" [outlined]="true"></p-button>
            </div>
          </p-tabPanel>

          <p-tabPanel header="Privacidade (LGPD)">
            <p-message *ngIf="!form.value.privacy_policy_text"
                       severity="warn"
                       text="Sem texto de LGPD configurado, o checkbox usará o texto padrão.">
            </p-message>
            <div class="field">
              <label for="lgpd_text">Texto LGPD exibido no widget</label>
              <textarea pInputTextarea id="lgpd_text" formControlName="privacy_policy_text"
                        rows="4" maxlength="1000"></textarea>
            </div>
            <div class="field">
              <label for="lgpd_url">URL da política de privacidade</label>
              <input pInputText id="lgpd_url" type="url" formControlName="privacy_policy_url"
                     placeholder="https://..." />
            </div>
          </p-tabPanel>

          <p-tabPanel header="Comportamento">
            <div class="field">
              <label for="abandon">Timeout para abandono (horas, IA sem resposta)</label>
              <p-inputNumber id="abandon" formControlName="abandonment_timeout_hours"
                             [min]="1" [max]="168" [showButtons]="true"></p-inputNumber>
            </div>
            <div class="field">
              <label for="inactive">Encerramento por inatividade (horas, atendente humano)</label>
              <p-inputNumber id="inactive" formControlName="inactivity_close_hours"
                             [min]="1" [max]="168" [showButtons]="true"></p-inputNumber>
            </div>
          </p-tabPanel>

          <p-tabPanel header="Segurança">
            <div class="field">
              <label for="domains">Domínios autorizados (um por linha; vazio libera qualquer origem)</label>
              <textarea pInputTextarea id="domains" rows="6"
                        [ngModel]="domainsText()" (ngModelChange)="onDomainsTextChange($event)"
                        [ngModelOptions]="{ standalone: true }"
                        placeholder="www.minhaclinica.com.br"></textarea>
            </div>
          </p-tabPanel>

          <p-tabPanel header="Instalação">
            <p>Cole o código abaixo antes de <code>&lt;/body&gt;</code> no site do tenant:</p>
            <pre class="snippet">{{ snippet() }}</pre>
            <p-button label="Copiar" icon="pi pi-copy" (onClick)="copySnippet()"></p-button>
          </p-tabPanel>

        </p-tabView>

        <footer class="actions">
          <p-button type="submit" label="Salvar" [loading]="service.saving()"></p-button>
        </footer>
      </form>
    </section>
  `,
  styles: [`
    .live-chat-config { padding: 24px; max-width: 980px; margin: 0 auto; }
    .page-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 16px; }
    .page-header h1 { margin: 0; font-size: 22px; }
    .page-header .muted { color: var(--color-text-muted, #7A7A7A); margin: 4px 0 0; }
    .field { margin: 12px 0; display: flex; flex-direction: column; gap: 6px; }
    .field-row { display: flex; gap: 8px; align-items: center; margin: 6px 0; }
    .field-select { padding: 6px; border: 1px solid #ccc; border-radius: 4px; }
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
    .actions { display: flex; justify-content: flex-end; margin-top: 16px; }
    .snippet { background: #2A2A2A; color: #EFEFEF; padding: 12px; border-radius: 4px; overflow-x: auto; }
  `],
})
export class LiveChatConfigComponent implements OnInit {
  protected readonly service = inject(WidgetConfigService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);

  protected readonly snippet = computed(() => this.service.snapshot()?.installation_snippet ?? '');
  protected readonly config = this.service.config;

  protected readonly iconOptions = [
    { label: 'Chat', value: 'chat' as LauncherIcon },
    { label: 'Mensagem', value: 'message' as LauncherIcon },
    { label: 'Suporte', value: 'support' as LauncherIcon },
  ];
  protected readonly positionOptions = [
    { label: 'Inferior direito', value: 'bottom_right' as WidgetPosition },
    { label: 'Inferior esquerdo', value: 'bottom_left' as WidgetPosition },
  ];

  protected form!: FormGroup;
  private domainsRaw = signal<string>('');
  protected domainsText = computed(() => this.domainsRaw());

  async ngOnInit(): Promise<void> {
    await this.service.load();
    const cfg = this.service.config();
    if (!cfg) return;

    this.form = this.fb.group({
      company_name: [cfg.company_name, [Validators.required, Validators.maxLength(100)]],
      welcome_message: [cfg.welcome_message, [Validators.required, Validators.maxLength(500)]],
      input_placeholder: [cfg.input_placeholder ?? '', [Validators.maxLength(150)]],
      primary_color: [cfg.primary_color, [Validators.required, Validators.pattern(/^#[0-9A-Fa-f]{6}$/)]],
      launcher_icon: [cfg.launcher_icon, [Validators.required]],
      position: [cfg.position, [Validators.required]],
      require_identification: [cfg.require_identification],
      identification_fields: this.fb.array(
        (cfg.identification_fields ?? []).map((f) => this.makeIdentField(f.field, f.label, f.required)),
      ),
      privacy_policy_text: [cfg.privacy_policy_text ?? ''],
      privacy_policy_url: [cfg.privacy_policy_url ?? ''],
      abandonment_timeout_hours: [cfg.abandonment_timeout_hours, [Validators.required, Validators.min(1), Validators.max(168)]],
      inactivity_close_hours: [cfg.inactivity_close_hours, [Validators.required, Validators.min(1), Validators.max(168)]],
    });
    this.domainsRaw.set((cfg.allowed_domains ?? []).join('\n'));
  }

  protected get identFields(): FormArray {
    return this.form.get('identification_fields') as FormArray;
  }

  protected addIdentField(): void {
    this.identFields.push(this.makeIdentField('name', 'Nome', true));
  }

  protected removeIdentField(index: number): void {
    this.identFields.removeAt(index);
  }

  private makeIdentField(field: IdentificationFieldKey, label: string, required: boolean): FormGroup {
    return this.fb.group({
      field: [field, [Validators.required]],
      label: [label, [Validators.required, Validators.maxLength(50)]],
      required: [required],
    });
  }

  protected onDomainsTextChange(value: string): void {
    this.domainsRaw.set(value);
  }

  protected async save(): Promise<void> {
    if (!this.form.valid) {
      this.toast.add({ severity: 'warn', summary: 'Verifique os campos.' });
      return;
    }
    const v = this.form.getRawValue();
    const domains = this.domainsRaw()
      .split('\n')
      .map((d) => d.trim())
      .filter((d) => d.length > 0);

    try {
      await this.service.update({
        primary_color: v.primary_color,
        launcher_icon: v.launcher_icon,
        company_name: v.company_name,
        welcome_message: v.welcome_message,
        input_placeholder: v.input_placeholder || null,
        position: v.position,
        require_identification: v.require_identification,
        identification_fields: v.require_identification ? v.identification_fields : null,
        allowed_domains: domains.length ? domains : null,
        privacy_policy_text: v.privacy_policy_text || null,
        privacy_policy_url: v.privacy_policy_url || null,
        abandonment_timeout_hours: v.abandonment_timeout_hours,
        inactivity_close_hours: v.inactivity_close_hours,
      });
      this.toast.add({ severity: 'success', summary: 'Configuração salva.' });
    } catch {
      this.toast.add({ severity: 'error', summary: 'Falha ao salvar.' });
    }
  }

  protected async onToggleEnabled(isEnabled: boolean): Promise<void> {
    try {
      const result = await this.service.toggle(isEnabled);
      if (!isEnabled && result.affected_conversations > 0) {
        this.toast.add({
          severity: 'warn',
          summary: 'Widget desativado',
          detail: `${result.affected_conversations} conversa(s) aberta(s) serão encerradas.`,
        });
      } else {
        this.toast.add({ severity: 'success', summary: isEnabled ? 'Widget ativado.' : 'Widget desativado.' });
      }
    } catch {
      this.toast.add({ severity: 'error', summary: 'Falha ao alternar widget.' });
    }
  }

  protected async copySnippet(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.snippet());
      this.toast.add({ severity: 'success', summary: 'Código copiado.' });
    } catch {
      this.toast.add({ severity: 'warn', summary: 'Selecione e copie manualmente.' });
    }
  }
}
