// Spec 009 US9 — T175
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import {
  CdkDrag,
  CdkDragDrop,
  CdkDropList,
  moveItemInArray,
} from '@angular/cdk/drag-drop';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ColorPickerModule } from 'primeng/colorpicker';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { PipelineColumnData, PipelineConfigService } from './services/pipeline-config.service';

interface EditableColumn extends PipelineColumnData {
  nameEdit: string;
  colorEdit: string;
}

@Component({
  selector: 'app-pipeline-config',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    FormsModule,
    CdkDropList,
    CdkDrag,
    ButtonModule,
    InputTextModule,
    ColorPickerModule,
    ToastModule,
  ],
  providers: [MessageService],
  template: `
    <p-toast></p-toast>

    <div class="pipeline-config-page">
      <h2 class="page-title">Configurar Pipeline</h2>

      @if (loading()) {
        <p class="status-text">Carregando...</p>
      } @else if (!pipelineId()) {
        <p class="status-text">Pipeline não encontrado para este departamento.</p>
      } @else {
        <p class="hint">Arraste as colunas para reordenar. Edite o nome e a cor de cada coluna.</p>

        <div
          cdkDropList
          [cdkDropListData]="columns()"
          (cdkDropListDropped)="onDrop($event)"
          class="columns-list"
        >
          @for (col of columns(); track col.id) {
            <div cdkDrag class="column-row">
              <i class="pi pi-bars drag-handle" cdkDragHandle></i>

              <div class="column-color-preview" [style.background]="col.colorEdit"></div>

              <input
                pInputText
                [(ngModel)]="col.nameEdit"
                [placeholder]="col.status_mapping"
                class="column-name-input"
              />

              <span class="status-tag">{{ col.status_mapping }}</span>

              <p-colorPicker [(ngModel)]="col.colorEdit" format="hex"></p-colorPicker>
            </div>
          }
        </div>

        @if (errorMessage()) {
          <p class="error-msg">{{ errorMessage() }}</p>
        }

        <div class="save-row">
          <p-button
            label="Salvar configuração"
            [loading]="saving()"
            [disabled]="saving()"
            (onClick)="onSave()"
          ></p-button>
        </div>
      }
    </div>
  `,
  styles: [`
    .pipeline-config-page { padding: 24px; max-width: 600px; }
    .page-title { font-size: 20px; font-weight: 700; margin-bottom: 8px; }
    .hint { font-size: 13px; color: var(--color-text-muted, #7a7a7a); margin-bottom: 20px; }
    .status-text { color: var(--color-text-muted, #7a7a7a); }
    .columns-list { display: flex; flex-direction: column; gap: 10px; }
    .column-row { display: flex; align-items: center; gap: 12px; padding: 12px 16px;
      border-radius: 8px; border: 1px solid #e0e0e0; background: #fff; cursor: grab; }
    .drag-handle { cursor: grab; color: #aaa; }
    .column-color-preview { width: 20px; height: 20px; border-radius: 4px; flex-shrink: 0; }
    .column-name-input { flex: 1; }
    .status-tag { font-size: 11px; color: #999; white-space: nowrap; }
    .save-row { margin-top: 20px; display: flex; justify-content: flex-end; }
    .error-msg { color: var(--color-danger, #b85c5c); font-size: 13px; margin-top: 8px; }
  `],
})
export class PipelineConfigComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly pipelineService = inject(PipelineConfigService);
  private readonly messageService = inject(MessageService);

  readonly loading    = this.pipelineService.loading;
  readonly columns    = signal<EditableColumn[]>([]);
  readonly pipelineId = signal<string | null>(null);
  readonly saving     = signal(false);
  readonly errorMessage = signal('');

  ngOnInit(): void {
    const deptId = this.route.snapshot.paramMap.get('departmentId') ?? '';
    void this.load(deptId);
  }

  onDrop(event: CdkDragDrop<EditableColumn[]>): void {
    const cols = [...this.columns()];
    moveItemInArray(cols, event.previousIndex, event.currentIndex);
    cols.forEach((c, i) => (c.order = i + 1));
    this.columns.set(cols);
  }

  async onSave(): Promise<void> {
    const id = this.pipelineId();
    if (!id) return;

    this.saving.set(true);
    this.errorMessage.set('');

    const result = await this.pipelineService.updateColumns(id, {
      columns: this.columns().map((c) => ({
        id:             c.id,
        name:           c.nameEdit.trim() || c.name,
        status_mapping: c.status_mapping,
        order:          c.order,
        color:          c.colorEdit || null,
      })),
    });

    if (result.success) {
      this.messageService.add({ severity: 'success', summary: 'Pipeline salvo', life: 3000 });
    } else {
      this.errorMessage.set(result.error ?? 'Erro ao salvar.');
    }
    this.saving.set(false);
  }

  private async load(deptId: string): Promise<void> {
    const data = await this.pipelineService.getByDepartment(deptId);
    if (!data) return;
    this.pipelineId.set(data.id);
    this.columns.set(data.columns.map((c) => ({
      ...c,
      nameEdit:  c.name,
      colorEdit: c.color ?? '#6F7D5C',
    })));
  }
}
