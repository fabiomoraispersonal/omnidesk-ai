import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

interface VapidEnvelope { success: boolean; data: { vapid_public_key: string }; }

const PERMISSION_REQUESTED_KEY = 'omnidesk.push_permission_requested';

/**
 * Spec 010 US2 T061 — Web Push browser integration.
 *
 * Responsibilities:
 *   - Register the Service Worker (`/sw-notifications.js`).
 *   - Request permission at most once (FR-011): if denied, do not re-prompt.
 *   - Fetch the server's VAPID public key, call `pushManager.subscribe(...)`,
 *     and POST the subscription to /api/push/subscribe.
 *   - Listen for `postMessage` events from the SW (`{ type: 'navigate' }`) and
 *     route the SPA via the Angular Router.
 *
 * The service is idempotent: calling `register()` again with an existing valid
 * subscription is a no-op (the backend upserts by endpoint).
 */
@Injectable({ providedIn: 'root' })
export class WebPushService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly base = `${environment.apiUrl}/api/push`;

  private messageListenerAttached = false;

  /**
   * Idempotently registers the Service Worker, requests permission (only once),
   * fetches the VAPID public key, subscribes via PushManager, and POSTs the
   * subscription to the backend.
   *
   * Safe to call on app boot; bails out early if the browser doesn't support
   * Service Workers / Push, or if permission was previously denied.
   */
  async register(): Promise<void> {
    if (!('serviceWorker' in navigator) || !('PushManager' in window)) return;

    let registration: ServiceWorkerRegistration;
    try {
      registration = await navigator.serviceWorker.register('/sw-notifications.js', { scope: '/' });
    } catch {
      return; // SW registration failed (insecure context, etc.) — silently bail.
    }

    this.attachMessageListener();

    // Determine permission state without re-prompting.
    const perm = Notification.permission;
    if (perm === 'denied') return;
    if (perm === 'default') {
      // Only ask once per browser install (FR-011).
      if (localStorage.getItem(PERMISSION_REQUESTED_KEY) === 'true') return;
      localStorage.setItem(PERMISSION_REQUESTED_KEY, 'true');
      let result: NotificationPermission;
      try { result = await Notification.requestPermission(); }
      catch { return; }
      if (result !== 'granted') return;
    }

    // permission === 'granted' from here on.
    let vapidPublicKey: string;
    try {
      const env = await firstValueFrom(this.http.get<VapidEnvelope>(`${this.base}/vapid-public-key`));
      vapidPublicKey = env.data.vapid_public_key;
    } catch {
      return; // Server hasn't configured VAPID yet — try again on next boot.
    }

    let sub: PushSubscription;
    try {
      const existing = await registration.pushManager.getSubscription();
      sub = existing ?? await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: this.urlBase64ToUint8Array(vapidPublicKey),
      });
    } catch {
      return;
    }

    await this.sendSubscriptionToServer(sub);
  }

  /** Removes the local subscription and tells the backend. Used on logout. */
  async unregister(): Promise<void> {
    if (!('serviceWorker' in navigator)) return;
    const registration = await navigator.serviceWorker.getRegistration('/sw-notifications.js');
    if (!registration) return;
    const sub = await registration.pushManager.getSubscription();
    if (!sub) return;
    try {
      await firstValueFrom(this.http.request('DELETE', `${this.base}/unsubscribe`, {
        body: { endpoint: sub.endpoint },
      }));
    } catch { /* swallow */ }
    try { await sub.unsubscribe(); } catch { /* swallow */ }
  }

  private attachMessageListener(): void {
    if (this.messageListenerAttached) return;
    this.messageListenerAttached = true;
    navigator.serviceWorker.addEventListener('message', (e: MessageEvent) => {
      const data = e.data as { type?: string; url?: string };
      if (data?.type === 'navigate' && typeof data.url === 'string') {
        this.router.navigateByUrl(data.url).catch(() => { /* swallow */ });
      }
    });
  }

  private async sendSubscriptionToServer(sub: PushSubscription): Promise<void> {
    const json = sub.toJSON();
    if (!json.endpoint || !json.keys?.p256dh || !json.keys?.auth) return;
    try {
      await firstValueFrom(this.http.post(`${this.base}/subscribe`, {
        endpoint:  json.endpoint,
        p256dh:    json.keys.p256dh,
        auth:      json.keys.auth,
        user_agent: navigator.userAgent,
      }));
    } catch { /* swallow — best-effort */ }
  }

  /** Web Push standard helper: base64url → Uint8Array for `applicationServerKey`. */
  private urlBase64ToUint8Array(b64: string): Uint8Array {
    const padding = '='.repeat((4 - (b64.length % 4)) % 4);
    const base64 = (b64 + padding).replace(/-/g, '+').replace(/_/g, '/');
    const raw = atob(base64);
    const out = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; ++i) out[i] = raw.charCodeAt(i);
    return out;
  }
}
