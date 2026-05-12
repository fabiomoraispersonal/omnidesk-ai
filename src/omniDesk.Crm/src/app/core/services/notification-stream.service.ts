import { Injectable, inject, OnDestroy } from '@angular/core';
import { NotificationsService, NotificationDto } from '../../features/notifications/notifications.service';
import { TokenService } from './token.service';
import { environment } from '../../../environments/environment';

interface WsEnvelope<T = unknown> {
  type: string;
  payload: T;
  timestamp: string;
  tenant_slug: string;
}

/**
 * Spec 010 US1 (T046) — listens to /ws/crm for `notification.new` and
 * `notification.unread_count` events and routes them into NotificationsService signals.
 *
 * Reconnect: native WebSocket auto-reconnect not built-in; we implement a simple
 * exponential backoff + on reconnect we refresh the unread count via REST (research §R15).
 */
@Injectable({ providedIn: 'root' })
export class NotificationStreamService implements OnDestroy {
  private readonly notifications = inject(NotificationsService);
  private readonly token = inject(TokenService);

  private ws: WebSocket | null = null;
  private reconnectAttempts = 0;
  private destroyed = false;

  start(): void {
    this.destroyed = false;
    this.connect();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.ws?.close();
    this.ws = null;
  }

  private connect(): void {
    if (this.destroyed) return;
    const accessToken = this.token.getAccessToken();
    if (!accessToken) {
      // No token yet; try again after a short delay.
      this.scheduleReconnect();
      return;
    }

    const url = `${environment.wsUrl}/ws/crm?token=${encodeURIComponent(accessToken)}`;
    const ws = new WebSocket(url);
    this.ws = ws;

    ws.onopen = () => {
      this.reconnectAttempts = 0;
      // Reconcile state in case events were missed while disconnected.
      this.notifications.refreshUnreadCount().catch(() => { /* tolerate */ });
    };

    ws.onmessage = (event) => this.handleMessage(event.data);

    ws.onclose = () => {
      this.ws = null;
      this.scheduleReconnect();
    };

    ws.onerror = () => {
      // Let onclose handle the reconnect.
    };
  }

  private handleMessage(raw: unknown): void {
    if (typeof raw !== 'string') return;
    let parsed: WsEnvelope | null = null;
    try { parsed = JSON.parse(raw) as WsEnvelope; } catch { return; }
    if (!parsed || typeof parsed.type !== 'string') return;

    switch (parsed.type) {
      case 'notification.new': {
        const n = parsed.payload as NotificationDto;
        this.notifications.prepend(n);
        this.notifications.unreadCount.update((c) => Math.min(c + 1, 99));
        break;
      }
      case 'notification.unread_count': {
        const p = parsed.payload as { count: number };
        if (typeof p?.count === 'number') this.notifications.setUnreadCount(p.count);
        break;
      }
    }
  }

  private scheduleReconnect(): void {
    if (this.destroyed) return;
    this.reconnectAttempts = Math.min(this.reconnectAttempts + 1, 6);
    const delayMs = Math.min(1000 * 2 ** this.reconnectAttempts, 30_000);
    setTimeout(() => this.connect(), delayMs);
  }
}
