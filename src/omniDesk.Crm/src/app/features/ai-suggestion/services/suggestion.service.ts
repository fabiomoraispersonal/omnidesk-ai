import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../../../environments/environment';

export interface SuggestionContextUsed {
  sub_agent_id: string | null;
  sub_agent_name: string | null;
  messages_used: number;
}

export interface SuggestionResponse {
  suggestionId: string;
  text: string;
  model: string;
  elapsedMs: number;
  inputTokens: number;
  outputTokens: number;
  contextUsed: SuggestionContextUsed;
}

export type HumanAction = 'approved' | 'edited' | 'discarded' | 'sent_unchanged';

@Injectable({ providedIn: 'root' })
export class SuggestionService {
  private readonly http = inject(HttpClient);

  request(conversationId: string, contextMessageCount?: number): Observable<SuggestionResponse> {
    return this.http.post<{ data: SuggestionResponse }>(
      `${environment.apiUrl}/api/conversations/${conversationId}/suggest-reply`,
      { contextMessageCount: contextMessageCount ?? null },
    ).pipe(map(r => r.data));
  }

  recordAction(
    conversationId: string,
    suggestionId: string,
    action: HumanAction,
    finalMessageText?: string,
  ): Observable<void> {
    return this.http.patch<void>(
      `${environment.apiUrl}/api/conversations/${conversationId}/suggestions/${suggestionId}`,
      { humanAction: action, finalMessageText: finalMessageText ?? null },
    );
  }
}
