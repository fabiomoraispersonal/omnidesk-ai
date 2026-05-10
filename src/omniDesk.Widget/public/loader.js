// Spec 007 — Widget loader. Sites embed a one-liner pointing at this file. The build
// pipeline replaces __CDN_BASE_URL__ + __WIDGET_BUNDLE__ with the actual hashed bundle name.

(function () {
  if (typeof window === 'undefined') return;
  var cfg = window.OmniDeskConfig;
  if (!cfg || typeof cfg.token !== 'string' || !cfg.token) {
    console.warn('[OmniDesk] window.OmniDeskConfig.token is required.');
    return;
  }

  if (window.__OMNIDESK_LOADED__) return;
  window.__OMNIDESK_LOADED__ = true;

  var script = document.createElement('script');
  script.type = 'module';
  script.async = true;
  script.src = '__CDN_BASE_URL__/__WIDGET_BUNDLE__';
  script.onerror = function () {
    console.warn('[OmniDesk] Failed to load widget bundle from ' + script.src);
  };
  document.head.appendChild(script);
})();
