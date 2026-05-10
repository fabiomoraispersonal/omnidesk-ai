// Spec 007 US4 — conversation-store unit tests covering all 3 status transitions.

import { describe, it, expect, beforeEach } from 'vitest';
import { conversationStore } from '../src/state/conversation-store';

describe('conversationStore', () => {
  beforeEach(() => window.localStorage.clear());

  it('persists an open conversation id and reads it back', () => {
    conversationStore.setActive('conv-open-1', 'open');
    expect(conversationStore.getActive()).toEqual({ id: 'conv-open-1', status: 'open' });
  });

  it('clears the persisted id when status is resolved', () => {
    conversationStore.setActive('conv-1', 'open');
    expect(conversationStore.getActive()).not.toBeNull();
    conversationStore.setActive('conv-1', 'resolved');
    expect(conversationStore.getActive()).toBeNull();
  });

  it('clears the persisted id when status is abandoned', () => {
    conversationStore.setActive('conv-2', 'open');
    conversationStore.setActive('conv-2', 'abandoned');
    expect(conversationStore.getActive()).toBeNull();
  });

  it('clear() wipes both id and last_message_id', () => {
    conversationStore.setActive('conv-3', 'open');
    conversationStore.setLastMessageId('msg-99');
    conversationStore.clear();
    expect(conversationStore.getActive()).toBeNull();
    expect(conversationStore.getLastMessageId()).toBeNull();
  });

  it('setLastMessageId + getLastMessageId round-trip', () => {
    conversationStore.setLastMessageId('msg-100');
    expect(conversationStore.getLastMessageId()).toBe('msg-100');
  });
});
