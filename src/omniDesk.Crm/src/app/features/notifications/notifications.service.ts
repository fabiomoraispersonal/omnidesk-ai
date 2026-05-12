import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface NotificationDto {
  id: string;
  event_type: string;
  title: string;
  body: string;
  entity_type: 'ticket' | 'conversation';
  entity_id: string;
  is_read: boolean;
  created_at: string;
}

interface ListEnvelope {
  success: boolean;
  data: NotificationDto[];
  meta: { page: number; per_page: number; total: number };
}

interface CountEnvelope { success: boolean; data: { count: number }; }
interface ReadEnvelope { success: boolean; data: { id: string; is_read: boolean }; }
interface ReadAllEnvelope { success: boolean; data: { marked: number }; }

/**
 * Spec 010 US1 (T045) — REST client + reactive state for the notification feed.
 *
 * Signals exposed:
 *   - `unreadCount` — live integer (also fed by WS via NotificationStreamService).
 *   - `items`      — most recently fetched page (newest first).
 *
 * Methods are awaitable and idempotent; callers handle errors via try/catch.
 */
@Injectable({ providedIn: 'root' })
export class NotificationsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/notifications`;

  readonly unreadCount = signal<number>(0);
  readonly items = signal<NotificationDto[]>([]);

  async fetchPage(page = 1, perPage = 20, unreadOnly = false): Promise<{ items: NotificationDto[]; total: number; }> {
    const params: Record<string, string> = {
      page: String(page),
      per_page: String(perPage),
    };
    if (unreadOnly) params['unread_only'] = 'true';

    const env = await firstValueFrom(
      this.http.get<ListEnvelope>(this.base, { params }),
    );
    if (page === 1) this.items.set(env.data);
    else this.items.update((curr) => [...curr, ...env.data]);
    return { items: env.data, total: env.meta.total };
  }

  async refreshUnreadCount(): Promise<number> {
    const env = await firstValueFrom(
      this.http.get<CountEnvelope>(`${this.base}/unread-count`),
    );
    const c = env.data.count;
    this.unreadCount.set(c);
    return c;
  }

  async markAsRead(id: string): Promise<void> {
    await firstValueFrom(this.http.patch<ReadEnvelope>(`${this.base}/${id}/read`, {}));
    this.items.update((arr) => arr.map((n) => (n.id === id ? { ...n, is_read: true } : n)));
    this.unreadCount.update((c) => Math.max(0, c - 1));
  }

  async markAllAsRead(): Promise<number> {
    const env = await firstValueFrom(
      this.http.post<ReadAllEnvelope>(`${this.base}/read-all`, {}),
    );
    this.items.update((arr) => arr.map((n) => ({ ...n, is_read: true })));
    this.unreadCount.set(0);
    return env.data.marked;
  }

  /** Called by NotificationStreamService when a `notification.new` WS event arrives. */
  prepend(notification: NotificationDto): void {
    this.items.update((arr) => [notification, ...arr]);
  }

  /** Called by NotificationStreamService when a `notification.unread_count` WS event arrives. */
  setUnreadCount(count: number): void {
    this.unreadCount.set(Math.min(count, 99));
  }
}
