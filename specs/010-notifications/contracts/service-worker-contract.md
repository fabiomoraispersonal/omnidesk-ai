# Contract: Service Worker (Web Push)

**Spec**: 010-notifications
**File**: `src/omniDesk.Crm/src/sw-notifications.js`
**Scope**: `/` (root of the CRM origin)
**Lifecycle**: registered once on CRM boot by `WebPushService.register()`.

This contract documents the events the Service Worker handles, the expected push payload shape, and the click navigation flow.

---

## Push payload shape (server → SW)

Sent from `WebPushDispatcher.SendAsync`. The body is encrypted by the WebPush library; this is the **decrypted JSON** the SW sees in the `push` event.

```json
{
  "title": "Nova mensagem — TK-20260512-00042",
  "body": "João Silva: Olá, preciso confirmar o agendamento de quinta.",
  "icon": "/icon-192.png",
  "badge": "/badge-72.png",
  "tag": "ticket-ab12...",
  "data": {
    "url": "/tickets/ab12-...",
    "notification_id": "9d7c6c7e-...",
    "event_type": "ticket.new_message"
  }
}
```

**Field constraints**:

- `title`: ≤ 64 chars (server-truncated).
- `body`: ≤ 120 chars (server-truncated).
- `tag`: stable per-entity (e.g., `ticket-{id}`) so OS replaces stacked notifications for the same ticket rather than piling them up.
- `data.url`: relative path; SW combines with `location.origin` on click.
- `data.notification_id`: matches the row id in `notifications` so analytics could correlate later.

---

## SW event handlers

### `install`

```js
self.addEventListener('install', () => self.skipWaiting());
```

Activate immediately on registration; no asset precaching (this SW does not cache).

### `activate`

```js
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));
```

Take control of all open tabs.

### `push`

```js
self.addEventListener('push', (event) => {
  const payload = event.data ? event.data.json() : {};
  const title = payload.title || 'OmniDesk';
  const options = {
    body:    payload.body || '',
    icon:    payload.icon || '/icon-192.png',
    badge:   payload.badge || '/badge-72.png',
    tag:     payload.tag,
    data:    payload.data || {},
  };
  event.waitUntil(self.registration.showNotification(title, options));
});
```

### `notificationclick`

```js
self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const url = (event.notification.data && event.notification.data.url) || '/';
  event.waitUntil((async () => {
    const all = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const target = all.find((c) => c.url.startsWith(self.location.origin));
    if (target) {
      target.focus();
      target.postMessage({ type: 'navigate', url });
    } else {
      self.clients.openWindow(self.location.origin + url);
    }
  })());
});
```

The `postMessage` is consumed by `web-push.service.ts` in the Angular CRM, which then uses `Router.navigateByUrl(url)`.

---

## Frontend Service Worker registration

In `core/services/web-push.service.ts`:

```ts
async register(): Promise<void> {
  if (!('serviceWorker' in navigator) || !('PushManager' in window)) return;
  const reg = await navigator.serviceWorker.register('/sw-notifications.js', { scope: '/' });
  // Listen for SW messages to navigate within the SPA.
  navigator.serviceWorker.addEventListener('message', (e) => {
    if (e.data?.type === 'navigate' && typeof e.data.url === 'string') {
      this.router.navigateByUrl(e.data.url);
    }
  });
}
```

---

## Permission flow

```text
1. CRM boot → check Notification.permission
   - "granted"   → fetch subscription, POST /api/push/subscribe
   - "denied"    → skip; show inert link in Preferences to open browser settings
   - "default"   → request permission ONCE at first login
2. On permission grant:
   - registration.pushManager.subscribe({
       userVisibleOnly: true,
       applicationServerKey: <fetched VAPID public key>
     })
   - POST /api/push/subscribe with the returned subscription
3. On permission denial:
   - Persist "permission_requested = true" in localStorage to avoid re-prompting.
```

The "request once" guarantee (FR-011) is enforced by the localStorage flag, not by checking `Notification.permission` directly (which could be `"default"` again after the user clears site data).

---

## Cloudflare deployment

The SW must be served from the root scope (`/sw-notifications.js`). The CRM's `_headers` file declares:

```
/sw-notifications.js
  Service-Worker-Allowed: /
  Cache-Control: no-cache, no-store, must-revalidate
```

(The `Service-Worker-Allowed: /` header lets the SW claim a broader scope than its file location — required if the file is served from a subdir during builds.)
