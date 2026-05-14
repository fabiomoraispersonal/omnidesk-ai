import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../../environments/environment';

export interface ApiKeyResponse {
  id: string;
  name: string;
  scopes: string[];
  last_used_at: string | null;
  expires_at: string | null;
  revoked: boolean;
  created_at: string;
}

export interface CreatedApiKeyResponse extends ApiKeyResponse {
  key: string;
}

interface Envelope<T> { success: boolean; data: T; }

@Injectable({ providedIn: 'root' })
export class ApiKeysService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/api-keys`;

  listApiKeys(): Observable<ApiKeyResponse[]> {
    return new Observable(obs =>
      this.http.get<Envelope<ApiKeyResponse[]>>(this.base).subscribe({
        next: r => { obs.next(r.data); obs.complete(); },
        error: e => obs.error(e),
      })
    );
  }

  createApiKey(name: string): Observable<CreatedApiKeyResponse> {
    return new Observable(obs =>
      this.http.post<Envelope<CreatedApiKeyResponse>>(this.base, { name }).subscribe({
        next: r => { obs.next(r.data); obs.complete(); },
        error: e => obs.error(e),
      })
    );
  }

  revokeApiKey(id: string): Observable<void> {
    return new Observable(obs =>
      this.http.delete<void>(`${this.base}/${id}`).subscribe({
        next: () => { obs.next(); obs.complete(); },
        error: e => obs.error(e),
      })
    );
  }
}
