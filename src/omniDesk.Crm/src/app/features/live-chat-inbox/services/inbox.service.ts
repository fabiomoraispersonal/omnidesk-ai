import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { ConversationSummary, InboxMessage } from './inbox.types';

interface Envelope<T> {
  success: true;
  data: T;
}

/**
 * Spec 007 US3 — signal-driven inbox state. Backend lives at /api/conversations.
 *
 * - `conversations`: open conversations the attendant owns or can pick up.
 * - `selectedId`: currently focused conversation in the right pane.
 * - `messagesById`: lazy-loaded history per conversation.
 */
@Injectable({ providedIn: 'root' })
export class InboxService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/conversations`;

  readonly conversations = signal<ConversationSummary[]>([]);
  readonly selectedId = signal<string | null>(null);
  readonly messagesById = signal<Record<string, InboxMessage[]>>({});

  readonly selected = computed(() => {
    const id = this.selectedId();
    return id ? this.conversations().find((c) => c.id === id) ?? null : null;
  });

  readonly selectedMessages = computed(() => {
    const id = this.selectedId();
    return id ? this.messagesById()[id] ?? [] : [];
  });

  async load(): Promise<void> {
    const data = await firstValueFrom(
      this.http.get<Envelope<ConversationSummary[]>>(this.base).pipe(map((e) => e.data)),
    );
    this.conversations.set(data);
  }

  async select(id: string): Promise<void> {
    this.selectedId.set(id);
    if (this.messagesById()[id]) return;
    const data = await firstValueFrom(
      this.http
        .get<Envelope<{ messages: InboxMessage[] }>>(`${this.base}/${id}/livechat-messages`)
        .pipe(map((e) => e.data.messages)),
    );
    this.messagesById.update((m) => ({ ...m, [id]: data }));
  }

  async send(id: string, content: string): Promise<void> {
    await firstValueFrom(
      this.http.post<Envelope<{ message_id: string }>>(`${this.base}/${id}/livechat-messages`, { content }),
    );
    // Optimistic local echo — the WS feed will re-emit the same message.new shortly.
    this.messagesById.update((m) => {
      const existing = m[id] ?? [];
      return {
        ...m,
        [id]: [
          ...existing,
          {
            id: `local-${Date.now()}`,
            sender_type: 'attendant',
            sender_id: null,
            content_type: 'text',
            content,
            attachment_url: null,
            attachment_name: null,
            attachment_size_bytes: null,
            created_at: new Date().toISOString(),
          },
        ],
      };
    });
  }

  async resolve(id: string): Promise<void> {
    await firstValueFrom(this.http.post<Envelope<unknown>>(`${this.base}/${id}/resolve`, {}));
    this.conversations.update((list) => list.filter((c) => c.id !== id));
    this.messagesById.update((m) => {
      const next = { ...m };
      delete next[id];
      return next;
    });
    if (this.selectedId() === id) this.selectedId.set(null);
  }

  pushIncoming(conversationId: string, message: InboxMessage): void {
    this.messagesById.update((m) => {
      const existing = m[conversationId] ?? [];
      // De-dup local optimistic echoes that share the real message id.
      if (existing.some((x) => x.id === message.id)) return m;
      return { ...m, [conversationId]: [...existing, message] };
    });
  }

  removeOnResolved(id: string): void {
    this.conversations.update((list) => list.filter((c) => c.id !== id));
    if (this.selectedId() === id) this.selectedId.set(null);
  }
}
