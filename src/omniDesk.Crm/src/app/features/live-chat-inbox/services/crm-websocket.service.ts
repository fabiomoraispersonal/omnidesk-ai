import { Injectable, inject, signal } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { InboxService } from './inbox.service';
import { BrowserNotificationService } from './browser-notification.service';
import { CrmEvent } from './inbox.types';

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
    }
  }

  private scheduleReconnect(): void {
    if (this.destroyed) return;
    const delay = Math.min(30_000, 10_000 * Math.max(1, this.attempt));
    this.attempt += 1;
    this.socket = null;
    setTimeout(() => this.connect(), delay);
  }
}
