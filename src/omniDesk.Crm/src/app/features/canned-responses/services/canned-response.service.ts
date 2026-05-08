import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';

export interface CannedResponseAuthor { id: string; name: string; }

export interface CannedResponse {
  id: string;
  title: string;
  content: string;
  departmentId: string | null;
  scope: 'global' | 'department';
  createdBy: CannedResponseAuthor;
  createdAt: string;
  updatedAt: string;
}

export interface CreateCannedResponseRequest {
  title: string;
  content: string;
  departmentId: string | null;
}

export interface RenderResult {
  rendered: string;
  missingVariables: string[];
}

@Injectable({ providedIn: 'root' })
export class CannedResponseService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/canned-responses`;

  list(filter?: { departmentId?: string; q?: string }): Observable<CannedResponse[]> {
    let params = new HttpParams();
    if (filter?.departmentId) params = params.set('department_id', filter.departmentId);
    if (filter?.q) params = params.set('q', filter.q);
    return this.http.get<{ data: CannedResponse[] }>(this.base, { params }).pipe(map(r => r.data));
  }

  create(payload: CreateCannedResponseRequest): Observable<CannedResponse> {
    return this.http.post<{ data: CannedResponse }>(this.base, payload).pipe(map(r => r.data));
  }

  update(id: string, payload: CreateCannedResponseRequest): Observable<CannedResponse> {
    return this.http.put<{ data: CannedResponse }>(`${this.base}/${id}`, payload).pipe(map(r => r.data));
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  render(templateId: string, context: { ticketId?: string; conversationId?: string; attendantId?: string }): Observable<RenderResult> {
    return this.http.post<{ data: RenderResult }>(`${this.base}/render`, {
      templateId,
      context,
    }).pipe(map(r => r.data));
  }
}
