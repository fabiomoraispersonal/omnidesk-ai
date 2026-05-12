import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { InputSwitchModule } from 'primeng/inputswitch';
import { InputMaskModule } from 'primeng/inputmask';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { environment } from '../../../environments/environment';

interface SettingsDto {
  follow_up_enabled: boolean;
  reminder_enabled: boolean;
  reminder_time: string;  // "HH:mm"
}

interface SettingsEnvelope { success: boolean; data: SettingsDto; }

/**
 * Spec 010 Phase 9 T096 — Tenant Admin → Configurações → Notificações.
 * Three controls: follow-up toggle, reminder toggle, reminder time (HH:mm).
 * Route is guarded; component itself also tolerates 403 with a clear message.
 */
@Component({
  selector: 'app-notification-settings-page',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    ButtonModule, CardModule, InputSwitchModule, InputMaskModule, ToastModule,
  ],
  providers: [MessageService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-toast />
    <div class="settings-page">
      <p-card header="Notificações para clientes (WhatsApp)">
        <p class="hint">
          Estas configurações controlam o envio automático de mensagens WhatsApp para os clientes.
          As notificações in-app e push para atendentes são configuradas por cada usuário.
        </p>

        @if (forbidden()) {
          <div class="forbidden">
            Acesso restrito a administradores do tenant.
          </div>
        } @else {
          <div class="row">
            <p-inputSwitch [(ngModel)]="followUp" inputId="followup" [disabled]="loading()" />
            <label for="followup">
              <strong>Enviar follow-up ao encerrar ticket</strong>
              <div class="sub">Dispara o template <code>follow_up</code> automaticamente após o atendente encerrar.</div>
            </label>
          </div>

          <div class="row">
            <p-inputSwitch [(ngModel)]="reminder" inputId="reminder" [disabled]="loading()" />
            <label for="reminder">
              <strong>Enviar lembrete de consulta</strong>
              <div class="sub">Job diário que envia o template <code>appointment_reminder</code> 24h antes.</div>
            </label>
          </div>

          <div class="row">
            <label for="time" class="time-label">Horário do lembrete:</label>
            <p-inputMask
              [(ngModel)]="reminderTime"
              mask="99:99"
              placeholder="HH:mm"
              inputId="time"
              [disabled]="loading() || !reminder" />
            <span class="sub">(horário local do tenant)</span>
          </div>

          <div class="actions">
            <p-button
              label="Salvar"
              icon="pi pi-save"
              [loading]="loading()"
              (click)="save()" />
          </div>
        }
      </p-card>
    </div>
  `,
  styles: [`
    .settings-page { max-width: 720px; margin: 24px auto; padding: 0 16px; }
    .hint { color: var(--text-color-secondary); margin-bottom: 16px; }
    .row { display: flex; align-items: flex-start; gap: 12px; padding: 12px 0;
           border-bottom: 1px solid var(--surface-200); }
    .row:last-of-type { border-bottom: 0; }
    .sub { font-size: 12px; color: var(--text-color-secondary); margin-top: 2px; }
    .time-label { font-weight: 600; min-width: 160px; }
    .actions { display: flex; justify-content: flex-end; padding-top: 16px; }
    .forbidden { padding: 16px; background: var(--red-50); color: var(--red-700);
                 border-radius: 4px; }
  `],
})
export class NotificationSettingsPageComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly toast = inject(MessageService);
  private readonly url = `${environment.apiUrl}/api/notification-settings`;

  protected readonly loading = signal(false);
  protected readonly forbidden = signal(false);

  protected followUp = false;
  protected reminder = false;
  protected reminderTime = '20:00';

  async ngOnInit(): Promise<void> {
    this.loading.set(true);
    try {
      const env = await firstValueFrom(this.http.get<SettingsEnvelope>(this.url));
      this.followUp     = env.data.follow_up_enabled;
      this.reminder     = env.data.reminder_enabled;
      this.reminderTime = env.data.reminder_time;
    } catch (e: unknown) {
      const code = (e as { status?: number })?.status;
      if (code === 403) this.forbidden.set(true);
      else this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao carregar.' });
    } finally {
      this.loading.set(false);
    }
  }

  async save(): Promise<void> {
    if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(this.reminderTime)) {
      this.toast.add({ severity: 'warn', summary: 'Horário inválido', detail: 'Use o formato HH:mm.' });
      return;
    }
    this.loading.set(true);
    try {
      const body = {
        follow_up_enabled: this.followUp,
        reminder_enabled:  this.reminder,
        reminder_time:     this.reminderTime,
      };
      const env = await firstValueFrom(this.http.put<SettingsEnvelope>(this.url, body));
      this.followUp     = env.data.follow_up_enabled;
      this.reminder     = env.data.reminder_enabled;
      this.reminderTime = env.data.reminder_time;
      this.toast.add({ severity: 'success', summary: 'Salvo', detail: 'Configurações atualizadas.' });
    } catch (e: unknown) {
      const code = (e as { status?: number })?.status;
      if (code === 403) this.forbidden.set(true);
      else this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao salvar.' });
    } finally {
      this.loading.set(false);
    }
  }
}
