import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface ProfessionalDto {
  id: string;
  name: string;
  specialty: string | null;
  department_id: string | null;
  attendant_id: string | null;
  is_active: boolean;
  created_at: string;
  updated_at: string;
}

export interface WeeklyScheduleSlot {
  id: string;
  professional_id: string;
  day_of_week: number;
  start_time: string;
  end_time: string;
}

export interface ScheduleBlock {
  id: string;
  professional_id: string;
  start_at: string;
  end_at: string;
  reason: string | null;
  created_at: string;
}

export interface CreateProfessionalRequest {
  name: string;
  specialty?: string | null;
  department_id?: string | null;
  attendant_id?: string | null;
}

export type UpdateProfessionalRequest = CreateProfessionalRequest;

interface ListEnvelope { success: boolean; data: ProfessionalDto[]; meta: { total: number }; }
interface ItemEnvelope { success: boolean; data: ProfessionalDto; }
interface ToggleEnvelope { success: boolean; data: { id: string; is_active: boolean }; }
interface ServicesEnvelope { success: boolean; data: { id: string; service_id: string }[]; }
interface ScheduleEnvelope { success: boolean; data: WeeklyScheduleSlot[]; }
interface BlocksEnvelope { success: boolean; data: ScheduleBlock[]; }
interface BlockEnvelope { success: boolean; data: ScheduleBlock; }

/**
 * Spec 011 US2 (T063) — HTTP client para profissionais e sub-rotas
 * (services, schedule, blocks).
 */
@Injectable({ providedIn: 'root' })
export class ProfessionalsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/professionals`;

  async list(params: { includeInactive?: boolean; departmentId?: string; serviceId?: string } = {}) {
    const p: Record<string, string> = { page: '1', per_page: '100' };
    if (params.includeInactive) p['include_inactive'] = 'true';
    if (params.departmentId) p['department_id'] = params.departmentId;
    if (params.serviceId)    p['service_id']    = params.serviceId;
    const env = await firstValueFrom(this.http.get<ListEnvelope>(this.base, { params: p }));
    return { items: env.data, total: env.meta.total };
  }

  async create(req: CreateProfessionalRequest): Promise<ProfessionalDto> {
    const env = await firstValueFrom(this.http.post<ItemEnvelope>(this.base, req));
    return env.data;
  }

  async update(id: string, req: UpdateProfessionalRequest): Promise<ProfessionalDto> {
    const env = await firstValueFrom(this.http.put<ItemEnvelope>(`${this.base}/${id}`, req));
    return env.data;
  }

  async toggle(id: string, isActive: boolean) {
    const env = await firstValueFrom(
      this.http.patch<ToggleEnvelope>(`${this.base}/${id}/toggle`, { is_active: isActive }));
    return env.data;
  }

  async getServices(id: string) {
    const env = await firstValueFrom(this.http.get<ServicesEnvelope>(`${this.base}/${id}/services`));
    return env.data;
  }

  async updateServices(id: string, serviceIds: string[]) {
    await firstValueFrom(this.http.put(`${this.base}/${id}/services`, { service_ids: serviceIds }));
  }

  async getSchedule(id: string): Promise<WeeklyScheduleSlot[]> {
    const env = await firstValueFrom(this.http.get<ScheduleEnvelope>(`${this.base}/${id}/schedule`));
    return env.data;
  }

  async updateSchedule(id: string, slots: { day_of_week: number; start_time: string; end_time: string }[]) {
    await firstValueFrom(this.http.put(`${this.base}/${id}/schedule`, { slots }));
  }

  async listBlocks(id: string): Promise<ScheduleBlock[]> {
    const env = await firstValueFrom(this.http.get<BlocksEnvelope>(`${this.base}/${id}/blocks`));
    return env.data;
  }

  async createBlock(id: string, req: { start_at: string; end_at: string; reason?: string | null }): Promise<ScheduleBlock> {
    const env = await firstValueFrom(this.http.post<BlockEnvelope>(`${this.base}/${id}/blocks`, req));
    return env.data;
  }

  async deleteBlock(professionalId: string, blockId: string) {
    await firstValueFrom(this.http.delete(`${this.base}/${professionalId}/blocks/${blockId}`));
  }
}
