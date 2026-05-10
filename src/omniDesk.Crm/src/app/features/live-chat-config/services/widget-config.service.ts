import { computed, Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ToggleResult,
  WidgetConfig,
  WidgetConfigSnapshot,
} from './widget-config.types';

interface Envelope<T> {
  success: true;
  data: T;
}

/**
 * Spec 007 US2 — signal store for widget config. Talks to /api/widget/config.
 *
 * Lifecycle:
 *  - load() pulls the current config + token + installation snippet.
 *  - update() PUTs a new config; on 200 the local signal is replaced with the response.
 *  - toggle() flips is_enabled; result includes affected_conversations for the UX.
 *  - saving() and loading() expose intermediate state for buttons/spinners.
 */
@Injectable({ providedIn: 'root' })
export class WidgetConfigService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/widget/config`;

  readonly snapshot = signal<WidgetConfigSnapshot | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly config = computed(() => this.snapshot()?.config ?? null);

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      const data = await firstValueFrom(
        this.http.get<Envelope<WidgetConfigSnapshot>>(this.base).pipe(map((e) => e.data)),
      );
      this.snapshot.set(data);
    } finally {
      this.loading.set(false);
    }
  }

  async update(payload: Omit<WidgetConfig, 'is_enabled' | 'updated_at'>): Promise<WidgetConfig> {
    this.saving.set(true);
    try {
      const data = await firstValueFrom(
        this.http.put<Envelope<WidgetConfig>>(this.base, payload).pipe(map((e) => e.data)),
      );
      const current = this.snapshot();
      if (current) this.snapshot.set({ ...current, config: data });
      return data;
    } finally {
      this.saving.set(false);
    }
  }

  async toggle(isEnabled: boolean): Promise<ToggleResult> {
    this.saving.set(true);
    try {
      const data = await firstValueFrom(
        this.http
          .patch<Envelope<ToggleResult>>(`${this.base}/toggle`, { is_enabled: isEnabled })
          .pipe(map((e) => e.data)),
      );
      const current = this.snapshot();
      if (current) {
        this.snapshot.set({
          ...current,
          config: { ...current.config, is_enabled: data.is_enabled },
        });
      }
      return data;
    } finally {
      this.saving.set(false);
    }
  }
}
