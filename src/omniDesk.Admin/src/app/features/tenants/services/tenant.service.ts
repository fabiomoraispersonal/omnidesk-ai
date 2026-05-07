import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  CreateTenantRequest,
  CreateTenantResponse,
  ImpersonateResponse,
  TenantDetail,
  TenantMetricsDetail,
  TenantStatus,
  TenantSummary,
  UpdateTenantRequest,
} from '../models/tenant.models';

@Injectable({ providedIn: 'root' })
export class TenantService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/admin/tenants';

  getTenants(status?: TenantStatus): Observable<TenantSummary[]> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    return this.http.get<TenantSummary[]>(this.base, { params });
  }

  getTenantDetail(id: string): Observable<TenantDetail> {
    return this.http.get<TenantDetail>(`${this.base}/${id}`);
  }

  createTenant(req: CreateTenantRequest): Observable<CreateTenantResponse> {
    return this.http.post<CreateTenantResponse>(this.base, req);
  }

  updateTenant(id: string, req: UpdateTenantRequest): Observable<TenantDetail> {
    return this.http.put<TenantDetail>(`${this.base}/${id}`, req);
  }

  retryProvisioning(id: string): Observable<CreateTenantResponse> {
    return this.http.post<CreateTenantResponse>(`${this.base}/${id}/retry-provisioning`, null);
  }

  blockTenant(id: string): Observable<{ id: string; status: TenantStatus; blocked_at: string }> {
    return this.http.post<{ id: string; status: TenantStatus; blocked_at: string }>(
      `${this.base}/${id}/block`, null);
  }

  unblockTenant(id: string): Observable<{ id: string; status: TenantStatus; blocked_at: null }> {
    return this.http.post<{ id: string; status: TenantStatus; blocked_at: null }>(
      `${this.base}/${id}/unblock`, null);
  }

  resetSuperAdminPassword(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${id}/reset-password`, null);
  }

  impersonateTenant(id: string): Observable<ImpersonateResponse> {
    return this.http.post<ImpersonateResponse>(`${this.base}/${id}/impersonate`, null);
  }

  getTenantMetrics(id: string): Observable<TenantMetricsDetail> {
    return this.http.get<TenantMetricsDetail>(`${this.base}/${id}/metrics`);
  }
}
