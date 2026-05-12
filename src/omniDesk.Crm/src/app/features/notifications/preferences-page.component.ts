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
import { CheckboxModule } from 'primeng/checkbox';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { environment } from '../../../environments/environment';

interface PreferencesDto {
  push_enabled: boolean;
  event_push_flags: Record<string, boolean>;
}

interface PreferencesEnvelope { success: boolean; data: PreferencesDto; }

const EVENT_LABELS: Record<string, string> = {
  'ticket.assigned':           'Ticket atribuído a mim',
  'ticket.new_message':        'Nova mensagem no ticket',
  'ticket.transferred_to_me':  'Ticket transferido para mim',
  'ticket.sla_warning':        'Aviso de SLA',
  'ticket.sla_breached':       'SLA ultrapassado',
  'ticket.client_replied':     'Cliente respondeu',
  'ticket.queued':             'Ticket sem atendente (supervisores)',
  'ticket.reminder_failed':    'Falha no lembrete de agendamento',
};

/**
 * Spec 010 US6 T089 — Preferências de Notificação.
 * Toggle global (push_enabled) + checkbox per event type.
 */
@Component({
  selector: 'app-notification-preferences-page',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, CardModule, CheckboxModule, ToastModule],
  providers: [MessageService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-toast />
    <div class="prefs-page">
      <p-card header="Notificações">
        <p class="hint">
          Suas preferências controlam as notificações <strong>push do navegador</strong>.
          Notificações in-app continuam aparecendo no sino mesmo se push for desativado.
        </p>

        <div class="row">
          <p-checkbox
            [(ngModel)]="pushEnabled"
            [binary]="true"
            inputId="push"
            [disabled]="loading()" />
          <label for="push">Ativar push do navegador</label>
        </div>

        <h3>Quais eventos disparam push:</h3>
        <div class="event-grid">
          @for (key of eventKeys; track key) {
            <div class="row">
              <p-checkbox
                [(ngModel)]="eventFlags[key]"
                [binary]="true"
                [inputId]="'evt-' + key"
                [disabled]="loading() || !pushEnabled" />
              <label [attr.for]="'evt-' + key">{{ EVENT_LABELS[key] }}</label>
            </div>
          }
        </div>

        <div class="actions">
          <p-button
            label="Salvar"
            icon="pi pi-save"
            [loading]="loading()"
            (click)="save()" />
        </div>
      </p-card>
    </div>
  `,
  styles: [`
    .prefs-page { max-width: 640px; margin: 24px auto; padding: 0 16px; }
    .hint { color: var(--text-color-secondary); margin-bottom: 16px; }
    .row { display: flex; align-items: center; gap: 8px; padding: 6px 0; }
    .event-grid { display: flex; flex-direction: column; gap: 4px; margin: 12px 0 24px; }
    .actions { display: flex; justify-content: flex-end; }
    h3 { font-size: 14px; margin: 24px 0 8px; color: var(--text-color); }
  `],
})
export class NotificationPreferencesPageComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly toast = inject(MessageService);
  private readonly url = `${environment.apiUrl}/api/notifications/preferences`;

  protected readonly EVENT_LABELS = EVENT_LABELS;
  protected readonly eventKeys = Object.keys(EVENT_LABELS);
  protected readonly loading = signal(false);

  protected pushEnabled = true;
  protected eventFlags: Record<string, boolean> = {};

  async ngOnInit(): Promise<void> {
    this.loading.set(true);
    try {
      const env = await firstValueFrom(this.http.get<PreferencesEnvelope>(this.url));
      this.pushEnabled = env.data.push_enabled;
      this.eventFlags = { ...env.data.event_push_flags };
      // Make sure all keys are present.
      for (const k of this.eventKeys) if (!(k in this.eventFlags)) this.eventFlags[k] = true;
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao carregar preferências.' });
    } finally {
      this.loading.set(false);
    }
  }

  async save(): Promise<void> {
    this.loading.set(true);
    try {
      const body = {
        push_enabled: this.pushEnabled,
        event_push_flags: this.eventFlags,
      };
      const env = await firstValueFrom(this.http.put<PreferencesEnvelope>(this.url, body));
      this.pushEnabled = env.data.push_enabled;
      this.eventFlags = { ...env.data.event_push_flags };
      this.toast.add({ severity: 'success', summary: 'Salvo', detail: 'Preferências atualizadas.' });
    } catch {
      this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao salvar.' });
    } finally {
      this.loading.set(false);
    }
  }
}
