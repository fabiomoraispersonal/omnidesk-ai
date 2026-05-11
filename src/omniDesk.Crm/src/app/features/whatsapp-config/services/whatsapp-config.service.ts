import { computed, Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, map } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  ToggleChannelResult,
  UpdateWhatsAppConfigRequest,
  WhatsAppConfig,
} from './whatsapp-config.types';

interface Envelope<T> {
  success: true;
  data: T;
}

/**
 * Spec 008 US2 — signal store para WhatsApp config. Conversa com
 * `/api/whatsapp/config` (GET / PUT / PATCH toggle).
 *
 * Lifecycle:
 *  - load(): GET — popula `config`.
 *  - save(req): PUT — envia apenas campos não-empty; access_token/app_secret
 *    vazios significam "manter o existente".
 *  - toggle(enabled): PATCH /toggle — em <c>true</c> backend valida com Meta /me.
 *  - loading()/saving() expõem estado para spinners.
 */
@Injectable({ providedIn: 'root' })
export class WhatsAppConfigService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/api/whatsapp/config`;

  readonly config = signal<WhatsAppConfig | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);

  readonly channelStatus = computed(() => this.config()?.channel_status ?? null);
  readonly isEnabled = computed(() => this.config()?.is_enabled ?? false);

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      const data = await firstValueFrom(
        this.http.get<Envelope<WhatsAppConfig>>(this.base).pipe(map((e) => e.data)),
      );
      this.config.set(data);
    } finally {
      this.loading.set(false);
    }
  }

  async save(payload: UpdateWhatsAppConfigRequest): Promise<WhatsAppConfig> {
    this.saving.set(true);
    try {
      const data = await firstValueFrom(
        this.http.put<Envelope<WhatsAppConfig>>(this.base, payload).pipe(map((e) => e.data)),
      );
      this.config.set(data);
      return data;
    } finally {
      this.saving.set(false);
    }
  }

  async toggle(isEnabled: boolean): Promise<ToggleChannelResult> {
    this.saving.set(true);
    try {
      const data = await firstValueFrom(
        this.http
          .patch<Envelope<WhatsAppConfig>>(`${this.base}/toggle`, { is_enabled: isEnabled })
          .pipe(map((e) => e.data)),
      );
      this.config.set(data);
      return { is_enabled: data.is_enabled, channel_status: data.channel_status };
    } finally {
      this.saving.set(false);
    }
  }
}
