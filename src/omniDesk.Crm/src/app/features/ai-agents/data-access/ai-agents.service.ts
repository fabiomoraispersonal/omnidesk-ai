import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  AiAgentDetail,
  AiAgentSummary,
  CreateAiAgentRequest,
  UpdateAiAgentRequest,
} from './ai-agents.types';

interface Envelope<T> {
  success: boolean;
  data: T;
  meta?: { total: number };
}

@Injectable({ providedIn: 'root' })
export class AiAgentsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/agents`;

  list(includeInactive = false): Observable<AiAgentSummary[]> {
    return this.http
      .get<Envelope<AiAgentSummary[]>>(this.base, { params: { include_inactive: String(includeInactive) } })
      .pipe(map((env) => env.data));
  }

  get(id: string): Observable<AiAgentDetail> {
    return this.http.get<Envelope<AiAgentDetail>>(`${this.base}/${id}`).pipe(map((env) => env.data));
  }

  create(body: CreateAiAgentRequest): Observable<{ id: string }> {
    return this.http.post<Envelope<{ id: string }>>(this.base, body).pipe(map((env) => env.data));
  }

  update(id: string, body: UpdateAiAgentRequest): Observable<{ id: string }> {
    return this.http.put<Envelope<{ id: string }>>(`${this.base}/${id}`, body).pipe(map((env) => env.data));
  }

  toggle(id: string, isActive: boolean): Observable<{ id: string; is_active: boolean }> {
    return this.http
      .patch<Envelope<{ id: string; is_active: boolean }>>(`${this.base}/${id}/toggle`, { isActive })
      .pipe(map((env) => env.data));
  }

  delete(id: string): Observable<{ id: string; soft_deleted: boolean }> {
    return this.http
      .delete<Envelope<{ id: string; soft_deleted: boolean }>>(`${this.base}/${id}`)
      .pipe(map((env) => env.data));
  }
}
