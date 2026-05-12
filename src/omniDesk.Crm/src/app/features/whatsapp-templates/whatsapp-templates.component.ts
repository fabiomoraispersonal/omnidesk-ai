import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DropdownModule } from 'primeng/dropdown';
import { ConfirmationService, MessageService } from 'primeng/api';
import { HttpErrorResponse } from '@angular/common/http';
import { TemplateListComponent } from './components/template-list.component';
import {
  EditorSubmitEvent,
  TemplateEditorComponent,
} from './components/template-editor.component';
import { WhatsAppTemplatesService } from './services/whatsapp-templates.service';
import {
  STATUS_LABEL,
  TemplateStatus,
  WhatsAppTemplate,
} from './services/whatsapp-templates.types';
import { ROLES, RoleSignal } from '../../core/authorization/role.signal';

/**
 * Spec 008 US5 — tela CRM → Configurações → WhatsApp → Templates.
 *
 * RBAC visível:
 *  - tenant_admin, supervisor: CRUD completo + submit.
 *  - attendant: rota não deveria expor (route guard); defensivamente exibimos
 *    apenas em modo leitura. Backend força status=approved no list para Attendant.
 *
 * Fluxo:
 *  1. ngOnInit carrega lista.
 *  2. "Novo template" abre editor em modo create.
 *  3. Card "Editar" abre editor em modo update (só draft).
 *  4. Card "Submeter à Meta" chama service.submit; sucesso vira pending_meta.
 *  5. Card "Excluir" confirma + soft-delete (apenas draft/rejected).
 */
@Component({
  selector: 'app-whatsapp-templates',
  standalone: true,
  imports: [
    CommonModule,
    ButtonModule,
    ToastModule,
    ConfirmDialogModule,
    DropdownModule,
    TemplateListComponent,
    TemplateEditorComponent,
  ],
  providers: [MessageService, ConfirmationService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="whatsapp-templates">
      <p-toast></p-toast>
      <p-confirmDialog></p-confirmDialog>

      <header class="page-header">
        <div>
          <h1>Templates WhatsApp</h1>
          <p class="muted">
            Templates aprovados pela Meta permitem enviar mensagens fora da janela de 24h.
          </p>
        </div>
        @if (!readOnly()) {
          <button
            pButton
            type="button"
            icon="pi pi-plus"
            label="Novo template"
            (click)="openCreate()"
          ></button>
        }
      </header>

      <div class="filter-bar">
        <label for="statusFilter">Status:</label>
        <p-dropdown
          inputId="statusFilter"
          [options]="statusOptions"
          [(ngModel)]="statusFilter"
          placeholder="Todos"
          [showClear]="true"
          (onChange)="reload()"
        ></p-dropdown>
      </div>

      <app-template-list
        [items]="service.templates()"
        [readOnly]="readOnly()"
        (edit)="openEdit($event)"
        (submit)="onSubmit($event)"
        (remove)="onRemove($event)"
      ></app-template-list>

      <app-template-editor
        [visible]="editorVisible()"
        [mode]="editorMode()"
        [existing]="editingTemplate()"
        [saving]="service.saving()"
        (submitted)="onEditorSubmitted($event)"
        (canceled)="closeEditor()"
      ></app-template-editor>
    </section>
  `,
  styles: [`
    .whatsapp-templates {
      display: flex;
      flex-direction: column;
      gap: 1.5rem;
      padding: 1.5rem;
      max-width: 1200px;
      margin: 0 auto;
    }
    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      flex-wrap: wrap;
      gap: 1rem;
    }
    h1 { margin: 0 0 0.25rem 0; }
    .muted { color: #7A7A7A; margin: 0; font-size: 0.875rem; }
    .filter-bar {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
  `],
})
export class WhatsAppTemplatesComponent implements OnInit {
  protected readonly service = inject(WhatsAppTemplatesService);
  private readonly roleSignal = inject(RoleSignal);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  protected readonly readOnly = computed(() => this.roleSignal.role() === ROLES.Attendant);
  protected statusFilter: TemplateStatus | null = null;

  protected readonly editorVisible = signal(false);
  protected readonly editorMode = signal<'create' | 'edit'>('create');
  protected readonly editingTemplate = signal<WhatsAppTemplate | null>(null);

  protected readonly statusOptions = (
    ['draft', 'pending_meta', 'approved', 'rejected'] as TemplateStatus[]
  ).map((s) => ({ value: s, label: STATUS_LABEL[s] }));

  async ngOnInit(): Promise<void> {
    try {
      await this.service.list({});
    } catch (err) {
      this.handleError(err, 'Não foi possível carregar templates.');
    }
  }

  async reload(): Promise<void> {
    try {
      await this.service.list({ status: this.statusFilter ?? undefined });
    } catch (err) {
      this.handleError(err, 'Não foi possível recarregar a lista.');
    }
  }

  openCreate(): void {
    this.editorMode.set('create');
    this.editingTemplate.set(null);
    this.editorVisible.set(true);
  }

  openEdit(template: WhatsAppTemplate): void {
    this.editorMode.set('edit');
    this.editingTemplate.set(template);
    this.editorVisible.set(true);
  }

  closeEditor(): void {
    this.editorVisible.set(false);
    this.editingTemplate.set(null);
  }

  async onEditorSubmitted(evt: EditorSubmitEvent): Promise<void> {
    try {
      if (evt.mode === 'create') {
        await this.service.create(evt.request);
        this.toast.add({
          severity: 'success',
          summary: 'Template criado',
          detail: 'Template salvo em rascunho.',
          life: 3000,
        });
      } else {
        await this.service.update(evt.id, evt.request);
        this.toast.add({
          severity: 'success',
          summary: 'Template atualizado',
          life: 3000,
        });
      }
      this.closeEditor();
    } catch (err) {
      this.handleError(err, 'Não foi possível salvar.');
    }
  }

  async onSubmit(template: WhatsAppTemplate): Promise<void> {
    this.confirm.confirm({
      header: 'Submeter à Meta',
      message: `Confirma submissão do template "${template.name}"? Após submetido, ele não poderá mais ser editado até a Meta aprovar ou rejeitar.`,
      acceptLabel: 'Submeter',
      rejectLabel: 'Cancelar',
      accept: async () => {
        try {
          await this.service.submit(template.id);
          this.toast.add({
            severity: 'success',
            summary: 'Submetido',
            detail: 'Aguardando aprovação da Meta.',
            life: 3000,
          });
        } catch (err) {
          this.handleError(err, 'Não foi possível submeter.');
        }
      },
    });
  }

  async onRemove(template: WhatsAppTemplate): Promise<void> {
    this.confirm.confirm({
      header: 'Excluir template',
      message: `Confirma exclusão do template "${template.name}"? Esta ação é definitiva.`,
      acceptLabel: 'Excluir',
      acceptButtonStyleClass: 'p-button-danger',
      rejectLabel: 'Cancelar',
      accept: async () => {
        try {
          await this.service.delete(template.id);
          this.toast.add({
            severity: 'success',
            summary: 'Template excluído',
            life: 3000,
          });
        } catch (err) {
          this.handleError(err, 'Não foi possível excluir.');
        }
      },
    });
  }

  private handleError(err: unknown, fallback: string): void {
    let summary = 'Erro';
    let detail = fallback;

    if (err instanceof HttpErrorResponse && err.error?.error) {
      const code = err.error.error.code as string;
      detail = err.error.error.message ?? fallback;

      switch (code) {
        case 'TEMPLATE_NAME_CONFLICT':
          summary = 'Nome duplicado';
          break;
        case 'TEMPLATE_VARIABLE_MISMATCH':
          summary = 'Variáveis inválidas';
          break;
        case 'TEMPLATE_NOT_EDITABLE':
        case 'TEMPLATE_NOT_SUBMITTABLE':
        case 'TEMPLATE_NOT_DELETABLE':
          summary = 'Operação não permitida';
          break;
        case 'WHATSAPP_NOT_CONFIGURED':
          summary = 'Configuração incompleta';
          break;
        case 'META_REJECTED':
          summary = 'Meta rejeitou';
          break;
        case 'VALIDATION_ERROR':
          summary = 'Validação falhou';
          break;
      }
    }

    this.toast.add({ severity: 'error', summary, detail, life: 5000 });
  }
}
