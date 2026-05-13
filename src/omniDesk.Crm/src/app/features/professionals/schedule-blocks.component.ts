import { Component, OnInit, inject, signal, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { ProfessionalsService, ScheduleBlock } from './professionals.service';

/** Spec 011 US2 (T068) — lista e cria bloqueios de horário do profissional. */
@Component({
  selector: 'app-schedule-blocks',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule, ButtonModule, TableModule, ToastModule],
  providers: [MessageService],
  templateUrl: './schedule-blocks.component.html',
  styleUrl: './schedule-blocks.component.scss',
})
export class ScheduleBlocksComponent implements OnInit {
  @Input({ required: true }) professionalId!: string;

  private readonly svc   = inject(ProfessionalsService);
  private readonly fb    = inject(FormBuilder);
  private readonly toast = inject(MessageService);

  blocks  = signal<ScheduleBlock[]>([]);
  saving  = signal(false);
  loading = signal(false);

  form = this.fb.group({
    start_at: ['', Validators.required],
    end_at:   ['', Validators.required],
    reason:   [null as string | null],
  });

  async ngOnInit() { await this.load(); }

  async load() {
    this.loading.set(true);
    try {
      this.blocks.set(await this.svc.listBlocks(this.professionalId));
    } finally { this.loading.set(false); }
  }

  async create() {
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.saving.set(true);
    try {
      const v = this.form.getRawValue();
      await this.svc.createBlock(this.professionalId, {
        start_at: v.start_at!,
        end_at: v.end_at!,
        reason: v.reason,
      });
      this.form.reset();
      await this.load();
      this.toast.add({ severity: 'success', summary: 'Bloqueio criado' });
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro ao criar bloqueio.' });
    } finally { this.saving.set(false); }
  }

  async remove(id: string) {
    try {
      await this.svc.deleteBlock(this.professionalId, id);
      await this.load();
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro ao remover bloqueio.' });
    }
  }
}
