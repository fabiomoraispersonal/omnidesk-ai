// Spec 007 — ws-client unit tests focusing on backoff schedule + send queue + replay.

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { WsClient } from '../src/api/ws-client';

class MockWebSocket {
  public static last: MockWebSocket | null = null;
  public static instances: MockWebSocket[] = [];
  public readyState = 0;
  public sent: string[] = [];
  private listeners: Record<string, ((ev: any) => void)[]> = {};

  static OPEN = 1;
  static CLOSED = 3;

  constructor(public readonly url: string) {
    MockWebSocket.last = this;
    MockWebSocket.instances.push(this);
  }

  addEventListener(event: string, cb: (ev: any) => void): void {
    (this.listeners[event] ??= []).push(cb);
  }

  send(data: string): void { this.sent.push(data); }

  open(): void {
    this.readyState = MockWebSocket.OPEN;
    this.fire('open', {});
  }

  message(payload: object): void {
    this.fire('message', { data: JSON.stringify(payload) });
  }

  close(code = 1006): void {
    this.readyState = MockWebSocket.CLOSED;
    this.fire('close', { code, reason: '' });
  }

  private fire(event: string, ev: any): void {
    for (const cb of this.listeners[event] ?? []) cb(ev);
  }
}

describe('WsClient', () => {
  beforeEach(() => {
    MockWebSocket.last = null;
    MockWebSocket.instances = [];
    vi.useFakeTimers();
    (globalThis as any).WebSocket = MockWebSocket;
  });

  afterEach(() => { vi.useRealTimers(); });

  it('drains queued messages on open', () => {
    const onMessage = vi.fn();
    const client = new WsClient({ url: 'ws://test', onMessage });
    client.connect();
    client.enqueueMessageSend({ client_message_id: 'm1', content: 'olá' });
    expect(client.pendingCount()).toBe(1);

    MockWebSocket.last!.open();
    expect(MockWebSocket.last!.sent[0]).toContain('"type":"message.send"');
    expect(MockWebSocket.last!.sent[0]).toContain('"content":"olá"');
  });

  it('replays from last_message_id on reconnect', () => {
    const onMessage = vi.fn();
    let lastId = 'msg-42';
    const client = new WsClient({
      url: 'ws://test',
      onMessage,
      getLastMessageId: () => lastId,
    });
    client.connect();
    MockWebSocket.last!.open();

    expect(MockWebSocket.last!.sent.some((s) => s.includes('messages.replay'))).toBe(true);
    expect(MockWebSocket.last!.sent.some((s) => s.includes('msg-42'))).toBe(true);
  });

  it('schedules reconnect with exponential backoff after a close', () => {
    const onMessage = vi.fn();
    const client = new WsClient({ url: 'ws://test', onMessage });
    client.connect();
    MockWebSocket.last!.open();
    MockWebSocket.last!.close();

    // First backoff is 1s + jitter (≤ 25%). Advance 2s → reconnect attempt fires.
    vi.advanceTimersByTime(2_000);
    expect(MockWebSocket.instances.length).toBeGreaterThanOrEqual(2);
  });

  it('responds to ping with pong', () => {
    const onMessage = vi.fn();
    const client = new WsClient({ url: 'ws://test', onMessage });
    client.connect();
    MockWebSocket.last!.open();
    MockWebSocket.last!.message({ type: 'ping', ts: '2026-01-01T00:00:00Z' });

    expect(MockWebSocket.last!.sent.some((s) => s.includes('"type":"pong"'))).toBe(true);
  });
});
