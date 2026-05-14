import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TableModule } from 'primeng/table';
import { DropdownModule } from 'primeng/dropdown';
import { CalendarModule } from 'primeng/calendar';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { AuditService, AuditLogDto } from '../services/audit.service';

const EVENT_OPTIONS = [
  { label: 'Todos os eventos', value: null },
  { label: 'Login', value: 'auth.login_success' },
  { label: 'Falha de login', value: 'auth.login_failed' },
  { label: 'Logout', value: 'auth.logout' },
  { label: 'Senha alterada', value: 'auth.password_changed' },
  { label: 'Redefinição de senha', value: 'auth.password_reset' },
  { label: '2FA ativado', value: 'auth.totp_enabled' },
  { label: '2FA desativado', value: 'auth.totp_disabled' },
  { label: 'Impersonation iniciada', value: 'auth.impersonation_started' },
  { label: 'Usuário convidado', value: 'user.invited' },
  { label: 'Convite aceito', value: 'user.invite_accepted' },
  { label: 'Usuário desativado', value: 'user.deactivated' },
  { label: 'Usuário reativado', value: 'user.reactivated' },
  { label: 'Ticket criado', value: 'ticket.created' },
  { label: 'Ticket atribuído', value: 'ticket.assigned' },
  { label: 'Ticket transferido', value: 'ticket.transferred' },
  { label: 'Status de ticket alterado', value: 'ticket.status_changed' },
  { label: 'Ticket cancelado', value: 'ticket.cancelled' },
  { label: 'Agendamento criado', value: 'appointment.created' },
  { label: 'Agendamento confirmado', value: 'appointment.confirmed' },
  { label: 'Agendamento cancelado', value: 'appointment.cancelled' },
  { label: 'No-show', value: 'appointment.no_show' },
  { label: 'WhatsApp configurado', value: 'tenant.whatsapp_configured' },
  { label: 'Chave OpenAI alterada', value: 'tenant.openai_key_changed' },
  { label: 'Agente IA criado', value: 'ai_agent.created' },
  { label: 'Agente IA atualizado', value: 'ai_agent.updated' },
  { label: 'Agente IA deletado', value: 'ai_agent.deleted' },
];

const EVENT_ICONS: Record<string, string> = {
  'auth.login_success': 'pi pi-sign-in',
  'auth.login_failed': 'pi pi-times-circle',
  'auth.logout': 'pi pi-sign-out',
  'auth.password_changed': 'pi pi-key',
  'auth.password_reset': 'pi pi-refresh',
  'auth.totp_enabled': 'pi pi-shield',
  'auth.totp_disabled': 'pi pi-shield',
  'auth.impersonation_started': 'pi pi-eye',
  'user.invited': 'pi pi-user-plus',
  'user.invite_accepted': 'pi pi-check-circle',
  'user.deactivated': 'pi pi-ban',
  'user.reactivated': 'pi pi-check',
  'user.role_changed': 'pi pi-users',
  'ticket.created': 'pi pi-ticket',
  'ticket.assigned': 'pi pi-user',
  'ticket.transferred': 'pi pi-arrow-right',
  'ticket.status_changed': 'pi pi-sync',
  'ticket.cancelled': 'pi pi-times',
  'appointment.created': 'pi pi-calendar-plus',
  'appointment.confirmed': 'pi pi-calendar-check',
  'appointment.cancelled': 'pi pi-calendar-times',
  'appointment.no_show': 'pi pi-exclamation-triangle',
  'tenant.whatsapp_configured': 'pi pi-whatsapp',
  'tenant.openai_key_changed': 'pi pi-cog',
  'ai_agent.created': 'pi pi-android',
  'ai_agent.updated': 'pi pi-android',
  'ai_agent.deleted': 'pi pi-android',
};

@Component({
  selector: 'app-audit-activity',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, TableModule, DropdownModule, CalendarModule, ButtonModule, ToastModule],
  providers: [MessageService],
  template: `
    <p-toast />
    <div class="audit-page">
      <div class="audit-page__header">
        <h1>Atividade Recente</h1>
        <p>Registro de ações críticas no sistema. Para análises avançadas, use o Metabase.</p>
      </div>

      <div class="audit-filters">
        <p-dropdown
          [options]="eventOptions"
          [(ngModel)]="selectedEvent"
          optionLabel="label"
          optionValue="value"
          placeholder="Filtrar por evento"
          [showClear]="true"
          (onChange)="onFilter()"
          class="audit-filters__event">
        </p-dropdown>

        <p-calendar
          [(ngModel)]="dateRange"
          selectionMode="range"
          [readonlyInput]="true"
          placeholder="Período"
          dateFormat="dd/mm/yy"
          (onSelect)="onFilter()"
          class="audit-filters__date">
        </p-calendar>

        <button pButton label="Limpar filtros" icon="pi pi-filter-slash"
          class="p-button-outlined p-button-sm"
          (click)="clearFilters()">
        </button>
      </div>

      <p-table
        [value]="logs()"
        [loading]="loading()"
        [lazy]="true"
        [totalRecords]="total()"
        [rows]="20"
        [paginator]="true"
        [rowsPerPageOptions]="[20, 50, 100]"
        (onLazyLoad)="onLazyLoad($event)"
        styleClass="p-datatable-sm">

        <ng-template pTemplate="header">
          <tr>
            <th style="width:2rem"></th>
            <th>Evento</th>
            <th>Ator</th>
            <th>Alvo</th>
            <th>Data</th>
          </tr>
        </ng-template>

        <ng-template pTemplate="body" let-log>
          <tr>
            <td>
              <i [class]="iconFor(log)" style="font-size:1.1rem;color:var(--color-text-muted)"></i>
            </td>
            <td>{{ labelFor(log) }}</td>
            <td>{{ log.actor.name || log.actor.user_id || log.actor.role }}</td>
            <td>{{ log.target?.label || log.target?.entity_type || '—' }}</td>
            <td>{{ log.timestamp | date:'dd/MM/yyyy HH:mm' }}</td>
          </tr>
        </ng-template>

        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="5" style="text-align:center;padding:2rem;color:var(--color-text-muted)">
              Nenhuma atividade encontrada.
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>
  `,
  styles: [`
    .audit-page { padding: 1.5rem; }
    .audit-page__header { margin-bottom: 1.5rem; }
    .audit-page__header h1 { margin: 0 0 0.25rem; font-size: 1.5rem; }
    .audit-page__header p { margin: 0; color: var(--color-text-muted); font-size: 0.875rem; }
    .audit-filters { display: flex; gap: 0.75rem; align-items: center; margin-bottom: 1rem; flex-wrap: wrap; }
    .audit-filters__event { min-width: 14rem; }
    .audit-filters__date { min-width: 14rem; }
  `],
})
export class AuditActivityComponent implements OnInit {
  private readonly auditService = inject(AuditService);
  private readonly messageService = inject(MessageService);

  logs    = signal<AuditLogDto[]>([]);
  loading = signal(false);
  total   = signal(0);
  page    = 1;
  perPage = 20;

  eventOptions = EVENT_OPTIONS;
  selectedEvent: string | null = null;
  dateRange: Date[] | null = null;

  ngOnInit() {
    this.load();
  }

  onLazyLoad(event: { first: number; rows: number }) {
    this.page    = Math.floor(event.first / event.rows) + 1;
    this.perPage = event.rows;
    this.load();
  }

  onFilter() {
    this.page = 1;
    this.load();
  }

  clearFilters() {
    this.selectedEvent = null;
    this.dateRange = null;
    this.page = 1;
    this.load();
  }

  iconFor(log: AuditLogDto): string {
    return EVENT_ICONS[log.event] ?? 'pi pi-info-circle';
  }

  labelFor(log: AuditLogDto): string {
    const opt = EVENT_OPTIONS.find(o => o.value === log.event);
    return opt?.label ?? log.event;
  }

  private load() {
    this.loading.set(true);
    const filters: Record<string, string | number> = { page: this.page, per_page: this.perPage };
    if (this.selectedEvent) filters['event'] = this.selectedEvent;
    if (this.dateRange?.[0]) filters['from'] = this.dateRange[0].toISOString();
    if (this.dateRange?.[1]) filters['to']   = this.dateRange[1].toISOString();

    this.auditService.getAuditLogs(filters).subscribe({
      next: result => {
        this.logs.set(result.data);
        this.total.set(result.meta.total);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.messageService.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao carregar atividade.' });
      },
    });
  }
}
