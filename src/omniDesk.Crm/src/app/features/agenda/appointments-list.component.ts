import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { DropdownModule } from 'primeng/dropdown';
import { InputTextModule } from 'primeng/inputtext';
import { BadgeModule } from 'primeng/badge';
import { AppointmentsService, AppointmentDto } from './appointments.service';

const STATUS_OPTIONS = [
  { label: 'Todos', value: null },
  { label: 'Pendente',   value: 'pending_confirmation' },
  { label: 'Confirmado', value: 'confirmed' },
  { label: 'Cancelado',  value: 'cancelled' },
  { label: 'Não compareceu', value: 'no_show' },
];

/**
 * Spec 011 US3 (T104) — paginated table of appointments with filters.
 */
@Component({
  selector: 'app-appointments-list',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule, TableModule, ButtonModule,
            DropdownModule, InputTextModule, BadgeModule],
  templateUrl: './appointments-list.component.html',
  styleUrls: ['./appointments-list.component.scss'],
})
export class AppointmentsListComponent implements OnInit {
  private readonly svc: AppointmentsService;

  constructor(s: AppointmentsService) { this.svc = s; }

  readonly items   = signal<AppointmentDto[]>([]);
  readonly total   = signal(0);
  readonly loading = signal(false);
  readonly page    = signal(1);
  readonly perPage = 20;

  filterStatus     = '';
  filterProfId     = '';
  readonly statusOptions = STATUS_OPTIONS;

  ngOnInit() { this.load(); }

  async load() {
    this.loading.set(true);
    try {
      const { items, total } = await this.svc.list({
        status:          this.filterStatus   || undefined,
        professional_id: this.filterProfId   || undefined,
        page:            this.page(),
        per_page:        this.perPage,
      });
      this.items.set(items);
      this.total.set(total);
    } finally {
      this.loading.set(false);
    }
  }

  onPage(event: { first: number; rows: number }) {
    this.page.set(Math.floor(event.first / event.rows) + 1);
    this.load();
  }

  statusSeverity(status: string): 'success' | 'warning' | 'danger' | 'secondary' {
    switch (status) {
      case 'confirmed':            return 'success';
      case 'pending_confirmation': return 'warning';
      case 'cancelled':            return 'danger';
      default:                     return 'secondary';
    }
  }

  statusLabel(status: string): string {
    return STATUS_OPTIONS.find(o => o.value === status)?.label ?? status;
  }
}
