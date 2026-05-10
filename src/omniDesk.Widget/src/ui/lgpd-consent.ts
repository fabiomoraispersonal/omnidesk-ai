// Spec 007 — LGPD consent block. Visible until the visitor checks the box; emits a
// `consent-granted` custom event upward when accepted.

export interface LgpdConfig {
  text?: string | null;
  url?: string | null;
}

export function createLgpdConsent(cfg: LgpdConfig, onConsent: () => void): HTMLElement {
  const wrapper = document.createElement('div');
  wrapper.className = 'lgpd';

  const id = `lgpd-${Math.random().toString(36).slice(2, 8)}`;
  const text = cfg.text?.trim()
    ?? 'Ao continuar, você concorda com nossa política de privacidade e tratamento de dados (LGPD).';

  const link = cfg.url
    ? `<a href="${escapeAttr(cfg.url)}" target="_blank" rel="noopener noreferrer">Ver política</a>`
    : '';

  wrapper.innerHTML = `
    <label for="${id}">
      <input type="checkbox" id="${id}" />
      <span>${escapeHtml(text)} ${link}</span>
    </label>
  `;

  const checkbox = wrapper.querySelector('input') as HTMLInputElement;
  checkbox.addEventListener('change', () => {
    if (checkbox.checked) onConsent();
  });

  return wrapper;
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c] ?? c));
}
function escapeAttr(s: string): string { return escapeHtml(s); }
