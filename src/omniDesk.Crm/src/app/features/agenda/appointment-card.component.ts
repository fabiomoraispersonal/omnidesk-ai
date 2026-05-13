import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BadgeModule } from 'primeng/badge';
import { ButtonModule } from 'primeng/button';
import { AppointmentDto } from './appointments.service';

/**
 * Spec 011 US3 (T102) — compact card for one appointment.
 * Used in weekly-grid and appointments-list.
 */
@Component({
  selector: 'app-appointment-card',
  standalone: true,
  imports: [CommonModule, BadgeModule, ButtonModule],
  templateUrl: './appointment-card.component.html',
  styleUrls: ['./appointment-card.component.scss'],
})
export class AppointmentCardComponent {
  @Input({ required: true }) appointment!: AppointmentDto;
  @Output() clicked = new EventEmitter<AppointmentDto>();

  get statusSeverity(): 'success' | 'warning' | 'danger' | 'secondary' {
    switch (this.appointment.status) {
      case 'confirmed':            return 'success';
      case 'pending_confirmation': return 'warning';
      case 'cancelled':            return 'danger';
      case 'no_show':              return 'secondary';
    }
  }

  get statusLabel(): string {
    switch (this.appointment.status) {
      case 'confirmed':            return 'Confirmado';
      case 'pending_confirmation': return 'Pendente';
      case 'cancelled':            return 'Cancelado';
      case 'no_show':              return 'Não compareceu';
    }
  }

  get clientLabel(): string {
    return this.appointment.client_type === 'new_client' ? 'Novo' : 'Retorno';
  }

  get startTime(): string {
    return new Date(this.appointment.start_at).toLocaleTimeString('pt-BR', {
      hour: '2-digit', minute: '2-digit', timeZone: 'America/Sao_Paulo',
    });
  }
}
