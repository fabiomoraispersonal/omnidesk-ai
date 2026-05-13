import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface ServiceDto {
  id: string;
  name: string;
  description: string | null;
  category: string | null;
  duration_minutes: number;
  price: number | null;
  requires_confirmation: boolean;
  is_active: boolean;
  created_at: string;
  updated_at: string;
}

export interface CreateServiceRequest {
  name: string;
  description?: string | null;
  category?: string | null;
  duration_minutes: number;
  price?: number | null;
  requires_confirmation: boolean;
}

export type UpdateServiceRequest = CreateServiceRequest;

interface ListEnvelope {
  success: boolean;
  data: ServiceDto[];
  meta: { page: number; per_page: number; total: number };
}

interface ItemEnvelope { success: boolean; data: ServiceDto; }
interface ToggleEnvelope { success: boolean; data: { id: string; is_active: boolean }; }

/**
 * Spec 011 US1 (T036) — HTTP client para o catálogo de serviços.
 * Todas as chamadas de escrita exigem role tenant_admin (enforcement no backend).
 */
@Injectable({ providedIn: 'root' })
export class ServicesCatalogService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/services`;

  async list(params: {
    page?: number;
    perPage?: number;
    includeInactive?: boolean;
    sort?: 'name' | 'created_at';
    order?: 'asc' | 'desc';
  } = {}): Promise<{ items: ServiceDto[]; total: number }> {
    const p: Record<string, string> = {
      page: String(params.page ?? 1),
      per_page: String(params.perPage ?? 50),
    };
    if (params.includeInactive) p['include_inactive'] = 'true';
    if (params.sort)  p['sort']  = params.sort;
    if (params.order) p['order'] = params.order;

    const env = await firstValueFrom(this.http.get<ListEnvelope>(this.base, { params: p }));
    return { items: env.data, total: env.meta.total };
  }

  async create(req: CreateServiceRequest): Promise<ServiceDto> {
    const env = await firstValueFrom(this.http.post<ItemEnvelope>(this.base, req));
    return env.data;
  }

  async update(id: string, req: UpdateServiceRequest): Promise<ServiceDto> {
    const env = await firstValueFrom(this.http.put<ItemEnvelope>(`${this.base}/${id}`, req));
    return env.data;
  }

  async toggle(id: string, isActive: boolean): Promise<{ id: string; is_active: boolean }> {
    const env = await firstValueFrom(
      this.http.patch<ToggleEnvelope>(`${this.base}/${id}/toggle`, { is_active: isActive }),
    );
    return env.data;
  }
}
