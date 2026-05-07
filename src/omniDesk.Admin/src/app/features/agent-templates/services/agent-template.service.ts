import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AgentTemplate,
  CreateAgentTemplateRequest,
  UpdateAgentTemplateRequest,
} from '../../tenants/models/tenant.models';

@Injectable({ providedIn: 'root' })
export class AgentTemplateService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/admin/agent-templates';

  getTemplates(activeOnly?: boolean): Observable<AgentTemplate[]> {
    let params = new HttpParams();
    if (activeOnly !== undefined) params = params.set('active_only', String(activeOnly));
    return this.http.get<AgentTemplate[]>(this.base, { params });
  }

  createTemplate(req: CreateAgentTemplateRequest): Observable<AgentTemplate> {
    return this.http.post<AgentTemplate>(this.base, req);
  }

  updateTemplate(id: string, req: UpdateAgentTemplateRequest): Observable<AgentTemplate> {
    return this.http.put<AgentTemplate>(`${this.base}/${id}`, req);
  }

  deleteTemplate(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
