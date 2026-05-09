import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';

export interface OpenAiCredentialsView {
  key_set: boolean;
  key_preview: string | null;
  organization: string | null;
  project: string | null;
}

export interface AiSettingsView {
  context_window_messages: number;
  available_models: string[];
  global_allowlist: string[];
  openai_credentials: OpenAiCredentialsView;
}

interface Envelope<T> { success: boolean; data: T; }

@Injectable({ providedIn: 'root' })
export class AiSettingsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/ai-settings`;

  get(): Observable<AiSettingsView> {
    return this.http.get<Envelope<AiSettingsView>>(this.base).pipe(map((e) => e.data));
  }

  update(body: { contextWindowMessages?: number; availableModels?: string[] }): Observable<unknown> {
    return this.http.put(this.base, body);
  }

  setKey(body: { apiKey: string; organization?: string; project?: string }): Observable<{ key_set: boolean; key_preview: string }> {
    return this.http
      .put<Envelope<{ key_set: boolean; key_preview: string }>>(`${this.base}/openai-credentials`, body)
      .pipe(map((e) => e.data));
  }

  deleteKey(): Observable<unknown> {
    return this.http.delete(`${this.base}/openai-credentials`);
  }
}
