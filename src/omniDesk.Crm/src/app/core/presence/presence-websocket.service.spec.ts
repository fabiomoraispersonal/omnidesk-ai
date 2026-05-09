import { TestBed } from '@angular/core/testing';
import { PresenceWebsocketService, RealtimeEvent } from './presence-websocket.service';
import { TokenService } from '../services/token.service';

class FakeWebSocket {
  static instances: FakeWebSocket[] = [];
  static OPEN = 1;
  readyState = 0;
  url: string;
  onopen?: () => void;
  onmessage?: (ev: { data: string }) => void;
  onclose?: () => void;
  sent: string[] = [];
  listeners: Record<string, Array<() => void>> = {};

  constructor(url: string) {
    this.url = url;
    FakeWebSocket.instances.push(this);
  }

  open() {
    this.readyState = FakeWebSocket.OPEN;
    this.listeners['open']?.forEach(fn => fn());
    this.onopen?.();
  }

  receive(payload: object) {
    this.onmessage?.({ data: JSON.stringify(payload) });
  }

  send(s: string) { this.sent.push(s); }

  close() { this.readyState = 3; this.onclose?.(); }

  addEventListener(name: string, fn: () => void) {
    (this.listeners[name] ??= []).push(fn);
  }

  removeEventListener(name: string, fn: () => void) {
    this.listeners[name] = (this.listeners[name] ?? []).filter(f => f !== fn);
  }
}

describe('PresenceWebsocketService', () => {
  const tokenService = { getToken: () => 'fake.jwt' };
  let originalWs: typeof globalThis.WebSocket;

  beforeEach(() => {
    FakeWebSocket.instances.length = 0;
    originalWs = globalThis.WebSocket;
    (globalThis as any).WebSocket = FakeWebSocket;
    TestBed.configureTestingModule({
      providers: [{ provide: TokenService, useValue: tokenService }],
    });
  });

  afterEach(() => { (globalThis as any).WebSocket = originalWs; });

  it('subscribes only after socket opens', () => {
    const svc = TestBed.inject(PresenceWebsocketService);
    svc.connect();
    svc.subscribe(['attendant:self', 'tenant']);
    const fake = FakeWebSocket.instances[0];
    expect(fake.sent.length).toBe(0);
    fake.open();
    expect(fake.sent[0]).toContain('attendant:self');
  });

  it('dispatches incoming events to events$', (done) => {
    const svc = TestBed.inject(PresenceWebsocketService);
    svc.events$.subscribe((evt: RealtimeEvent) => {
      expect(evt.type).toBe('attendant.status_changed');
      done();
    });
    svc.connect();
    const fake = FakeWebSocket.instances[0];
    fake.open();
    fake.receive({ type: 'attendant.status_changed', payload: {}, timestamp: '', tenant_slug: 't' });
  });

  it('skips redundant subscriptions for already-subscribed channels', () => {
    const svc = TestBed.inject(PresenceWebsocketService);
    svc.connect();
    const fake = FakeWebSocket.instances[0];
    fake.open();
    svc.subscribe(['tenant']);
    svc.subscribe(['tenant']);
    expect(fake.sent.length).toBe(1);
  });
});
