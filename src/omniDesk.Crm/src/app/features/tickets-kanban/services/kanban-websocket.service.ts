// Spec 009 US2 — thin wrapper around CrmWebSocketService that surfaces
// ticket-domain events as a dedicated signal. Components in tickets-kanban
// should inject this service rather than CrmWebSocketService directly so
// that the dependency stays inside the feature boundary.
import { Injectable, Signal, inject } from '@angular/core';
import { CrmWebSocketService } from '../../live-chat-inbox/services/crm-websocket.service';
import { TicketWsEvent } from './tickets.service';

@Injectable({ providedIn: 'root' })
export class KanbanWebSocketService {
  private readonly ws = inject(CrmWebSocketService);

  /** Re-exposes the ticket event stream from the shared WS singleton. */
  readonly ticketEvents: Signal<TicketWsEvent | null> = this.ws.ticketEvents;

  /** Ensure the underlying WS connection is open. Safe to call multiple times. */
  connect(): void {
    this.ws.connect();
  }
}
