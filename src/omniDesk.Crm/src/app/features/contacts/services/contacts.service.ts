// Spec 009 US6 — T148
import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';

export interface ContactSummary {
  id: string;
  name: string | null;
  email: string | null;
  phone: string | null;
  source_channels: string[];
  tickets_count: number;
  last_interaction_at: string | null;
  created_at: string;
  updated_at: string;
}

export interface ContactDetail extends ContactSummary {
  notes: string | null;
  conversations_count: number;
}

export interface ContactTicket {
  id: string;
  protocol: string;
  subject: string;
  status: string;
  priority: string;
  channel: string;
  department_id: string;
  attendant_id: string | null;
  created_at: string;
  updated_at: string;
}

export interface ContactConversation {
  id: string;
  channel: string;
  status: string;
  ticket_id: string | null;
  created_at: string;
}

export interface UpdateContactPayload {
  name?: string | null;
  email?: string | null;
  phone?: string | null;
  notes?: string | null;
}

interface PagedResponse<T> {
  success: boolean;
  data: T[];
  meta: { page: number; per_page: number; total: number };
}

@Injectable({ providedIn: 'root' })
export class ContactsService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = environment.apiUrl;

  readonly loading = signal(false);
  readonly contact = signal<ContactDetail | null>(null);

  async list(q?: string, page = 1, perPage = 20): Promise<{ items: ContactSummary[]; total: number }> {
    const params: Record<string, string> = { page: String(page), per_page: String(perPage) };
    if (q) params['q'] = q;
    const url = new URL(`${this.apiBase}/api/contacts`);
    Object.entries(params).forEach(([k, v]) => url.searchParams.set(k, v));

    const res = await firstValueFrom(
      this.http.get<PagedResponse<ContactSummary>>(url.toString()),
    );
    return { items: res.data, total: res.meta.total };
  }

  async get(id: string): Promise<ContactDetail | null> {
    this.loading.set(true);
    try {
      const res = await firstValueFrom(
        this.http.get<{ success: boolean; data: ContactDetail }>(`${this.apiBase}/api/contacts/${id}`)
          .pipe(map((r) => r.data)),
      );
      this.contact.set(res);
      return res;
    } catch {
      return null;
    } finally {
      this.loading.set(false);
    }
  }

  async update(id: string, payload: UpdateContactPayload): Promise<{ success: boolean; error?: string }> {
    try {
      await firstValueFrom(this.http.put(`${this.apiBase}/api/contacts/${id}`, payload));
      return { success: true };
    } catch (err: any) {
      const code = err?.error?.error?.code ?? 'UNKNOWN';
      return { success: false, error: code };
    }
  }

  async listTickets(id: string, page = 1, perPage = 20): Promise<{ items: ContactTicket[]; total: number }> {
    const res = await firstValueFrom(
      this.http.get<PagedResponse<ContactTicket>>(
        `${this.apiBase}/api/contacts/${id}/tickets?page=${page}&per_page=${perPage}`,
      ),
    );
    return { items: res.data, total: res.meta.total };
  }

  async listConversations(id: string, page = 1, perPage = 20): Promise<{ items: ContactConversation[]; total: number }> {
    const res = await firstValueFrom(
      this.http.get<PagedResponse<ContactConversation>>(
        `${this.apiBase}/api/contacts/${id}/conversations?page=${page}&per_page=${perPage}`,
      ),
    );
    return { items: res.data, total: res.meta.total };
  }
}
