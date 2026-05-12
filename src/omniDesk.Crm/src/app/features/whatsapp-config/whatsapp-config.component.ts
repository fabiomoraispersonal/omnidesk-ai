import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastModule } from 'primeng/toast';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { MessageService } from 'primeng/api';
import { HttpErrorResponse } from '@angular/common/http';
import { ChannelStatusBadgeComponent } from './components/channel-status-badge.component';
import { WebhookInfoComponent } from './components/webhook-info.component';
import { CredentialsFormComponent } from './components/credentials-form.component';
import { WhatsAppConfigService } from './services/whatsapp-config.service';
import {
  ApiError,
  UpdateWhatsAppConfigRequest,
} from './services/whatsapp-config.types';
import { RoleSignal, ROLES } from '../../core/authorization/role.signal';

/**
 * Spec 008 US2 — tela CRM → Configurações → WhatsApp.
 *
 * RBAC visível:
 *  - tenant_admin: edita credenciais + toggle.
 *  - supervisor: visualiza status + webhook info (read-only).
 *  - attendant: rota gated em outro lugar (não chega aqui).
 *
 * Fluxo:
 *  1. Carrega config via service.
 *  2. Tenant admin preenche credenciais → PUT /api/whatsapp/config.
 *  3. Toggle "Ativar canal" → PATCH /toggle. Backend valida com /me Meta;
 *     422 INVALID_TOKEN ou WHATSAPP_NOT_CONFIGURED são exibidos como toast.
 */
@Component({
  selector: 'app-whatsapp-config',
  standalone: true,
  imports: [
    CommonModule,
    ToastModule,
    CardModule,
    ButtonModule,
    MessageModule,
    ChannelStatusBadgeComponent,
    WebhookInfoComponent,
    CredentialsFormComponent,
  ],
  providers: [MessageService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="whatsapp-config">
      <p-toast></p-toast>

      <header class="page-header">
        <div class="title-row">
          <h1>WhatsApp Business</h1>
          <app-channel-status-badge [status]="service.channelStatus()"></app-channel-status-badge>
        </div>

        @if (!readOnly()) {
          <button
            pButton
            type="button"
            [label]="service.isEnabled() ? 'Desativar canal' : 'Ativar canal'"
            [icon]="service.isEnabled() ? 'pi pi-power-off' : 'pi pi-check'"
            [severity]="service.isEnabled() ? 'danger' : 'success'"
            [disabled]="service.saving() || service.loading()"
            [loading]="service.saving()"
            (click)="onToggle()"
          ></button>
        }
      </header>

      @if (readOnly()) {
        <p-message
          severity="info"
          text="Apenas o tenant_admin pode editar credenciais e ativar/desativar o canal. Você está em modo leitura."
        ></p-message>
      }

      @if (service.loading() && !service.config()) {
        <p-message severity="secondary" text="Carregando configuração..."></p-message>
      }

      @if (service.config(); as cfg) {
        <p-card header="Webhook (Meta Business Manager)">
          <app-webhook-info
            [webhookUrl]="cfg.webhook_url"
            [verifyToken]="cfg.webhook_verify_token"
          ></app-webhook-info>
        </p-card>

        <p-card header="Credenciais Meta">
          <app-credentials-form
            [config]="cfg"
            [readOnly]="readOnly()"
            [saving]="service.saving()"
            (submitted)="onSave($event)"
          ></app-credentials-form>
        </p-card>
      }
    </section>
  `,
  styles: [`
    .whatsapp-config {
      display: flex;
      flex-direction: column;
      gap: 1.5rem;
      padding: 1.5rem;
      max-width: 900px;
      margin: 0 auto;
    }
    .page-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: 1rem;
    }
    .title-row {
      display: flex;
      align-items: center;
      gap: 1rem;
    }
    h1 {
      margin: 0;
      font-size: 1.5rem;
    }
  `],
})
export class WhatsAppConfigComponent implements OnInit {
  protected readonly service = inject(WhatsAppConfigService);
  private readonly roleSignal = inject(RoleSignal);
  private readonly toast = inject(MessageService);

  protected readonly readOnly = computed(() => this.roleSignal.role() !== ROLES.TenantAdmin);

  async ngOnInit(): Promise<void> {
    try {
      await this.service.load();
    } catch (err) {
      this.handleError(err, 'Não foi possível carregar a configuração.');
    }
  }

  async onSave(payload: UpdateWhatsAppConfigRequest): Promise<void> {
    try {
      await this.service.save(payload);
      this.toast.add({
        severity: 'success',
        summary: 'Salvo',
        detail: 'Credenciais atualizadas com sucesso.',
        life: 3000,
      });
    } catch (err) {
      this.handleError(err, 'Não foi possível salvar.');
    }
  }

  async onToggle(): Promise<void> {
    const target = !this.service.isEnabled();
    try {
      const result = await this.service.toggle(target);
      this.toast.add({
        severity: 'success',
        summary: result.is_enabled ? 'Canal ativado' : 'Canal desativado',
        detail: result.is_enabled
          ? 'O canal WhatsApp está agora ativo.'
          : 'O canal foi desativado.',
        life: 3000,
      });
    } catch (err) {
      this.handleError(err, target ? 'Não foi possível ativar o canal.' : 'Não foi possível desativar.');
    }
  }

  private handleError(err: unknown, fallback: string): void {
    let summary = 'Erro';
    let detail = fallback;

    if (err instanceof HttpErrorResponse && err.error?.error) {
      const apiErr = err.error.error as ApiError;
      summary = this.errorSummary(apiErr.code);
      detail = apiErr.message ?? fallback;
    }

    this.toast.add({
      severity: 'error',
      summary,
      detail,
      life: 5000,
    });
  }

  private errorSummary(code: string): string {
    switch (code) {
      case 'WHATSAPP_NOT_CONFIGURED':
        return 'Configuração incompleta';
      case 'INVALID_TOKEN':
        return 'Access Token inválido';
      case 'VALIDATION_ERROR':
        return 'Validação falhou';
      case 'FORBIDDEN':
        return 'Acesso negado';
      default:
        return 'Erro';
    }
  }
}
