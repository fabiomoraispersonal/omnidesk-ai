import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  CreateTemplateRequest,
  ListTemplatesFilter,
  ListTemplatesResult,
  UpdateTemplateRequest,
  WhatsAppTemplate,
} from './whatsapp-templates.types';

interface ListEnvelope {
  success: true;
  data: WhatsAppTemplate[];
  meta: { page: number; per_page: number; total: number };
}

interface ItemEnvelope {
  success: true;
  data: WhatsAppTemplate;
}

/**
 * Spec 008 US5 — signal store dos templates WhatsApp. Conversa com
 * <c>/api/whatsapp/templates</c>.
 */
@Injectable({ providedIn: 'root' })
export class WhatsAppTemplatesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/whatsapp/templates`;

  readonly templates = signal<WhatsAppTemplate[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly perPage = signal(20);
  readonly loading = signal(false);
  readonly saving = signal(false);

  async list(filter: ListTemplatesFilter = {}): Promise<ListTemplatesResult> {
    this.loading.set(true);
    try {
      let params = new HttpParams();
      if (filter.status) params = params.set('status', filter.status);
      if (filter.type) params = params.set('type', filter.type);
      if (filter.page) params = params.set('page', String(filter.page));
      if (filter.per_page) params = params.set('per_page', String(filter.per_page));

      const env = await firstValueFrom(
        this.http.get<ListEnvelope>(this.base, { params }),
      );

      const result: ListTemplatesResult = {
        items: env.data,
        total: env.meta.total,
        page: env.meta.page,
        per_page: env.meta.per_page,
      };
      this.templates.set(result.items);
      this.total.set(result.total);
      this.page.set(result.page);
      this.perPage.set(result.per_page);
      return result;
    } finally {
      this.loading.set(false);
    }
  }

  async create(req: CreateTemplateRequest): Promise<WhatsAppTemplate> {
    this.saving.set(true);
    try {
      const data = await firstValueFrom(
        this.http.post<ItemEnvelope>(this.base, req).pipe(map((e) => e.data)),
      );
      this.templates.update((arr) => [data, ...arr]);
      return data;
    } finally {
      this.saving.set(false);
    }
  }

  async update(id: string, req: UpdateTemplateRequest): Promise<WhatsAppTemplate> {
    this.saving.set(true);
    try {
      const data = await firstValueFrom(
        this.http.put<ItemEnvelope>(`${this.base}/${id}`, req).pipe(map((e) => e.data)),
      );
      this.templates.update((arr) => arr.map((t) => (t.id === id ? data : t)));
      return data;
    } finally {
      this.saving.set(false);
    }
  }

  async submit(id: string): Promise<WhatsAppTemplate> {
    this.saving.set(true);
    try {
      const data = await firstValueFrom(
        this.http.post<ItemEnvelope>(`${this.base}/${id}/submit`, {}).pipe(map((e) => e.data)),
      );
      this.templates.update((arr) => arr.map((t) => (t.id === id ? data : t)));
      return data;
    } finally {
      this.saving.set(false);
    }
  }

  async delete(id: string): Promise<void> {
    this.saving.set(true);
    try {
      await firstValueFrom(this.http.delete<void>(`${this.base}/${id}`));
      this.templates.update((arr) => arr.filter((t) => t.id !== id));
    } finally {
      this.saving.set(false);
    }
  }
}
