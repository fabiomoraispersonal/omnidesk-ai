import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
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
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ListboxModule } from 'primeng/listbox';
import { MessageModule } from 'primeng/message';
import { WhatsAppTemplatesService } from '../../whatsapp-templates/services/whatsapp-templates.service';
import {
  TYPE_LABEL,
  WhatsAppTemplate,
} from '../../whatsapp-templates/services/whatsapp-templates.types';

export interface TemplatePickerResult {
  templateId: string;
  variables: Record<string, string>;
}

/**
 * Spec 008 US4 T100 — dialog para o atendente selecionar template aprovado +
 * preencher variáveis quando a janela de 24h está expirada.
 *
 * Lista carregada via WhatsAppTemplatesService.list({ status: 'approved' }).
 * Atendente clica num template → form dinâmico de variáveis aparece →
 * preview do body renderizado com substituição → "Enviar".
 *
 * Emite TemplatePickerResult com templateId + variables map (chaves "1".."N").
 * contracts/whatsapp-templates-api.md.
 */
@Component({
  selector: 'app-template-picker-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    DialogModule,
    ButtonModule,
    InputTextModule,
    ListboxModule,
    MessageModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-dialog
      [visible]="visible()"
      [modal]="true"
      [closable]="true"
      [draggable]="false"
      [style]="{ width: '640px', maxHeight: '80vh' }"
      header="Enviar template aprovado"
      (visibleChange)="onVisibleChange($event)"
    >
      <p-message
        severity="info"
        text="A janela de 24h da Meta expirou nesta conversa. Selecione um template aprovado para retomar contato."
      ></p-message>

      @if (loading()) {
        <p class="muted">Carregando templates aprovados…</p>
      } @else if (approvedTemplates().length === 0) {
        <p-message
          severity="warn"
          text="Nenhum template aprovado disponível. Crie e submeta templates em Configurações → WhatsApp → Templates."
        ></p-message>
      } @else {
        <div class="picker">
          <section class="template-list">
            <label class="block">Templates aprovados</label>
            <p-listbox
              [options]="approvedTemplates()"
              optionLabel="name"
              [(ngModel)]="selectedTemplate"
              (onChange)="onTemplateSelected($event.value)"
            >
              <ng-template let-tpl pTemplate="item">
                <div class="tpl-item">
                  <strong>{{ tpl.name }}</strong>
                  <small>{{ typeLabel(tpl.type) }}</small>
                </div>
              </ng-template>
            </p-listbox>
          </section>

          @if (selectedTemplate(); as tpl) {
            <section class="template-form">
              <label class="block">Variáveis</label>
              <form [formGroup]="form" class="vars-form">
                @for (label of tpl.variable_labels; track $index) {
                  <div class="field">
                    <label [for]="'var-' + $index">
                      <code>&#123;&#123;{{ $index + 1 }}&#125;&#125;</code>
                      <span>{{ label }}</span>
                    </label>
                    <input
                      [id]="'var-' + $index"
                      pInputText
                      [formControlName]="String($index + 1)"
                      [placeholder]="label"
                    />
                  </div>
                }
              </form>

              <label class="block">Preview</label>
              <pre class="preview">{{ rendered() }}</pre>
            </section>
          }
        </div>
      }

      <ng-template pTemplate="footer">
        <button
          pButton
          type="button"
          label="Cancelar"
          severity="secondary"
          [outlined]="true"
          (click)="onCancel()"
        ></button>
        <button
          pButton
          type="button"
          icon="pi pi-send"
          label="Enviar template"
          [disabled]="!canSend()"
          [loading]="sending()"
          (click)="onSend()"
        ></button>
      </ng-template>
    </p-dialog>
  `,
  styles: [`
    .block { display: block; margin-bottom: 0.5rem; font-weight: 500; }
    .muted { color: #7A7A7A; }
    .picker {
      display: grid;
      grid-template-columns: 220px 1fr;
      gap: 1rem;
      margin-top: 1rem;
      max-height: 50vh;
    }
    .template-list { overflow-y: auto; }
    .template-form { overflow-y: auto; display: flex; flex-direction: column; gap: 0.5rem; }
    .vars-form { display: flex; flex-direction: column; gap: 0.75rem; }
    .vars-form .field { display: flex; flex-direction: column; gap: 0.25rem; }
    .vars-form label {
      display: flex; align-items: center; gap: 0.5rem;
      font-weight: normal; font-size: 0.875rem;
    }
    .vars-form code {
      background: #EDE7DF; padding: 0.1rem 0.4rem; border-radius: 3px;
      font-family: var(--font-family-mono, monospace);
    }
    .preview {
      margin: 0;
      padding: 0.75rem;
      background: #F4F1EC;
      border: 1px solid #EDE7DF;
      border-radius: 6px;
      font-family: var(--font-family-base);
      font-size: 0.875rem;
      white-space: pre-wrap;
      min-height: 4rem;
    }
    .tpl-item { display: flex; flex-direction: column; gap: 0.125rem; }
    .tpl-item small { color: #7A7A7A; font-size: 0.75rem; }
  `],
})
export class TemplatePickerDialogComponent {
  private readonly templatesService = inject(WhatsAppTemplatesService);
  private readonly fb = new FormBuilder();

  readonly visible = input(false);
  readonly sending = input(false);

  readonly submitted = output<TemplatePickerResult>();
  readonly canceled = output<void>();

  protected readonly loading = signal(false);
  protected readonly selectedTemplate = signal<WhatsAppTemplate | null>(null);
  protected readonly form: FormGroup = this.fb.group({});

  protected readonly approvedTemplates = computed(() =>
    this.templatesService.templates().filter((t) => t.status === 'approved'),
  );

  /** Expor String() para o template Angular (não permite acessar globals diretamente). */
  protected readonly String = String;

  constructor() {
    effect(() => {
      // Quando dialog abre, carrega templates aprovados (uma vez).
      if (this.visible()) {
        void this.loadApproved();
      } else {
        // Reset state on close.
        this.selectedTemplate.set(null);
        this.rebuildForm([]);
      }
    });
  }

  protected readonly rendered = computed(() => {
    const tpl = this.selectedTemplate();
    if (!tpl) return '';
    let body = tpl.body_template;
    const values = this.form.value as Record<string, string>;
    for (let i = 1; i <= tpl.variable_labels.length; i++) {
      const v = values[String(i)] ?? '';
      body = body.replaceAll(`{{${i}}}`, v || `{{${i}}}`);
    }
    return body;
  });

  protected canSend(): boolean {
    const tpl = this.selectedTemplate();
    if (!tpl) return false;
    if (this.form.invalid) return false;
    const values = this.form.value as Record<string, string>;
    for (let i = 1; i <= tpl.variable_labels.length; i++) {
      if (!values[String(i)] || values[String(i)].trim().length === 0) return false;
    }
    return true;
  }

  protected onTemplateSelected(tpl: WhatsAppTemplate | null): void {
    if (!tpl) {
      this.selectedTemplate.set(null);
      this.rebuildForm([]);
      return;
    }
    this.selectedTemplate.set(tpl);
    this.rebuildForm(tpl.variable_labels);
  }

  protected onSend(): void {
    const tpl = this.selectedTemplate();
    if (!tpl || !this.canSend()) return;

    const variables: Record<string, string> = {};
    const v = this.form.value as Record<string, string>;
    for (let i = 1; i <= tpl.variable_labels.length; i++) {
      variables[String(i)] = (v[String(i)] ?? '').trim();
    }

    this.submitted.emit({ templateId: tpl.id, variables });
  }

  protected onCancel(): void {
    this.canceled.emit();
  }

  protected onVisibleChange(open: boolean): void {
    if (!open) this.canceled.emit();
  }

  protected typeLabel(t: string): string {
    return (TYPE_LABEL as Record<string, string>)[t] ?? t;
  }

  private async loadApproved(): Promise<void> {
    if (this.loading()) return;
    this.loading.set(true);
    try {
      await this.templatesService.list({ status: 'approved' });
    } finally {
      this.loading.set(false);
    }
  }

  private rebuildForm(labels: readonly string[]): void {
    // Remove todos os controls existentes e re-cria para o template selecionado.
    for (const key of Object.keys(this.form.controls)) {
      this.form.removeControl(key);
    }
    for (let i = 0; i < labels.length; i++) {
      this.form.addControl(
        String(i + 1),
        this.fb.control<string>('', { nonNullable: true, validators: [Validators.required] }),
      );
    }
  }
}
