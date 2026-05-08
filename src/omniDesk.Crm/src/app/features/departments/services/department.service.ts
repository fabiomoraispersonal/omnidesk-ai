import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';

export interface BusinessHoursDto {
  start: string;
  end: string;
  days: number[];
}

export interface SlaDto {
  firstResponseMinutes: number | null;
  resolutionMinutes: number | null;
}

export interface Department {
  id: string;
  name: string;
  description: string | null;
  businessHours: BusinessHoursDto | null;
  sla: SlaDto;
  isActive: boolean;
  attendantCount: number;
  activeTicketCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateDepartmentRequest {
  name: string;
  description?: string | null;
  businessHours?: BusinessHoursDto | null;
  sla?: SlaDto | null;
}

export interface DepartmentAttendantSummary {
  attendantId: string;
  name: string;
  avatarUrl: string | null;
  activeTicketCount: number;
  maxSimultaneousChats: number;
  isPrimaryDepartment: boolean;
  status: 'online' | 'away' | 'offline' | null;
}

@Injectable({ providedIn: 'root' })
export class DepartmentService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/departments`;

  list(includeInactive = false): Observable<Department[]> {
    const url = `${this.base}?include_inactive=${includeInactive}`;
    return this.http.get<{ data: Department[] }>(url).pipe(map(r => r.data));
  }

  get(id: string): Observable<Department> {
    return this.http.get<{ data: Department }>(`${this.base}/${id}`).pipe(map(r => r.data));
  }

  create(payload: CreateDepartmentRequest): Observable<Department> {
    return this.http.post<{ data: Department }>(this.base, payload).pipe(map(r => r.data));
  }

  update(id: string, payload: CreateDepartmentRequest): Observable<Department> {
    return this.http.put<{ data: Department }>(`${this.base}/${id}`, payload).pipe(map(r => r.data));
  }

  deactivate(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  attendants(id: string, status?: string): Observable<DepartmentAttendantSummary[]> {
    const url = status ? `${this.base}/${id}/attendants?status=${status}` : `${this.base}/${id}/attendants`;
    return this.http.get<{ data: DepartmentAttendantSummary[] }>(url).pipe(map(r => r.data));
  }
}
