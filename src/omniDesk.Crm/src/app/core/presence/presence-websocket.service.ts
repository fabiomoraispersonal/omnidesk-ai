import { Injectable, OnDestroy, inject } from '@angular/core';
import { Subject } from 'rxjs';
import { TokenService } from '../services/token.service';
import { environment } from '../../../environments/environment';

export interface RealtimeEvent<T = unknown> {
  type: string;
  payload: T;
  timestamp: string;
  tenant_slug: string;
}

@Injectable({ providedIn: 'root' })
export class PresenceWebsocketService implements OnDestroy {
  private readonly tokenService = inject(TokenService);
  private socket: WebSocket | null = null;
  private readonly subscribed = new Set<string>();
  readonly events$ = new Subject<RealtimeEvent>();

  connect(): void {
    if (this.socket && this.socket.readyState === WebSocket.OPEN) return;
    const token = this.tokenService.getToken();
    if (!token) return;

    const base = (environment.apiUrl ?? '').replace(/^http/, 'ws');
    const url = `${base}/ws?token=${encodeURIComponent(token)}`;
    this.socket = new WebSocket(url);
    this.socket.onmessage = (msg) => {
      try {
        const event = JSON.parse(msg.data) as RealtimeEvent;
        this.events$.next(event);
      } catch { /* ignore malformed frame */ }
    };
    this.socket.onclose = () => { this.socket = null; this.subscribed.clear(); };
  }

  subscribe(channels: string[]): void {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
      // queue subscription until OPEN
      const open = () => {
        this.send({ type: 'subscribe', channels });
        channels.forEach(c => this.subscribed.add(c));
        this.socket?.removeEventListener('open', open);
      };
      this.socket?.addEventListener('open', open);
      return;
    }
    const fresh = channels.filter(c => !this.subscribed.has(c));
    if (fresh.length === 0) return;
    this.send({ type: 'subscribe', channels: fresh });
    fresh.forEach(c => this.subscribed.add(c));
  }

  disconnect(): void {
    this.socket?.close();
    this.socket = null;
    this.subscribed.clear();
  }

  ngOnDestroy(): void {
    this.disconnect();
    this.events$.complete();
  }

  private send(payload: object): void {
    this.socket?.send(JSON.stringify(payload));
  }
}
