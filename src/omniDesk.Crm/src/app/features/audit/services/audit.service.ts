import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../../environments/environment';

export interface AuditActorDto {
  user_id: string | null;
  name: string | null;
  role: string;
  impersonated_by: string | null;
}

export interface AuditTargetDto {
  entity_type: string;
  entity_id: string;
  label: string | null;
}

export interface AuditLogDto {
  id: string;
  event: string;
  actor: AuditActorDto;
  target: AuditTargetDto | null;
  metadata: Record<string, unknown> | null;
  ip_address: string | null;
  timestamp: string;
}

export interface AuditLogFilters {
  event?: string;
  actor_id?: string;
  from?: string;
  to?: string;
  page?: number;
  per_page?: number;
}

export interface PagedResult<T> {
  data: T[];
  meta: { page: number; per_page: number; total: number };
}

interface Envelope<T> { data: T; meta?: { page: number; per_page: number; total: number } }

@Injectable({ providedIn: 'root' })
export class AuditService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/audit-logs`;

  getAuditLogs(filters: AuditLogFilters = {}): Observable<PagedResult<AuditLogDto>> {
    let params = new HttpParams();
    if (filters.event)    params = params.set('event', filters.event);
    if (filters.actor_id) params = params.set('actor_id', filters.actor_id);
    if (filters.from)     params = params.set('from', filters.from);
    if (filters.to)       params = params.set('to', filters.to);
    if (filters.page)     params = params.set('page', filters.page.toString());
    if (filters.per_page) params = params.set('per_page', filters.per_page.toString());

    return this.http
      .get<Envelope<AuditLogDto[]>>(this.base, { params })
      .pipe(map(r => ({ data: r.data, meta: r.meta! })));
  }
}
