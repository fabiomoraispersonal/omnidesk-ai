import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { DropdownModule } from 'primeng/dropdown';
import { FormsModule } from '@angular/forms';
import { AppointmentsService, AppointmentDto } from './appointments.service';
import { ProfessionalsService, ProfessionalDto } from '../professionals/professionals.service';
import { AppointmentCardComponent } from './appointment-card.component';

const DAY_NAMES = ['Dom', 'Seg', 'Ter', 'Qua', 'Qui', 'Sex', 'Sáb'];

interface DayColumn { date: Date; label: string; dayOfWeek: number; }

/**
 * Spec 011 US3 (T103) — weekly calendar grid grouped by professional.
 * Displays 7 columns (Sun–Sat) with appointment cards.
 */
@Component({
  selector: 'app-weekly-grid',
  standalone: true,
  imports: [CommonModule, RouterModule, ButtonModule, DropdownModule, FormsModule, AppointmentCardComponent],
  templateUrl: './weekly-grid.component.html',
  styleUrls: ['./weekly-grid.component.scss'],
})
export class WeeklyGridComponent implements OnInit {
  private readonly appointmentsSvc: AppointmentsService;
  private readonly professionalsSvc: ProfessionalsService;
  private readonly router = inject(Router);

  constructor(a: AppointmentsService, p: ProfessionalsService) {
    this.appointmentsSvc  = a;
    this.professionalsSvc = p;
  }

  readonly today       = new Date();
  readonly weekStart   = signal<Date>(this.getMonday(new Date()));
  readonly appointments = signal<AppointmentDto[]>([]);
  readonly professionals = signal<ProfessionalDto[]>([]);
  readonly selectedProfId = signal<string | null>(null);
  readonly loading      = signal(false);

  readonly columns = computed<DayColumn[]>(() => {
    const start = this.weekStart();
    return Array.from({ length: 7 }, (_, i) => {
      const d   = new Date(start);
      d.setDate(d.getDate() + i);
      return { date: d, label: `${DAY_NAMES[d.getDay()]} ${d.getDate()}`, dayOfWeek: d.getDay() };
    });
  });

  readonly filteredProfessionals = computed(() =>
    this.selectedProfId()
      ? this.professionals().filter(p => p.id === this.selectedProfId())
      : this.professionals(),
  );

  ngOnInit() { this.loadAll(); }

  async loadAll() {
    this.loading.set(true);
    try {
      const [{ items: profs }, { items: appts }] = await Promise.all([
        this.professionalsSvc.list({ includeInactive: false }),
        this.appointmentsSvc.list({
          from: this.weekStart().toISOString(),
          to:   this.weekEnd().toISOString(),
          per_page: 200,
        }),
      ]);
      this.professionals.set(profs);
      this.appointments.set(appts);
    } finally {
      this.loading.set(false);
    }
  }

  getAppointmentsForProfDay(profId: string, col: DayColumn): AppointmentDto[] {
    return this.appointments().filter(a => {
      const d = new Date(a.start_at);
      return a.professional_id === profId && d.toDateString() === col.date.toDateString();
    }).sort((a, b) => a.start_at.localeCompare(b.start_at));
  }

  prevWeek() {
    const d = new Date(this.weekStart());
    d.setDate(d.getDate() - 7);
    this.weekStart.set(d);
    this.loadAll();
  }

  nextWeek() {
    const d = new Date(this.weekStart());
    d.setDate(d.getDate() + 7);
    this.weekStart.set(d);
    this.loadAll();
  }

  private getMonday(date: Date): Date {
    const d = new Date(date);
    const day = d.getDay();
    d.setDate(d.getDate() - ((day + 6) % 7));
    d.setHours(0, 0, 0, 0);
    return d;
  }

  private weekEnd(): Date {
    const d = new Date(this.weekStart());
    d.setDate(d.getDate() + 7);
    return d;
  }

  navigate(appt: AppointmentDto) {
    this.router.navigate(['/agenda/agendamentos', appt.id]);
  }

  profOptions() {
    return [
      { label: 'Todos os profissionais', value: null },
      ...this.professionals().map(p => ({ label: p.name, value: p.id })),
    ];
  }
}
