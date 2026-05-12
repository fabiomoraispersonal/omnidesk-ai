// Spec 009 US2 — signal-driven tickets state with HTTP methods and WS event patching.
import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, map } from 'rxjs';
import { environment } from '../../../../../environments/environment';

// ---------------------------------------------------------------------------
// Shared types (exported so other features can import)
// ---------------------------------------------------------------------------

export interface SlaInfo {
  first_response_deadline?: string;
  resolution_deadline_effective?: string;
  first_response_at?: string;
  paused_minutes?: number;
  status: 'ok' | 'warning' | 'breached';
}

export type TicketChannel = 'live_chat' | 'whatsapp' | 'manual';
export type TicketStatus = 'new' | 'in_progress' | 'waiting_client' | 'resolved' | 'cancelled';
export type TicketPriority = 'low' | 'normal' | 'high' | 'urgent';

export interface TicketSummary {
  id: string;
  protocol: string;
  channel: TicketChannel;
  status: TicketStatus;
  priority: TicketPriority;
  subject: string;
  department: { id: string; name: string } | null;
  attendant: { id: string; name: string } | null;
  contact: { id: string; name: string; email: string } | null;
  tags: string[];
  sla: SlaInfo | null;
  has_reminder_alert: boolean;
  created_at: string;
  updated_at: string;
}

export interface ConversationMessage {
  id: string;
  sender_type: 'visitor' | 'ai_agent' | 'attendant' | 'system';
  sender_id?: string | null;
  sender_name?: string | null;
  content: string | null;
  attachment_url?: string | null;
  sent_at: string;
}

export interface TicketNote {
  id: string;
  attendant_id: string;
  attendant_name: string;
  content: string;
  created_at: string;
}

export interface TicketDetail extends TicketSummary {
  conversation: ConversationMessage[];
  notes: TicketNote[];
  resolved_at: string | null;
  cancelled_at: string | null;
}

// ---------------------------------------------------------------------------
// WS event types
// ---------------------------------------------------------------------------

export type TicketEventType =
  | 'ticket.created'
  | 'ticket.assigned'
  | 'ticket.status_changed'
  | 'ticket.transferred'
  | 'ticket.sla_warning'
  | 'ticket.sla_breached';

export interface TicketWsEvent {
  type: TicketEventType;
  payload: TicketWsPayload;
  timestamp: string;
}

export type TicketWsPayload =
  | TicketCreatedPayload
  | TicketAssignedPayload
  | TicketStatusChangedPayload
  | TicketTransferredPayload
  | TicketSlaWarningPayload
  | TicketSlaBreadchedPayload;

export interface TicketCreatedPayload {
  ticket: TicketSummary;
}

export interface TicketAssignedPayload {
  ticket_id: string;
  attendant: { id: string; name: string } | null;
}

export interface TicketStatusChangedPayload {
  ticket_id: string;
  status: TicketStatus;
  sla?: SlaInfo | null;
}

export interface TicketTransferredPayload {
  ticket_id: string;
  attendant: { id: string; name: string } | null;
  department: { id: string; name: string } | null;
  sla?: SlaInfo | null;
}

export interface TicketSlaWarningPayload {
  ticket_id: string;
  sla_type: 'first_response' | 'resolution';
  sla: SlaInfo;
}

export interface TicketSlaBreadchedPayload {
  ticket_id: string;
  sla_type: 'first_response' | 'resolution';
  sla: SlaInfo;
}

// ---------------------------------------------------------------------------
// Filter types
// ---------------------------------------------------------------------------

export interface TicketFilters {
  department_id?: string | null;
  attendant_id?: string | null;
  channel?: TicketChannel | null;
  status?: TicketStatus | null;
  priority?: string | null;
  q?: string | null;
  page?: number;
  per_page?: number;
  include_terminal?: boolean;
}

// ---------------------------------------------------------------------------
// API envelope
// ---------------------------------------------------------------------------

interface Envelope<T> {
  success: true;
  data: T;
  meta?: { page: number; per_page: number; total: number };
}

// ---------------------------------------------------------------------------
// Service
// ---------------------------------------------------------------------------

@Injectable({ providedIn: 'root' })
export class TicketsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/tickets`;

  readonly tickets = signal<TicketSummary[]>([]);
  readonly selectedId = signal<string | null>(null);
  readonly loading = signal(false);

  async load(filters?: TicketFilters): Promise<void> {
    this.loading.set(true);
    try {
      const params: Record<string, string> = {};
      if (filters?.department_id) params['department_id'] = filters.department_id;
      if (filters?.attendant_id) params['attendant_id'] = filters.attendant_id;
      if (filters?.channel) params['channel'] = filters.channel;
      if (filters?.status) params['status'] = filters.status;
      if (filters?.priority) params['priority'] = filters.priority;
      if (filters?.q) params['q'] = filters.q;
      if (filters?.include_terminal) params['include_terminal'] = 'true';
      if (filters?.page != null) params['page'] = String(filters.page);
      if (filters?.per_page != null) params['per_page'] = String(filters.per_page);

      const data = await firstValueFrom(
        this.http
          .get<Envelope<TicketSummary[]>>(this.base, { params })
          .pipe(map((e) => e.data)),
      );
      this.tickets.set(data);
    } finally {
      this.loading.set(false);
    }
  }

  async getDetail(id: string): Promise<TicketDetail> {
    return firstValueFrom(
      this.http
        .get<Envelope<TicketDetail>>(`${this.base}/${id}`)
        .pipe(map((e) => e.data)),
    );
  }

  async patchStatus(id: string, status: TicketStatus, reason?: string): Promise<void> {
    const body: Record<string, unknown> = { status };
    if (reason) body['reason'] = reason;
    await firstValueFrom(
      this.http.patch<Envelope<unknown>>(`${this.base}/${id}/status`, body),
    );
    this.tickets.update((list) =>
      list.map((t) => (t.id === id ? { ...t, status } : t)),
    );
  }

  async resolve(id: string, note?: string): Promise<void> {
    const body: Record<string, unknown> = {};
    if (note) body['note'] = note;
    await firstValueFrom(
      this.http.post<Envelope<unknown>>(`${this.base}/${id}/resolve`, body),
    );
    this.tickets.update((list) =>
      list.map((t) => (t.id === id ? { ...t, status: 'resolved' } : t)),
    );
  }

  async cancel(id: string, reason?: string): Promise<void> {
    const body: Record<string, unknown> = {};
    if (reason) body['reason'] = reason;
    await firstValueFrom(
      this.http.post<Envelope<unknown>>(`${this.base}/${id}/cancel`, body),
    );
    this.tickets.update((list) =>
      list.map((t) => (t.id === id ? { ...t, status: 'cancelled' } : t)),
    );
  }

  async transfer(
    id: string,
    payload: {
      target_type: 'attendant' | 'department';
      target_attendant_id?: string | null;
      target_department_id?: string | null;
      note?: string | null;
    },
  ): Promise<void> {
    await firstValueFrom(
      this.http.post<Envelope<unknown>>(`${this.base}/${id}/transfer`, payload),
    );
    // Optimistic: remove from current kanban view (transferred tickets may belong to another dept)
    this.tickets.update((list) => list.filter((t) => t.id !== id));
  }

  /** Patch the tickets signal based on incoming WS events. */
  applyWsEvent(event: TicketWsEvent): void {
    switch (event.type) {
      case 'ticket.created': {
        const payload = event.payload as TicketCreatedPayload;
        // Add only if not already present
        this.tickets.update((list) => {
          if (list.some((t) => t.id === payload.ticket.id)) return list;
          return [payload.ticket, ...list];
        });
        break;
      }
      case 'ticket.assigned': {
        const payload = event.payload as TicketAssignedPayload;
        this.tickets.update((list) =>
          list.map((t) =>
            t.id === payload.ticket_id ? { ...t, attendant: payload.attendant } : t,
          ),
        );
        break;
      }
      case 'ticket.status_changed': {
        const payload = event.payload as TicketStatusChangedPayload;
        this.tickets.update((list) =>
          list.map((t) =>
            t.id === payload.ticket_id
              ? { ...t, status: payload.status, ...(payload.sla ? { sla: payload.sla } : {}) }
              : t,
          ),
        );
        break;
      }
      case 'ticket.transferred': {
        const payload = event.payload as TicketTransferredPayload;
        this.tickets.update((list) =>
          list.map((t) =>
            t.id === payload.ticket_id
              ? {
                  ...t,
                  attendant: payload.attendant,
                  department: payload.department ?? t.department,
                  ...(payload.sla ? { sla: payload.sla } : {}),
                }
              : t,
          ),
        );
        break;
      }
      case 'ticket.sla_warning': {
        const payload = event.payload as TicketSlaWarningPayload;
        this.tickets.update((list) =>
          list.map((t) =>
            t.id === payload.ticket_id ? { ...t, sla: payload.sla } : t,
          ),
        );
        break;
      }
      case 'ticket.sla_breached': {
        const payload = event.payload as TicketSlaBreadchedPayload;
        this.tickets.update((list) =>
          list.map((t) =>
            t.id === payload.ticket_id ? { ...t, sla: payload.sla } : t,
          ),
        );
        break;
      }
    }
  }
}
