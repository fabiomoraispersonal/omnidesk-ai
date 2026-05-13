import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AppointmentDto {
  id: string;
  professional_id: string;
  service_id: string;
  contact_id: string | null;
  ticket_id: string | null;
  conversation_id: string | null;
  start_at: string;
  end_at: string;
  status: 'pending_confirmation' | 'confirmed' | 'cancelled' | 'no_show';
  client_type: 'new_client' | 'returning_client';
  created_by: 'ai' | 'attendant';
  notes: string | null;
  reminder_sent_at: string | null;
  cancelled_by: 'client' | 'attendant' | 'system' | null;
  cancelled_at: string | null;
  cancellation_reason: string | null;
  created_at: string;
  updated_at: string;
  professional: { id: string; name: string } | null;
  service: { id: string; name: string; duration_minutes: number; price: number | null } | null;
}

export interface CreateAppointmentRequest {
  professional_id: string;
  service_id: string;
  contact_id?: string | null;
  ticket_id?: string | null;
  conversation_id?: string | null;
  start_at: string;
  notes?: string | null;
}

export interface UpdateAppointmentRequest {
  professional_id: string;
  service_id: string;
  contact_id?: string | null;
  start_at: string;
  notes?: string | null;
}

interface ListEnvelope { success: boolean; data: AppointmentDto[]; meta: { page: number; per_page: number; total: number }; }
interface ItemEnvelope { success: boolean; data: AppointmentDto; }

/**
 * Spec 011 US3 (T100) — HTTP client for /api/appointments.
 */
@Injectable({ providedIn: 'root' })
export class AppointmentsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/appointments`;

  async list(params: {
    professional_id?: string;
    service_id?: string;
    status?: string;
    from?: string;
    to?: string;
    page?: number;
    per_page?: number;
  } = {}) {
    const p: Record<string, string> = {};
    if (params.professional_id) p['professional_id'] = params.professional_id;
    if (params.service_id)      p['service_id']      = params.service_id;
    if (params.status)          p['status']           = params.status;
    if (params.from)            p['from']             = params.from;
    if (params.to)              p['to']               = params.to;
    p['page']     = String(params.page ?? 1);
    p['per_page'] = String(params.per_page ?? 20);
    const env = await firstValueFrom(this.http.get<ListEnvelope>(this.base, { params: p }));
    return { items: env.data, total: env.meta.total };
  }

  async get(id: string): Promise<AppointmentDto> {
    const env = await firstValueFrom(this.http.get<ItemEnvelope>(`${this.base}/${id}`));
    return env.data;
  }

  async create(req: CreateAppointmentRequest): Promise<AppointmentDto> {
    const env = await firstValueFrom(this.http.post<ItemEnvelope>(this.base, req));
    return env.data;
  }

  async update(id: string, req: UpdateAppointmentRequest): Promise<AppointmentDto> {
    const env = await firstValueFrom(this.http.put<ItemEnvelope>(`${this.base}/${id}`, req));
    return env.data;
  }

  async confirm(id: string): Promise<AppointmentDto> {
    const env = await firstValueFrom(this.http.patch<ItemEnvelope>(`${this.base}/${id}/confirm`, {}));
    return env.data;
  }

  async cancel(id: string, reason?: string | null): Promise<AppointmentDto> {
    const env = await firstValueFrom(
      this.http.patch<ItemEnvelope>(`${this.base}/${id}/cancel`, { cancellation_reason: reason ?? null }));
    return env.data;
  }

  async noShow(id: string): Promise<AppointmentDto> {
    const env = await firstValueFrom(this.http.patch<ItemEnvelope>(`${this.base}/${id}/no-show`, {}));
    return env.data;
  }

  async resendReminder(id: string): Promise<{ reminder_sent_at: string }> {
    const env = await firstValueFrom(
      this.http.post<{ success: boolean; data: { reminder_sent_at: string } }>(`${this.base}/${id}/resend-reminder`, {}));
    return env.data;
  }
}
