// Spec 007 — visitor-store unit tests (Vitest + happy-dom).

import { describe, it, expect, beforeEach } from 'vitest';
import { visitorStore } from '../src/state/visitor-store';

describe('visitorStore', () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  it('generates a UUID v4 on first visit and persists it', () => {
    const id = visitorStore.getOrCreate();
    expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i);
    expect(window.localStorage.getItem('omnidesk_visitor_id')).toEqual(id);
  });

  it('reuses the existing id on subsequent calls', () => {
    const first = visitorStore.getOrCreate();
    const second = visitorStore.getOrCreate();
    expect(second).toEqual(first);
  });

  it('regenerates when the stored value is not a UUID', () => {
    window.localStorage.setItem('omnidesk_visitor_id', 'not-a-uuid');
    const id = visitorStore.getOrCreate();
    expect(id).not.toEqual('not-a-uuid');
  });
});
