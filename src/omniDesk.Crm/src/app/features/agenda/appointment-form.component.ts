import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { DropdownModule } from 'primeng/dropdown';
import { CalendarModule } from 'primeng/calendar';
import { InputTextareaModule } from 'primeng/inputtextarea';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { AppointmentsService } from './appointments.service';
import { AvailabilityService, AvailabilitySlot } from './availability.service';
import { ServicesCatalogService } from '../services-catalog/services.service';
import { ProfessionalsService, ProfessionalDto } from '../professionals/professionals.service';

/**
 * Spec 011 US3 (T106) — form to create a new appointment.
 * Selects professional → service → date → slot (from AvailabilityCalculator).
 */
@Component({
  selector: 'app-appointment-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, ButtonModule, DropdownModule,
            CalendarModule, InputTextareaModule, ToastModule],
  providers: [MessageService],
  templateUrl: './appointment-form.component.html',
  styleUrls: ['./appointment-form.component.scss'],
})
export class AppointmentFormComponent implements OnInit {
  private readonly fb        = inject(FormBuilder);
  private readonly router    = inject(Router);
  private readonly apptSvc   = inject(AppointmentsService);
  private readonly availSvc  = inject(AvailabilityService);
  private readonly svcSvc    = inject(ServicesCatalogService);
  private readonly profSvc   = inject(ProfessionalsService);
  private readonly toast     = inject(MessageService);

  readonly professionals = signal<ProfessionalDto[]>([]);
  readonly services      = signal<{ id: string; name: string; duration_minutes: number }[]>([]);
  readonly slots         = signal<AvailabilitySlot[]>([]);
  readonly loadingSlots  = signal(false);
  readonly saving        = signal(false);

  readonly form = this.fb.group({
    professional_id: ['', Validators.required],
    service_id:      ['', Validators.required],
    date:            [null as Date | null, Validators.required],
    slot_start_at:   ['', Validators.required],
    notes:           [''],
  });

  readonly today = new Date();

  ngOnInit() { this.loadProfessionals(); }

  async loadProfessionals() {
    const { items } = await this.profSvc.list({ includeInactive: false });
    this.professionals.set(items);
  }

  onProfessionalChange() {
    this.form.patchValue({ service_id: '', date: null, slot_start_at: '' });
    this.services.set([]);
    this.slots.set([]);
    const profId = this.form.value.professional_id;
    if (!profId) return;
    this.loadServicesForProfessional(profId);
  }

  async loadServicesForProfessional(profId: string) {
    const { items } = await this.svcSvc.list({ includeInactive: false });
    const linked    = await this.profSvc.getServices(profId);
    const linkedIds = new Set(linked.map((l: { service_id: string }) => l.service_id));
    this.services.set(items.filter((s: any) => linkedIds.has(s.id)));
  }

  async onDateChange(date: Date | null) {
    this.form.patchValue({ slot_start_at: '' });
    this.slots.set([]);
    const profId = this.form.value.professional_id;
    const svcId  = this.form.value.service_id;
    if (!date || !profId || !svcId) return;
    const dateStr = date.toISOString().slice(0, 10);
    this.loadingSlots.set(true);
    try {
      const s = await this.availSvc.getSlots(profId, svcId, dateStr);
      this.slots.set(s);
    } finally {
      this.loadingSlots.set(false);
    }
  }

  async submit() {
    if (this.form.invalid) return;
    this.saving.set(true);
    try {
      const v    = this.form.value;
      const appt = await this.apptSvc.create({
        professional_id: v.professional_id!,
        service_id:      v.service_id!,
        start_at:        v.slot_start_at!,
        notes:           v.notes || null,
      });
      this.toast.add({ severity: 'success', summary: 'Agendamento criado' });
      this.router.navigate(['/agenda/agendamentos', appt.id]);
    } catch (err: any) {
      const code = err?.error?.error?.code ?? 'UNKNOWN';
      this.toast.add({ severity: 'error', summary: 'Erro', detail: code });
    } finally {
      this.saving.set(false);
    }
  }

  slotOptions() {
    return this.slots().map(s => ({
      label: new Date(s.start_at).toLocaleTimeString('pt-BR', {
        hour: '2-digit', minute: '2-digit', timeZone: 'America/Sao_Paulo',
      }),
      value: s.start_at,
    }));
  }
}
