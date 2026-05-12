// Spec 009 US7 — T161
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
import { DropdownModule } from 'primeng/dropdown';
import { environment } from '../../../../../environments/environment';

export interface KanbanFilterState {
  department_id: string | null;
  attendant_id: string | null;
  channel: string | null;
  priority: string | null;
}

interface Option { label: string; value: string | null; }

@Component({
  selector: 'app-kanban-filters',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, DropdownModule],
  template: `
    <div class="kanban-filters-bar">
      <!-- Department -->
      <p-dropdown
        [options]="deptOptions()"
        [(ngModel)]="state.department_id"
        optionLabel="label"
        optionValue="value"
        placeholder="Departamento"
        [style]="{ width: '160px' }"
        (onChange)="emit()"
      ></p-dropdown>

      <!-- Attendant -->
      <p-dropdown
        [options]="attendantOptions()"
        [(ngModel)]="state.attendant_id"
        optionLabel="label"
        optionValue="value"
        placeholder="Atendente"
        [style]="{ width: '160px' }"
        (onChange)="emit()"
      ></p-dropdown>

      <!-- Channel -->
      <p-dropdown
        [options]="channelOptions"
        [(ngModel)]="state.channel"
        optionLabel="label"
        optionValue="value"
        placeholder="Canal"
        [style]="{ width: '130px' }"
        (onChange)="emit()"
      ></p-dropdown>

      <!-- Priority -->
      <p-dropdown
        [options]="priorityOptions"
        [(ngModel)]="state.priority"
        optionLabel="label"
        optionValue="value"
        placeholder="Prioridade"
        [style]="{ width: '130px' }"
        (onChange)="emit()"
      ></p-dropdown>
    </div>
  `,
  styles: [`.kanban-filters-bar { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }`],
})
export class KanbanFiltersComponent {
  @Output() filterChange = new EventEmitter<KanbanFilterState>();

  private readonly http = inject(HttpClient);
  private readonly apiBase = environment.apiUrl;

  readonly deptOptions     = signal<Option[]>([{ label: 'Todos depts', value: null }]);
  readonly attendantOptions = signal<Option[]>([
    { label: 'Todos atendentes', value: null },
    { label: 'Sem atendente', value: 'null' },
  ]);

  readonly channelOptions: Option[] = [
    { label: 'Todos os canais', value: null },
    { label: 'Live Chat', value: 'live_chat' },
    { label: 'WhatsApp', value: 'whatsapp' },
    { label: 'Manual', value: 'manual' },
  ];

  readonly priorityOptions: Option[] = [
    { label: 'Todas prioridades', value: null },
    { label: 'Normal', value: 'normal' },
    { label: 'Baixa', value: 'low' },
    { label: 'Alta', value: 'high' },
    { label: 'Urgente', value: 'urgent' },
  ];

  state: KanbanFilterState = {
    department_id: null,
    attendant_id:  null,
    channel:       null,
    priority:      null,
  };

  constructor() {
    void this.loadDepts();
    void this.loadAttendants();
  }

  emit(): void {
    this.filterChange.emit({ ...this.state });
  }

  private async loadDepts(): Promise<void> {
    try {
      const depts = await firstValueFrom(
        this.http.get<{ data: { id: string; name: string }[] }>(`${this.apiBase}/api/departments`)
          .pipe(map((r) => r.data ?? [])),
      );
      this.deptOptions.set([
        { label: 'Todos depts', value: null },
        ...depts.map((d) => ({ label: d.name, value: d.id })),
      ]);
    } catch { /* keep default */ }
  }

  private async loadAttendants(): Promise<void> {
    try {
      const atts = await firstValueFrom(
        this.http.get<{ data: { id: string; name: string }[] }>(`${this.apiBase}/api/attendants`)
          .pipe(map((r) => r.data ?? [])),
      );
      this.attendantOptions.set([
        { label: 'Todos atendentes', value: null },
        { label: 'Sem atendente', value: 'null' },
        ...atts.map((a) => ({ label: a.name, value: a.id })),
      ]);
    } catch { /* keep default */ }
  }
}
