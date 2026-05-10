// Spec 007 — WebSocket wrapper with exponential backoff reconnection and message queue
// drain on reconnect (research §R6). Replays missed messages via `messages.replay` once
// the socket is open and a `last_message_id` is known.

import type { MessageSendPayload, WsEvent } from '../types';
import { MessageQueue } from '../state/message-queue';

export interface WsClientOptions {
  url: string;
  // Called every time a message arrives from the server (after JSON parse).
  onMessage: (event: WsEvent) => void;
  onOpen?: () => void;
  onClose?: (code: number, reason: string) => void;
  // When supplied and non-null, the client emits `messages.replay` on every (re)open
  // so the server can backfill anything posted since the given message id.
  getLastMessageId?: () => string | null;
}

const BACKOFF_SCHEDULE_MS = [1_000, 2_000, 4_000, 8_000, 16_000, 30_000];

export class WsClient {
  private socket: WebSocket | null = null;
  private attempt = 0;
  private destroyed = false;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly queue = new MessageQueue();

  constructor(private readonly opts: WsClientOptions) {}

  connect(): void {
    if (this.destroyed) return;
    const ws = new WebSocket(this.opts.url);
    this.socket = ws;

    ws.addEventListener('open', () => {
      this.attempt = 0;
      this.opts.onOpen?.();
      this.replayIfNeeded();
      void this.drainQueue();
    });

    ws.addEventListener('message', (ev) => {
      const data = typeof ev.data === 'string' ? ev.data : '';
      try {
        const parsed = JSON.parse(data) as WsEvent;
        if (parsed.type === 'ping') {
          this.send({ type: 'pong', ts: new Date().toISOString() });
          return;
        }
        this.opts.onMessage(parsed);
      } catch {
        // Drop malformed frames silently.
      }
    });

    ws.addEventListener('close', (ev) => {
      this.opts.onClose?.(ev.code, ev.reason);
      this.scheduleReconnect();
    });

    ws.addEventListener('error', () => {
      // The 'close' handler will fire next; let it manage reconnect.
    });
  }

  // Outbound: queues for delivery. Called by UI to send `message.send` etc.
  enqueueMessageSend(payload: MessageSendPayload): void {
    if (this.isOpen()) {
      this.send({ type: 'message.send', payload });
    } else {
      this.queue.enqueue(payload);
    }
  }

  // Direct send for non-queueable events (typing, read, replay).
  send(envelope: object): void {
    if (!this.isOpen()) return;
    this.socket?.send(JSON.stringify(envelope));
  }

  destroy(): void {
    this.destroyed = true;
    if (this.reconnectTimer !== null) clearTimeout(this.reconnectTimer);
    this.reconnectTimer = null;
    this.socket?.close(1000, 'destroy');
    this.socket = null;
  }

  isOpen(): boolean {
    return this.socket !== null && this.socket.readyState === WebSocket.OPEN;
  }

  pendingCount(): number {
    return this.queue.size();
  }

  private async drainQueue(): Promise<void> {
    if (!this.isOpen()) return;
    await this.queue.flush((payload) => {
      this.send({ type: 'message.send', payload });
    });
  }

  private replayIfNeeded(): void {
    const since = this.opts.getLastMessageId?.() ?? null;
    if (!since) return;
    this.send({ type: 'messages.replay', payload: { since_message_id: since } });
  }

  private scheduleReconnect(): void {
    if (this.destroyed) return;
    const idx = Math.min(this.attempt, BACKOFF_SCHEDULE_MS.length - 1);
    const base = BACKOFF_SCHEDULE_MS[idx]!;
    const jitter = Math.random() * 0.25 * base;
    const delay = base + jitter;
    this.attempt += 1;
    this.reconnectTimer = setTimeout(() => this.connect(), delay);
  }
}
