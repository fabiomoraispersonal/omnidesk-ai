import { Injectable, effect, inject } from '@angular/core';
import { NotificationsService } from '../../features/notifications/notifications.service';
import { CrmWebSocketService } from '../../features/live-chat-inbox/services/crm-websocket.service';

/**
 * Spec 010 US1 (T046) — bridges the singleton CrmWebSocketService (Spec 007) to the
 * NotificationsService signals, so the bell and list update in real time.
 *
 * No own WebSocket connection — piggybacks on the existing /ws/crm singleton.
 * On reconnect, the unread count is refreshed via REST (research §R15).
 */
@Injectable({ providedIn: 'root' })
export class NotificationStreamService {
  private readonly notifications = inject(NotificationsService);
  private readonly ws = inject(CrmWebSocketService);

  private started = false;
  private lastConnected = false;

  start(): void {
    if (this.started) return;
    this.started = true;

    this.ws.connect();

    // New notification → prepend + bump local counter (server also pushes count separately).
    effect(() => {
      const incoming = this.ws.notificationNew();
      if (!incoming) return;
      this.notifications.prepend(incoming);
      this.notifications.unreadCount.update((c) => Math.min(c + 1, 99));
    });

    // Authoritative unread count from server.
    effect(() => {
      const count = this.ws.notificationUnreadCount();
      if (count == null) return;
      this.notifications.setUnreadCount(count);
    });

    // Reconcile on reconnect (events while disconnected are not replayed).
    effect(() => {
      const connected = this.ws.connected();
      if (connected && !this.lastConnected) {
        this.notifications.refreshUnreadCount().catch(() => { /* tolerate */ });
      }
      this.lastConnected = connected;
    });
  }
}
