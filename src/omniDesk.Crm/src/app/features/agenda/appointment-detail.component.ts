import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, ActivatedRoute } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { BadgeModule } from 'primeng/badge';
import { CardModule } from 'primeng/card';
import { DialogModule } from 'primeng/dialog';
import { InputTextareaModule } from 'primeng/inputtextarea';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { FormsModule } from '@angular/forms';
import { AppointmentsService, AppointmentDto } from './appointments.service';

const STATUS_LABELS: Record<string, string> = {
  confirmed:            'Confirmado',
  pending_confirmation: 'Pendente',
  cancelled:            'Cancelado',
  no_show:              'Não compareceu',
};

const CANCEL_BY_LABELS: Record<string, string> = {
  client:   'Cliente',
  attendant: 'Atendente',
  system:   'Sistema',
};

/**
 * Spec 011 US3 (T107) — appointment detail page with all lifecycle actions.
 */
@Component({
  selector: 'app-appointment-detail',
  standalone: true,
  imports: [CommonModule, RouterModule, ButtonModule, BadgeModule, CardModule,
            DialogModule, InputTextareaModule, ToastModule, FormsModule],
  providers: [MessageService],
  templateUrl: './appointment-detail.component.html',
  styleUrls: ['./appointment-detail.component.scss'],
})
export class AppointmentDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly svc   = inject(AppointmentsService);
  private readonly toast = inject(MessageService);

  readonly appt    = signal<AppointmentDto | null>(null);
  readonly loading = signal(false);
  readonly busy    = signal(false);

  showCancelDialog = false;
  cancelReason     = '';

  ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.load(id);
  }

  async load(id: string) {
    this.loading.set(true);
    try {
      this.appt.set(await this.svc.get(id));
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Agendamento não encontrado.' });
    } finally {
      this.loading.set(false);
    }
  }

  async confirm() {
    this.busy.set(true);
    try {
      const updated = await this.svc.confirm(this.appt()!.id);
      this.appt.set(updated);
      this.toast.add({ severity: 'success', summary: 'Confirmado' });
    } finally { this.busy.set(false); }
  }

  async cancelSubmit() {
    this.busy.set(true);
    try {
      const updated = await this.svc.cancel(this.appt()!.id, this.cancelReason || null);
      this.appt.set(updated);
      this.showCancelDialog = false;
      this.toast.add({ severity: 'success', summary: 'Cancelado' });
    } finally { this.busy.set(false); }
  }

  async noShow() {
    this.busy.set(true);
    try {
      const updated = await this.svc.noShow(this.appt()!.id);
      this.appt.set(updated);
      this.toast.add({ severity: 'success', summary: 'Marcado como não compareceu' });
    } finally { this.busy.set(false); }
  }

  async resendReminder() {
    this.busy.set(true);
    try {
      await this.svc.resendReminder(this.appt()!.id);
      await this.load(this.appt()!.id);
      this.toast.add({ severity: 'success', summary: 'Lembrete reenviado' });
    } finally { this.busy.set(false); }
  }

  statusLabel(s: string)   { return STATUS_LABELS[s] ?? s; }
  cancelByLabel(s: string) { return CANCEL_BY_LABELS[s] ?? s; }

  get isTerminal()  { return ['cancelled', 'no_show'].includes(this.appt()?.status ?? ''); }
  get isPending()   { return this.appt()?.status === 'pending_confirmation'; }
  get isConfirmed() { return this.appt()?.status === 'confirmed'; }
}
