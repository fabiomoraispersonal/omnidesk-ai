import { Injectable, inject, signal } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { InboxService } from './inbox.service';
import { BrowserNotificationService } from './browser-notification.service';
import {
  CrmEvent,
  WaDeliveryStatus,
  WaSessionExpiredPayload,
  WaSessionExpiringPayload,
} from './inbox.types';
import { TicketWsEvent } from '../../tickets-kanban/services/tickets.service';

/** Estado local de status de delivery por message_id. */
export interface WaMessageStatusState {
  status: WaDeliveryStatus;
  errorCode?: string | null;
  errorMessage?: string | null;
  attachmentReady?: boolean;
  attachmentUrl?: string | null;
  timestamp: string;
}

/** Estado da janela 24h por conversation_id. */
export type WaSessionWindowState =
  | { status: 'active' }
  | { status: 'expiring'; expiresAt: string; minutesRemaining: number }
  | { status: 'expired'; expiredAt: string };

/** Spec 010 US1 — payload of `notification.new` event from the backend. */
export interface NotificationWsPayload {
  id: string;
  event_type: string;
  title: string;
  body: string;
  entity_type: 'ticket' | 'conversation';
  entity_id: string;
  created_at: string;
}

/**
 * Spec 007 US3 — singleton WebSocket subscription to /ws/crm. JWT lives in an
 * httpOnly cookie (Spec 002), so the browser sends it automatically — no token
 * shoehorning into the URL. Reconnects with linear backoff (10/20/30s).
 */
@Injectable({ providedIn: 'root' })
export class CrmWebSocketService {
  private readonly inbox = inject(InboxService);
  private readonly notify = inject(BrowserNotificationService);

  readonly connected = signal(false);

  // Spec 009 US2 — signal updated for every ticket WS event received.
  readonly ticketEvents = signal<TicketWsEvent | null>(null);

  // Spec 010 US1 — signal updated when a `notification.new` arrives over the WS.
  readonly notificationNew = signal<NotificationWsPayload | null>(null);

  // Spec 010 US1 — signal updated when the live unread counter changes.
  readonly notificationUnreadCount = signal<number | null>(null);

  // Spec 008 US3 — map message_id → status atual para renderizar ícones de delivery.
  readonly waMessageStatuses = signal<ReadonlyMap<string, WaMessageStatusState>>(new Map());

  // Spec 008 US4 — map conversation_id → estado da janela 24h.
  readonly waSessionWindows = signal<ReadonlyMap<string, WaSessionWindowState>>(new Map());

  private socket: WebSocket | null = null;
  private destroyed = false;
  private attempt = 0;

  connect(): void {
    if (this.socket || this.destroyed) return;
    const wsBase = environment.apiUrl.replace(/^http/, 'ws');
    this.socket = new WebSocket(`${wsBase.replace(/\/$/, '')}/ws/crm`);

    this.socket.addEventListener('open', () => {
      this.attempt = 0;
      this.connected.set(true);
    });
    this.socket.addEventListener('message', (ev) => this.handleEvent(ev.data as string));
    this.socket.addEventListener('close', () => {
      this.connected.set(false);
      this.scheduleReconnect();
    });
  }

  destroy(): void {
    this.destroyed = true;
    this.socket?.close(1000, 'destroy');
    this.socket = null;
  }

  private handleEvent(raw: string): void {
    let event: CrmEvent;
    try { event = JSON.parse(raw) as CrmEvent; }
    catch { return; }

    switch (event.type) {
      case 'ping':
        this.socket?.send(JSON.stringify({ type: 'pong', ts: new Date().toISOString() }));
        break;
      case 'chat.message_received':
        this.inbox.pushIncoming(event.payload.conversation_id, {
          id: event.payload.id,
          sender_type: event.payload.sender_type,
          sender_id: event.payload.sender_id,
          content_type: event.payload.content_type,
          content: event.payload.content,
          attachment_url: event.payload.attachment_url ?? null,
          attachment_name: event.payload.attachment_name ?? null,
          attachment_size_bytes: event.payload.attachment_size_bytes ?? null,
          created_at: event.payload.created_at,
        });
        // Spec 008 — mensagem do visitante reabre a janela 24h.
        if (event.payload.sender_type === 'visitor') {
          this.resetSessionWindow(event.payload.conversation_id);
        }
        break;
      case 'chat.new_conversation':
        // Refresh list so the new conversation shows up. Cheap query.
        void this.inbox.load();
        break;
      case 'chat.conversation_resolved':
        this.inbox.removeOnResolved(event.payload.conversation_id);
        break;
      case 'chat.browser_notify':
        this.notify.notify(event.payload.title, event.payload.body, event.payload.conversation_id);
        break;
      case 'wa.message_status':
        this.applyWaMessageStatus(event.payload);
        break;
      case 'wa.session_expiring':
        this.applyWaSessionExpiring(event.payload);
        break;
      case 'wa.session_expired':
        this.applyWaSessionExpired(event.payload);
        break;
      // Spec 009 US2 — ticket events forwarded to the ticketEvents signal.
      case 'ticket.created':
      case 'ticket.assigned':
      case 'ticket.status_changed':
      case 'ticket.transferred':
      case 'ticket.sla_warning':
      case 'ticket.sla_breached':
        this.ticketEvents.set(event as unknown as TicketWsEvent);
        break;
      // Spec 010 US1 — notification feed events for the bell.
      case 'notification.new':
        this.notificationNew.set(
          (event as unknown as { payload: NotificationWsPayload }).payload);
        break;
      case 'notification.unread_count': {
        const p = (event as unknown as { payload: { count: number } }).payload;
        if (typeof p?.count === 'number') this.notificationUnreadCount.set(p.count);
        break;
      }
    }
  }

  /**
   * Spec 010 US2 T058/T063 — send a typed client→server message. Used by ticket-detail
   * to emit <c>attendant.viewing_ticket</c> heartbeats. No-op when not connected.
   */
  send(message: object): void {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) return;
    try { this.socket.send(JSON.stringify(message)); } catch { /* swallow */ }
  }

  private applyWaMessageStatus(payload: { conversation_id: string; message_id: string; status: string;
      timestamp: string; error_code?: string | null; error_message?: string | null;
      attachment_ready?: boolean; attachment_url?: string | null }): void {
    const next = new Map(this.waMessageStatuses());
    next.set(payload.message_id, {
      status: payload.status as WaDeliveryStatus,
      errorCode: payload.error_code ?? null,
      errorMessage: payload.error_message ?? null,
      attachmentReady: !!payload.attachment_ready,
      attachmentUrl: payload.attachment_url ?? null,
      timestamp: payload.timestamp,
    });
    this.waMessageStatuses.set(next);
  }

  private applyWaSessionExpiring(payload: WaSessionExpiringPayload): void {
    const next = new Map(this.waSessionWindows());
    next.set(payload.conversation_id, {
      status: 'expiring',
      expiresAt: payload.expires_at,
      minutesRemaining: payload.minutes_remaining,
    });
    this.waSessionWindows.set(next);
  }

  private applyWaSessionExpired(payload: WaSessionExpiredPayload): void {
    const next = new Map(this.waSessionWindows());
    next.set(payload.conversation_id, {
      status: 'expired',
      expiredAt: payload.expired_at,
    });
    this.waSessionWindows.set(next);
  }

  /** Spec 008 — reabertura da janela quando o cliente envia nova mensagem. */
  resetSessionWindow(conversationId: string): void {
    const next = new Map(this.waSessionWindows());
    if (next.delete(conversationId)) this.waSessionWindows.set(next);
  }

  private scheduleReconnect(): void {
    if (this.destroyed) return;
    const delay = Math.min(30_000, 10_000 * Math.max(1, this.attempt));
    this.attempt += 1;
    this.socket = null;
    setTimeout(() => this.connect(), delay);
  }
}
