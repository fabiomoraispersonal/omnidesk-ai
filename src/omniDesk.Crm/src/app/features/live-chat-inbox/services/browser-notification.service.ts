import { Injectable, inject } from '@angular/core';
import { InboxService } from './inbox.service';

/**
 * Spec 007 US3 — Browser Notification wrapper. Silent when:
 *   1. The user denied permission, OR
 *   2. The CRM tab is visible AND the target conversation is currently selected
 *      (no point pinging the user about something they're already looking at).
 */
@Injectable({ providedIn: 'root' })
export class BrowserNotificationService {
  private readonly inbox = inject(InboxService);
  private permissionRequested = false;

  async requestPermission(): Promise<NotificationPermission> {
    if (typeof Notification === 'undefined') return 'denied';
    if (Notification.permission !== 'default') return Notification.permission;
    if (this.permissionRequested) return Notification.permission;
    this.permissionRequested = true;
    try { return await Notification.requestPermission(); }
    catch { return Notification.permission; }
  }

  notify(title: string, body: string, conversationId: string): void {
    if (typeof Notification === 'undefined') return;
    if (Notification.permission !== 'granted') return;

    const focused = typeof document !== 'undefined'
      && document.visibilityState === 'visible'
      && this.inbox.selectedId() === conversationId;
    if (focused) return;

    try {
      const n = new Notification(title, { body, tag: `omnidesk-${conversationId}` });
      n.onclick = () => {
        window.focus();
        void this.inbox.select(conversationId);
        n.close();
      };
    } catch {
      // ignore — some browsers reject Notification() outside of secure contexts
    }
  }
}
