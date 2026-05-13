import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { AppointmentsService, AppointmentDto } from './appointments.service';

/**
 * Spec 011 US3 (T105) — lists all pending_confirmation appointments.
 * Attendant can confirm each directly from this view.
 */
@Component({
  selector: 'app-pending-appointments',
  standalone: true,
  imports: [CommonModule, RouterModule, ButtonModule, CardModule, ToastModule],
  providers: [MessageService],
  templateUrl: './pending-appointments.component.html',
  styleUrls: ['./pending-appointments.component.scss'],
})
export class PendingAppointmentsComponent implements OnInit {
  private readonly svc: AppointmentsService;
  private readonly toast: MessageService;

  constructor(s: AppointmentsService, t: MessageService) {
    this.svc   = s;
    this.toast = t;
  }

  readonly items   = signal<AppointmentDto[]>([]);
  readonly loading = signal(false);
  readonly confirming = signal<string | null>(null);

  ngOnInit() { this.load(); }

  async load() {
    this.loading.set(true);
    try {
      const { items } = await this.svc.list({
        status:   'pending_confirmation',
        per_page: 100,
      });
      this.items.set(items);
    } finally {
      this.loading.set(false);
    }
  }

  async confirm(appt: AppointmentDto) {
    this.confirming.set(appt.id);
    try {
      await this.svc.confirm(appt.id);
      this.items.update(list => list.filter(a => a.id !== appt.id));
      this.toast.add({ severity: 'success', summary: 'Confirmado', detail: 'Agendamento confirmado com sucesso.' });
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Não foi possível confirmar.' });
    } finally {
      this.confirming.set(null);
    }
  }
}
