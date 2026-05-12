// Spec 009 US9 — T174
import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../../../environments/environment';

export interface PipelineColumnData {
  id: string;
  name: string;
  status_mapping: string;
  order: number;
  color: string | null;
}

export interface PipelineData {
  id: string;
  department_id: string;
  name: string;
  columns: PipelineColumnData[];
  updated_at: string;
}

export interface UpdateColumnsPayload {
  columns: Array<{
    id?: string;
    name: string;
    status_mapping: string;
    order: number;
    color?: string | null;
  }>;
}

@Injectable({ providedIn: 'root' })
export class PipelineConfigService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = environment.apiUrl;

  readonly loading = signal(false);
  readonly pipeline = signal<PipelineData | null>(null);

  async getByDepartment(departmentId: string): Promise<PipelineData | null> {
    this.loading.set(true);
    try {
      const res = await firstValueFrom(
        this.http.get<{ success: boolean; data: PipelineData }>(
          `${this.apiBase}/api/pipelines/by-dept/${departmentId}`,
        ).pipe(map((r) => r.data)),
      );
      this.pipeline.set(res);
      return res;
    } catch {
      return null;
    } finally {
      this.loading.set(false);
    }
  }

  async updateColumns(pipelineId: string, payload: UpdateColumnsPayload): Promise<{ success: boolean; error?: string }> {
    try {
      const res = await firstValueFrom(
        this.http.put<{ success: boolean; data: PipelineData }>(
          `${this.apiBase}/api/pipelines/${pipelineId}/columns`,
          payload,
        ).pipe(map((r) => r.data)),
      );
      this.pipeline.set(res);
      return { success: true };
    } catch (err: any) {
      const msg = err?.error?.error?.message ?? 'Erro ao salvar configuração.';
      return { success: false, error: msg };
    }
  }
}
