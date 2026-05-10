// Spec 007 — in-memory FIFO buffering messages typed during a transient WS disconnect.
// Drained by ws-client when the connection re-opens (R6).

import type { MessageSendPayload } from '../types';

export class MessageQueue {
  private items: MessageSendPayload[] = [];

  enqueue(payload: MessageSendPayload): void {
    this.items.push(payload);
  }

  peek(): readonly MessageSendPayload[] {
    return this.items.slice();
  }

  size(): number {
    return this.items.length;
  }

  isEmpty(): boolean {
    return this.items.length === 0;
  }

  // Pulls everything currently buffered and forwards it via `send`.
  // If `send` throws synchronously, the message is left at the head of the queue.
  async flush(send: (payload: MessageSendPayload) => Promise<void> | void): Promise<void> {
    while (this.items.length > 0) {
      const next = this.items[0]!;
      await send(next);
      this.items.shift();
    }
  }

  clear(): void {
    this.items = [];
  }
}
