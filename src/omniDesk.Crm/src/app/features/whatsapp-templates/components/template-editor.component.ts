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
  FormArray,
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { InputTextareaModule } from 'primeng/inputtextarea';
import { DropdownModule } from 'primeng/dropdown';
import { MessageModule } from 'primeng/message';
import {
  CreateTemplateRequest,
  PREDEFINED_TEMPLATES,
  TYPE_LABEL,
  TemplateType,
  UpdateTemplateRequest,
  WhatsAppTemplate,
  isPredefined,
} from '../services/whatsapp-templates.types';

type EditorMode = 'create' | 'edit';

export interface EditorSubmitCreate {
  mode: 'create';
  request: CreateTemplateRequest;
}

export interface EditorSubmitUpdate {
  mode: 'update';
  id: string;
  request: UpdateTemplateRequest;
}

export type EditorSubmitEvent = EditorSubmitCreate | EditorSubmitUpdate;

/**
 * Spec 008 US5 — dialog de criação/edição de templates.
 *  - Criação: tenant escolhe tipo; pré-definidos pré-preenchem body + variáveis
 *    (variáveis read-only, count fixo). Custom permite tudo livre.
 *  - Edição: tipo é imutável; só body + variable_labels podem mudar.
 */
@Component({
  selector: 'app-template-editor',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    DialogModule,
    ButtonModule,
    InputTextModule,
    InputTextareaModule,
    DropdownModule,
    MessageModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-dialog
      [visible]="visible()"
      [modal]="true"
      [closable]="true"
      [draggable]="false"
      [style]="{ width: '600px' }"
      header="{{ headerText() }}"
      (visibleChange)="onVisibleChange($event)"
    >
      <form [formGroup]="form" class="editor-form" (ngSubmit)="onSave()">

        @if (mode() === 'create') {
          <div class="field">
            <label for="type">Tipo</label>
            <p-dropdown
              inputId="type"
              [options]="typeOptions"
              formControlName="type"
              placeholder="Selecione um tipo"
              (onChange)="onTypeChange($event.value)"
            ></p-dropdown>
          </div>
        }

        @if (showCustomSuffix()) {
          <div class="field">
            <label for="name_suffix">Sufixo do nome (snake_case)</label>
            <input
              id="name_suffix"
              pInputText
              formControlName="name_suffix"
              placeholder="primeira_consulta"
            />
            <small class="muted">
              O nome final ficará <code>custom_&#123;sufixo&#125;_&#123;slug&#125;</code>.
            </small>
          </div>
        }

        <div class="field">
          <label for="body">Corpo do template</label>
          <textarea
            id="body"
            pInputTextarea
            rows="5"
            formControlName="body_template"
            placeholder="Use {{ '{' }}{{ '{' }}1{{ '}' }}{{ '}' }}, {{ '{' }}{{ '{' }}2{{ '}' }}{{ '}' }}... nas posições marcadas"
          ></textarea>
          <small class="muted">
            <span [class.error]="bodyLengthExceeded()">
              {{ bodyLength() }} / 1024 caracteres
            </span>
          </small>
        </div>

        @if (variableLabels.length > 0) {
          <div class="field">
            <label>Variáveis (descrição para a Meta)</label>
            <div formArrayName="variable_labels" class="variables-list">
              @for (ctl of variableLabels.controls; track $index) {
                <div class="variable-row">
                  <code>&#123;&#123;{{ $index + 1 }}&#125;&#125;</code>
                  <input
                    pInputText
                    [formControlName]="$index"
                    [readonly]="variableLabelsReadOnly()"
                    [placeholder]="'Descrição da variável ' + ($index + 1)"
                  />
                  @if (!variableLabelsReadOnly()) {
                    <button
                      pButton
                      type="button"
                      icon="pi pi-times"
                      severity="danger"
                      [outlined]="true"
                      (click)="removeVariable($index)"
                      aria-label="Remover variável"
                    ></button>
                  }
                </div>
              }
            </div>
            @if (!variableLabelsReadOnly()) {
              <button
                pButton
                type="button"
                icon="pi pi-plus"
                label="Adicionar variável"
                severity="secondary"
                [outlined]="true"
                (click)="addVariable()"
              ></button>
            }
          </div>
        }

        @if (placeholderMismatch()) {
          <p-message
            severity="warn"
            text="Placeholders &#123;&#123;N&#125;&#125; no body devem ser sequenciais e casar com as variáveis."
          ></p-message>
        }
      </form>

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
          icon="pi pi-save"
          label="Salvar"
          [disabled]="form.invalid || placeholderMismatch() || saving()"
          [loading]="saving()"
          (click)="onSave()"
        ></button>
      </ng-template>
    </p-dialog>
  `,
  styles: [`
    .editor-form {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .field { display: flex; flex-direction: column; gap: 0.5rem; }
    .variables-list { display: flex; flex-direction: column; gap: 0.5rem; }
    .variable-row {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .variable-row code {
      background: #EDE7DF;
      padding: 0.25rem 0.5rem;
      border-radius: 4px;
      min-width: 3rem;
      text-align: center;
    }
    .variable-row input { flex: 1 1 auto; }
    .muted { color: #7A7A7A; font-size: 0.8rem; }
    .error { color: #B85C5C; }
    code { font-family: var(--font-family-mono, monospace); }
  `],
})
export class TemplateEditorComponent {
  readonly visible = input(false);
  readonly mode = input<EditorMode>('create');
  readonly existing = input<WhatsAppTemplate | null>(null);
  readonly saving = input(false);

  readonly submitted = output<EditorSubmitEvent>();
  readonly canceled = output<void>();

  protected readonly typeOptions = Object.entries(TYPE_LABEL).map(([value, label]) => ({
    value: value as TemplateType,
    label,
  }));

  private readonly fb = new FormBuilder();
  protected readonly form: FormGroup = this.fb.group({
    type: this.fb.control<TemplateType>('appointment_reminder', { nonNullable: true, validators: [Validators.required] }),
    name_suffix: this.fb.control<string>(''),
    body_template: this.fb.control('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(1024)] }),
    variable_labels: this.fb.array<FormControl<string>>([]),
  });

  protected get variableLabels(): FormArray<FormControl<string>> {
    return this.form.get('variable_labels') as FormArray<FormControl<string>>;
  }

  protected readonly headerText = computed(() =>
    this.mode() === 'create' ? 'Novo template' : 'Editar template',
  );

  protected readonly variableLabelsReadOnly = computed(() => {
    const t = this.form.get('type')?.value as TemplateType;
    return this.mode() === 'edit' || (t !== 'custom' && isPredefined(t));
  });

  protected readonly showCustomSuffix = computed(() => {
    return this.mode() === 'create' && (this.form.get('type')?.value as TemplateType) === 'custom';
  });

  protected readonly bodyLength = computed(() => {
    return (this.form.get('body_template')?.value as string | undefined)?.length ?? 0;
  });

  protected readonly bodyLengthExceeded = computed(() => this.bodyLength() > 1024);

  protected readonly placeholderMismatch = signal(false);

  constructor() {
    effect(() => {
      const m = this.mode();
      const e = this.existing();

      if (m === 'edit' && e) {
        this.form.patchValue({
          type: e.type,
          name_suffix: '',
          body_template: e.body_template,
        });
        this.setVariables(e.variable_labels);
        this.form.get('type')?.disable({ emitEvent: false });
      } else if (m === 'create') {
        this.form.get('type')?.enable({ emitEvent: false });
      }
    });

    // Recalcula placeholder mismatch a cada mudança de body / variáveis.
    this.form.valueChanges.subscribe(() => {
      this.placeholderMismatch.set(this.checkMismatch());
    });
  }

  protected onTypeChange(type: TemplateType): void {
    const def = PREDEFINED_TEMPLATES[type];
    this.form.patchValue({
      body_template: def.defaultBody,
      name_suffix: type === 'custom' ? this.form.get('name_suffix')?.value ?? '' : '',
    });
    this.setVariables([...def.variableLabels]);
  }

  protected addVariable(): void {
    if (this.variableLabelsReadOnly()) return;
    this.variableLabels.push(this.fb.control<string>('', { nonNullable: true, validators: [Validators.required] }));
  }

  protected removeVariable(index: number): void {
    if (this.variableLabelsReadOnly()) return;
    this.variableLabels.removeAt(index);
  }

  protected onSave(): void {
    if (this.form.invalid || this.placeholderMismatch()) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue() as {
      type: TemplateType;
      name_suffix: string;
      body_template: string;
      variable_labels: string[];
    };

    if (this.mode() === 'create') {
      this.submitted.emit({
        mode: 'create',
        request: {
          type: v.type,
          name_suffix: v.type === 'custom' ? v.name_suffix : null,
          body_template: v.body_template,
          variable_labels: v.variable_labels,
        },
      });
    } else {
      const id = this.existing()?.id;
      if (!id) return;
      this.submitted.emit({
        mode: 'update',
        id,
        request: {
          body_template: v.body_template,
          variable_labels: v.variable_labels,
        },
      });
    }
  }

  protected onCancel(): void {
    this.canceled.emit();
  }

  protected onVisibleChange(visible: boolean): void {
    if (!visible) this.canceled.emit();
  }

  private setVariables(labels: string[]): void {
    while (this.variableLabels.length) this.variableLabels.removeAt(0);
    for (const label of labels) {
      this.variableLabels.push(
        this.fb.control<string>(label, { nonNullable: true, validators: [Validators.required] }),
      );
    }
  }

  private checkMismatch(): boolean {
    const body = (this.form.get('body_template')?.value as string | undefined) ?? '';
    const labelsCount = this.variableLabels.length;
    const indexes = [...body.matchAll(/\{\{(\d+)\}\}/g)]
      .map((m) => parseInt(m[1], 10))
      .filter((n) => !Number.isNaN(n));
    const distinct = Array.from(new Set(indexes)).sort((a, b) => a - b);

    if (distinct.length !== labelsCount) return true;
    for (let i = 0; i < distinct.length; i++) {
      if (distinct[i] !== i + 1) return true;
    }
    return false;
  }
}
