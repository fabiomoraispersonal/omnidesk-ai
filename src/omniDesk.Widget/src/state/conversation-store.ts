// Spec 007 — active conversation id persisted in localStorage. Cleared on `resolved`/`abandoned`.

const KEY_ID = 'omnidesk_conversation_id';
const KEY_LAST_MSG = 'omnidesk_last_message_id';

export type ConversationStatus = 'open' | 'resolved' | 'abandoned';

export const conversationStore = {
  getActive(): { id: string; status: ConversationStatus } | null {
    try {
      const id = window.localStorage.getItem(KEY_ID);
      if (!id) return null;
      return { id, status: 'open' };
    } catch {
      return null;
    }
  },

  setActive(id: string, status: ConversationStatus = 'open'): void {
    try {
      if (status === 'open') window.localStorage.setItem(KEY_ID, id);
      else window.localStorage.removeItem(KEY_ID);
    } catch { /* ignore */ }
  },

  clear(): void {
    try {
      window.localStorage.removeItem(KEY_ID);
      window.localStorage.removeItem(KEY_LAST_MSG);
    } catch { /* ignore */ }
  },

  setLastMessageId(messageId: string): void {
    try { window.localStorage.setItem(KEY_LAST_MSG, messageId); } catch { /* ignore */ }
  },

  getLastMessageId(): string | null {
    try { return window.localStorage.getItem(KEY_LAST_MSG); } catch { return null; }
  },
};
