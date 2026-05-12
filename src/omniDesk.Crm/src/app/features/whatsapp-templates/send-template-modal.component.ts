import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { DropdownModule } from 'primeng/dropdown';
import { InputTextModule } from 'primeng/inputtext';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { environment } from '../../../environments/environment';

interface TemplateDto {
  id: string;
  name: string;
  body_template: string;
  variable_labels: string[];
  variable_count: number;
  status: string;
}

interface TemplateListEnvelope {
  success: boolean;
  data: TemplateDto[];
}

/**
 * Spec 010 US5 T083 — Modal triggered from ticket-detail.
 * Loads approved templates → user picks one → fills named variables →
 * live preview rendered client-side → POST /api/tickets/{id}/send-template.
 */
@Component({
  selector: 'app-send-template-modal',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    ButtonModule, DialogModule, DropdownModule, InputTextModule, ToastModule,
  ],
  providers: [MessageService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-toast />
    <p-dialog
      header="Enviar template"
      [(visible)]="visibleInternal"
      [modal]="true"
      [style]="{ width: '560px' }"
      [closable]="!sending()"
      (onHide)="onClose()">

      @if (loadingTemplates()) {
        <p>Carregando templates…</p>
      } @else if (templates().length === 0) {
        <p class="empty">Nenhum template aprovado disponível.</p>
      } @else {
        <div class="field">
          <label for="tpl-select">Template:</label>
          <p-dropdown
            inputId="tpl-select"
            [options]="templates()"
            [(ngModel)]="selectedTemplate"
            optionLabel="name"
            placeholder="Escolha um template"
            (onChange)="onTemplateChange()"
            [disabled]="sending()"
            styleClass="w-full" />
        </div>

        @if (selectedTemplate) {
          @for (label of selectedTemplate.variable_labels; track label; let i = $index) {
            <div class="field">
              <label [for]="'var-' + i">{{ label }}:</label>
              <input
                pInputText
                [id]="'var-' + i"
                [(ngModel)]="variables[label]"
                (input)="recomputePreview()"
                [disabled]="sending()" />
            </div>
          }

          <div class="preview">
            <h4>Pré-visualização:</h4>
            <pre>{{ preview() }}</pre>
          </div>
        }
      }

      <ng-template pTemplate="footer">
        <p-button
          label="Cancelar"
          severity="secondary"
          [text]="true"
          (click)="onClose()"
          [disabled]="sending()" />
        <p-button
          label="Enviar"
          icon="pi pi-send"
          [loading]="sending()"
          [disabled]="!canSend()"
          (click)="onSend()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [`
    .field { margin-bottom: 12px; display: flex; flex-direction: column; gap: 4px; }
    .field label { font-size: 13px; font-weight: 600; color: var(--text-color); }
    .field input { width: 100%; }
    .preview { margin-top: 16px; padding: 12px; background: var(--surface-50);
               border-left: 3px solid var(--primary-color); border-radius: 4px; }
    .preview h4 { margin: 0 0 6px; font-size: 12px; text-transform: uppercase;
                  color: var(--text-color-secondary); }
    .preview pre { margin: 0; white-space: pre-wrap; font-family: inherit;
                   font-size: 14px; }
    .empty { color: var(--text-color-secondary); padding: 12px 0; }
  `],
})
export class SendTemplateModalComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly toast = inject(MessageService);

  @Input({ required: true }) ticketId!: string;
  @Input() visible = false;
  @Output() visibleChange = new EventEmitter<boolean>();
  @Output() sent = new EventEmitter<void>();

  // Internal copy so the dialog can bind two-way.
  protected get visibleInternal(): boolean { return this.visible; }
  protected set visibleInternal(v: boolean) {
    this.visible = v;
    this.visibleChange.emit(v);
  }

  protected readonly loadingTemplates = signal(true);
  protected readonly sending = signal(false);
  protected readonly templates = signal<TemplateDto[]>([]);
  protected selectedTemplate: TemplateDto | null = null;
  protected variables: Record<string, string> = {};
  protected readonly preview = signal('');

  protected readonly canSend = computed(() => {
    if (this.sending()) return false;
    if (!this.selectedTemplate) return false;
    // Every variable must have a non-empty value.
    return this.selectedTemplate.variable_labels.every(
      (l) => this.variables[l] && this.variables[l].trim().length > 0,
    );
  });

  async ngOnInit(): Promise<void> {
    this.loadingTemplates.set(true);
    try {
      const env = await firstValueFrom(this.http.get<TemplateListEnvelope>(
        `${environment.apiUrl}/api/whatsapp/templates`,
        { params: { status: 'approved', per_page: '50' } },
      ));
      this.templates.set(env.data);
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao carregar templates.' });
    } finally {
      this.loadingTemplates.set(false);
    }
  }

  onTemplateChange(): void {
    this.variables = {};
    if (this.selectedTemplate) {
      for (const label of this.selectedTemplate.variable_labels) {
        this.variables[label] = '';
      }
    }
    this.recomputePreview();
  }

  recomputePreview(): void {
    if (!this.selectedTemplate) { this.preview.set(''); return; }

    let body = this.selectedTemplate.body_template;
    // Substitution: WhatsApp templates use {{1}}..{{N}} placeholders; variable_labels[i]
    // maps to {{i+1}}. Replace each in order.
    for (let i = 0; i < this.selectedTemplate.variable_labels.length; i++) {
      const label = this.selectedTemplate.variable_labels[i];
      const value = this.variables[label] ?? '';
      const placeholder = new RegExp(`\\{\\{\\s*${i + 1}\\s*\\}\\}`, 'g');
      body = body.replace(placeholder, value || `[${label}]`);
    }
    this.preview.set(body);
  }

  async onSend(): Promise<void> {
    if (!this.canSend() || !this.selectedTemplate) return;
    this.sending.set(true);
    try {
      const body = {
        template_id: this.selectedTemplate.id,
        variables: { ...this.variables },
      };
      await firstValueFrom(this.http.post(
        `${environment.apiUrl}/api/tickets/${this.ticketId}/send-template`, body));
      this.toast.add({ severity: 'success', summary: 'Enviado',
                       detail: 'Template enfileirado para envio.' });
      this.sent.emit();
      this.onClose();
    } catch (e: unknown) {
      const code = (e as { error?: { error?: { code?: string; message?: string } } })
        ?.error?.error;
      const message = code?.message ?? 'Falha ao enviar template.';
      this.toast.add({ severity: 'error', summary: 'Erro', detail: message });
    } finally {
      this.sending.set(false);
    }
  }

  onClose(): void {
    if (this.sending()) return;
    this.visibleInternal = false;
    this.selectedTemplate = null;
    this.variables = {};
    this.preview.set('');
  }
}
