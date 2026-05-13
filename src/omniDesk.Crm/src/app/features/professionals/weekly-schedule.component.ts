import { Component, OnInit, inject, signal, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, FormArray, FormGroup, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { ProfessionalsService, WeeklyScheduleSlot } from './professionals.service';

const DAY_LABELS = ['Domingo', 'Segunda', 'Terça', 'Quarta', 'Quinta', 'Sexta', 'Sábado'];

/** Spec 011 US2 (T067) — grade de disponibilidade semanal do profissional (multi-turno por dia). */
@Component({
  selector: 'app-weekly-schedule',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, InputTextModule, ButtonModule, ToastModule],
  providers: [MessageService],
  templateUrl: './weekly-schedule.component.html',
  styleUrl: './weekly-schedule.component.scss',
})
export class WeeklyScheduleComponent implements OnInit {
  @Input({ required: true }) professionalId!: string;

  private readonly svc = inject(ProfessionalsService);
  private readonly fb  = inject(FormBuilder);
  private readonly toast = inject(MessageService);

  readonly dayLabels = DAY_LABELS;
  saving = signal(false);

  slotsForm = this.fb.group({
    slots: this.fb.array<FormGroup>([]),
  });

  get slotsArray() { return this.slotsForm.get('slots') as FormArray; }

  async ngOnInit() {
    const slots = await this.svc.getSchedule(this.professionalId);
    slots.forEach(s => this.addSlot(s.day_of_week, s.start_time, s.end_time));
  }

  addSlot(day = 1, start = '08:00', end = '17:00') {
    this.slotsArray.push(this.fb.group({
      day_of_week: [day, [Validators.required, Validators.min(0), Validators.max(6)]],
      start_time: [start, [Validators.required, Validators.pattern(/^\d{2}:\d{2}$/)]],
      end_time:   [end,   [Validators.required, Validators.pattern(/^\d{2}:\d{2}$/)]],
    }));
  }

  removeSlot(i: number) { this.slotsArray.removeAt(i); }

  async save() {
    if (this.slotsForm.invalid) { this.slotsForm.markAllAsTouched(); return; }
    this.saving.set(true);
    try {
      const slots: WeeklyScheduleSlot[] = this.slotsArray.value.map((s: any) => ({
        day_of_week: +s.day_of_week,
        start_time: s.start_time,
        end_time: s.end_time,
      }));
      await this.svc.updateSchedule(this.professionalId, slots);
      this.toast.add({ severity: 'success', summary: 'Agenda salva' });
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro ao salvar agenda.' });
    } finally { this.saving.set(false); }
  }
}
