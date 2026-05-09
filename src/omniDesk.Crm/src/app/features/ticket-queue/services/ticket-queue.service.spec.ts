import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { TicketQueueService } from './ticket-queue.service';
import { PresenceWebsocketService, RealtimeEvent } from '../../../core/presence/presence-websocket.service';

describe('TicketQueueService', () => {
  const events$ = new Subject<RealtimeEvent>();
  const ws = {
    connect: jasmine.createSpy('connect'),
    subscribe: jasmine.createSpy('subscribe'),
    events$,
  };

  beforeEach(() => {
    ws.connect.calls.reset();
    ws.subscribe.calls.reset();
    TestBed.configureTestingModule({
      providers: [{ provide: PresenceWebsocketService, useValue: ws }],
    });
  });

  it('subscribes to tenant + each requested dept channel on start', () => {
    const svc = TestBed.inject(TicketQueueService);
    svc.start(['d1', 'd2']);
    expect(ws.subscribe).toHaveBeenCalledWith(['tenant', 'dept:d1', 'dept:d2']);
  });

  it('appends queued tickets to the per-dept list', () => {
    const svc = TestBed.inject(TicketQueueService);
    svc.start(['d1']);
    events$.next({
      type: 'ticket.queued',
      payload: {
        ticket_id: 't1', ticket_number: 1, department_id: 'd1',
        reason: 'NoAttendantsOnline', queued_at: '2026-05-08T10:00Z',
      },
      timestamp: '', tenant_slug: 'x',
    });
    expect(svc.queues()['d1'].length).toBe(1);
    expect(svc.queues()['d1'][0].ticketId).toBe('t1');
  });

  it('removes a ticket from queue when an assignment event arrives', () => {
    const svc = TestBed.inject(TicketQueueService);
    svc.start(['d1']);
    events$.next({
      type: 'ticket.queued',
      payload: { ticket_id: 't1', ticket_number: 1, department_id: 'd1', reason: 'NoAttendantsOnline', queued_at: '' },
      timestamp: '', tenant_slug: 'x',
    });
    events$.next({
      type: 'ticket.assigned',
      payload: { ticket_id: 't1', ticket_number: 1, department_id: 'd1', attendant_id: 'a1', assignment_method: 'auto', assigned_at: '' },
      timestamp: '', tenant_slug: 'x',
    });
    expect(svc.queues()['d1'].length).toBe(0);
    expect(svc.assigned().length).toBe(1);
  });
});
