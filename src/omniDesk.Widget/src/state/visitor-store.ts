// Spec 007 — visitor identity persisted in localStorage.

import { generateUuid } from '../lib/crypto-uuid';

const STORAGE_KEY = 'omnidesk_visitor_id';

export const visitorStore = {
  getOrCreate(): string {
    let id: string | null = null;
    try { id = window.localStorage.getItem(STORAGE_KEY); } catch { /* private mode */ }

    if (id && isUuid(id)) return id;

    const fresh = generateUuid();
    try { window.localStorage.setItem(STORAGE_KEY, fresh); } catch { /* private mode */ }
    return fresh;
  },

  // Test-only convenience. Production callers always use getOrCreate.
  reset(): void {
    try { window.localStorage.removeItem(STORAGE_KEY); } catch { /* ignore */ }
  },
};

function isUuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}
