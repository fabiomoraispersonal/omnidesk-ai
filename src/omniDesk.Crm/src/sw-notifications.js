// Spec 010 US2 T060 — Service Worker for Web Push notifications.
// NOT an Angular Service Worker (no caching). Sole job: receive push events,
// show OS-level notifications, and route clicks back into the SPA.
//
// Registered by `WebPushService.register()` from the Angular app.
// See specs/010-notifications/contracts/service-worker-contract.md.

self.addEventListener('install', () => {
  // Activate immediately on first registration.
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  // Claim control of all open clients so push routing works for the current tabs.
  event.waitUntil(self.clients.claim());
});

self.addEventListener('push', (event) => {
  let payload = {};
  if (event.data) {
    try { payload = event.data.json(); } catch (_e) { payload = { title: 'OmniDesk', body: event.data.text() }; }
  }

  const title = payload.title || 'OmniDesk';
  const options = {
    body:  payload.body  || '',
    icon:  payload.icon  || '/icon-192.png',
    badge: payload.badge || '/badge-72.png',
    tag:   payload.tag,
    data:  payload.data  || {},
    // Keep notification visible until user dismisses (default false on most browsers).
    requireInteraction: false,
  };

  event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();

  const url = (event.notification.data && event.notification.data.url) || '/';
  const fullUrl = new URL(url, self.location.origin).href;

  event.waitUntil((async () => {
    const all = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    // Prefer a window already open on this origin.
    const target = all.find((c) => c.url.startsWith(self.location.origin));
    if (target) {
      try { await target.focus(); } catch (_e) { /* swallow */ }
      try {
        target.postMessage({ type: 'navigate', url });
      } catch (_e) { /* swallow */ }
      return;
    }
    await self.clients.openWindow(fullUrl);
  })());
});
