// Spec 009 US2 — Ticket detail state: loads full ticket + notes management.
import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, map } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import { TicketDetail, TicketNote, TicketsService } from '../../tickets-kanban/services/tickets.service';

interface Envelope<T> {
  success: true;
  data: T;
}

@Injectable({ providedIn: 'root' })
export class TicketDetailService {
  private readonly http = inject(HttpClient);
  private readonly ticketsService = inject(TicketsService);
  private readonly base = `${environment.apiUrl}/api/tickets`;

  readonly detail = signal<TicketDetail | null>(null);
  readonly loading = signal(false);

  async load(id: string): Promise<void> {
    this.loading.set(true);
    try {
      const data = await this.ticketsService.getDetail(id);
      this.detail.set(data);
    } finally {
      this.loading.set(false);
    }
  }

  async addNote(ticketId: string, content: string): Promise<TicketNote> {
    const note = await firstValueFrom(
      this.http
        .post<Envelope<TicketNote>>(`${this.base}/${ticketId}/notes`, { content })
        .pipe(map((e) => e.data)),
    );
    // Append locally so UI reflects immediately
    this.detail.update((d) => {
      if (!d) return d;
      return { ...d, notes: [...d.notes, note] };
    });
    return note;
  }
}
