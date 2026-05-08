import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';

export interface Attendant {
  id: string;
  userId: string;
  name: string;
  avatarUrl: string | null;
  maxSimultaneousChats: number;
  activeTicketCount: number;
  isActive: boolean;
  departmentIds: string[];
  primaryDepartmentId: string | null;
  status: 'online' | 'away' | 'offline' | null;
  createdAt: string;
}

export interface CreateAttendantRequest {
  userId: string;
  name: string;
  maxSimultaneousChats?: number;
  departmentIds: string[];
  primaryDepartmentId?: string | null;
}

export interface UpdateAttendantRequest {
  name: string;
  maxSimultaneousChats?: number;
}

export interface UpdateAttendantDepartmentsRequest {
  departmentIds: string[];
  primaryDepartmentId?: string | null;
}

@Injectable({ providedIn: 'root' })
export class AttendantService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/attendants`;

  list(): Observable<Attendant[]> {
    return this.http.get<{ data: Attendant[] }>(this.base).pipe(map(r => r.data));
  }

  get(id: string): Observable<Attendant> {
    return this.http.get<{ data: Attendant }>(`${this.base}/${id}`).pipe(map(r => r.data));
  }

  create(payload: CreateAttendantRequest): Observable<Attendant> {
    return this.http.post<{ data: Attendant }>(this.base, payload).pipe(map(r => r.data));
  }

  update(id: string, payload: UpdateAttendantRequest): Observable<Attendant> {
    return this.http.put<{ data: Attendant }>(`${this.base}/${id}`, payload).pipe(map(r => r.data));
  }

  deactivate(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  updateDepartments(id: string, payload: UpdateAttendantDepartmentsRequest): Observable<Attendant> {
    return this.http.put<{ data: Attendant }>(`${this.base}/${id}/departments`, payload).pipe(map(r => r.data));
  }

  uploadAvatar(id: string, file: File): Observable<{ avatarUrl: string }> {
    const fd = new FormData();
    fd.append('file', file);
    return this.http.post<{ data: { avatarUrl: string } }>(`${this.base}/${id}/avatar`, fd).pipe(map(r => r.data));
  }
}
