import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../environments/environment';

export interface AgendaSettingsDto {
  late_cancel_window_hours: number;
  late_cancel_text: string;
  cancellation_policy_text: string;
  updated_at: string;
}

interface ApiResponse<T> { success: boolean; data: T; }

@Injectable({ providedIn: 'root' })
export class AgendaSettingsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/agenda-settings`;

  get(): Observable<AgendaSettingsDto> {
    return this.http.get<ApiResponse<AgendaSettingsDto>>(this.base).pipe(map(r => r.data));
  }

  update(payload: Omit<AgendaSettingsDto, 'updated_at'>): Observable<AgendaSettingsDto> {
    return this.http.put<ApiResponse<AgendaSettingsDto>>(this.base, payload).pipe(map(r => r.data));
  }
}
