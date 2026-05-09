import { Injectable, OnDestroy, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { PresenceWebsocketService, RealtimeEvent } from '../../../core/presence/presence-websocket.service';

export interface QueuedTicket {
  ticketId: string;
  ticketNumber: number;
  departmentId: string;
  reason: 'NoAttendantsOnline' | 'AllAtCapacity' | 'OutsideBusinessHoursNoOneOnline';
  nextBusinessWindowStart: string | null;
  queuedAt: string;
}

export interface AssignedTicket {
  ticketId: string;
  ticketNumber: number;
  departmentId: string;
  attendantId: string;
  assignmentMethod: 'auto' | 'manual';
  assignedAt: string;
}

@Injectable({ providedIn: 'root' })
export class TicketQueueService implements OnDestroy {
  private readonly ws = inject(PresenceWebsocketService);
  private readonly _queues = signal<Record<string, QueuedTicket[]>>({});
  private readonly _assigned = signal<AssignedTicket[]>([]);
  private subscription: Subscription | null = null;

  readonly queues = computed(() => this._queues());
  readonly assigned = computed(() => this._assigned());

  start(departmentIds: string[]): void {
    this.ws.connect();
    this.ws.subscribe(['tenant', ...departmentIds.map(id => `dept:${id}`)]);
    this.subscription?.unsubscribe();
    this.subscription = this.ws.events$.subscribe(evt => this.handle(evt));
  }

  ngOnDestroy(): void {
    this.subscription?.unsubscribe();
  }

  private handle(evt: RealtimeEvent): void {
    if (evt.type === 'ticket.queued') {
      const p = evt.payload as Record<string, any>;
      const dept = p['department_id'] as string;
      const item: QueuedTicket = {
        ticketId: p['ticket_id'],
        ticketNumber: p['ticket_number'],
        departmentId: dept,
        reason: p['reason'],
        nextBusinessWindowStart: p['next_business_window_start'] ?? null,
        queuedAt: p['queued_at'],
      };
      this._queues.update(state => ({ ...state, [dept]: [...(state[dept] ?? []), item] }));
      return;
    }
    if (evt.type === 'ticket.assigned') {
      const p = evt.payload as Record<string, any>;
      const ticketId = p['ticket_id'] as string;
      const dept = p['department_id'] as string;
      this._queues.update(state => ({
        ...state,
        [dept]: (state[dept] ?? []).filter(q => q.ticketId !== ticketId),
      }));
      this._assigned.update(list => [...list, {
        ticketId,
        ticketNumber: p['ticket_number'] ?? 0,
        departmentId: dept,
        attendantId: p['attendant_id'],
        assignmentMethod: p['assignment_method'],
        assignedAt: p['assigned_at'],
      }]);
    }
  }
}
