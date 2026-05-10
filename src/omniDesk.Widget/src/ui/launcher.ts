// Spec 007 — Floating launcher button. Plain DOM (no shadow root here — the panel owns
// the shadow). Hosted directly inside the parent host element's shadow tree.

import type { WidgetPosition } from '../types';

export interface LauncherCallbacks {
  onClick: () => void;
}

export function createLauncher(position: WidgetPosition, callbacks: LauncherCallbacks): {
  el: HTMLButtonElement;
  setBadge: (count: number) => void;
} {
  const button = document.createElement('button');
  button.className = `launcher${position === 'bottom_left' ? ' left' : ''}`;
  button.setAttribute('aria-label', 'Abrir chat');
  button.innerHTML = `
    <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor"
         stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>
    </svg>
    <span class="badge" aria-live="polite"></span>
  `;
  button.addEventListener('click', () => callbacks.onClick());

  const badge = button.querySelector('.badge') as HTMLSpanElement;

  function setBadge(count: number): void {
    if (count <= 0) {
      badge.classList.remove('visible');
      badge.textContent = '';
    } else {
      badge.classList.add('visible');
      badge.textContent = count > 9 ? '9+' : String(count);
    }
  }

  return { el: button, setBadge };
}
