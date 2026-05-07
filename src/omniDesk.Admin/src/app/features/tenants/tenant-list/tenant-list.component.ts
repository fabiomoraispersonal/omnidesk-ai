import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { MessageService, ConfirmationService } from 'primeng/api';
import { TenantService } from '../services/tenant.service';
import { TenantSummary, TenantStatus } from '../models/tenant.models';

type TagSeverity = 'success' | 'warn' | 'danger' | 'info' | 'secondary' | 'contrast';

@Component({
  selector: 'app-tenant-list',
  standalone: true,
  imports: [
    CommonModule, TableModule, TagModule, ButtonModule, ToastModule, ConfirmDialogModule,
  ],
  providers: [MessageService, ConfirmationService],
  template: `
    <p-toast />
    <p-confirmDialog />

    <div class="flex items-center justify-between mb-4">
      <h2 class="text-xl font-semibold">Tenants</h2>
      <p-button label="Novo Tenant" icon="pi pi-plus" (onClick)="router.navigate(['/tenants/new'])" />
    </div>

    <p-table [value]="tenants()" [loading]="loading()" [paginator]="true" [rows]="20" dataKey="id">
      <ng-template pTemplate="header">
        <tr>
          <th>Nome / Slug</th>
          <th>CNPJ</th>
          <th>Status</th>
          <th>Postgres</th>
          <th>Redis</th>
          <th>MongoDB</th>
          <th>Chats (30d)</th>
          <th>Tickets</th>
          <th>Usuários</th>
          <th>OpenAI</th>
          <th>Ações</th>
        </tr>
      </ng-template>
      <ng-template pTemplate="body" let-t>
        <tr>
          <td>
            <div class="font-medium">{{ t.nome_fantasia || t.razao_social }}</div>
            <div class="text-sm text-gray-500">{{ t.slug }}</div>
          </td>
          <td>{{ t.cnpj }}</td>
          <td><p-tag [value]="t.status" [severity]="statusSeverity(t.status)" /></td>
          <td><span [class]="t.metrics?.postgres?.connected ? 'text-green-600' : 'text-red-600'">
            {{ t.metrics ? (t.metrics.postgres.connected ? '✅' : '❌') : '--' }}
          </span></td>
          <td><span [class]="t.metrics?.redis?.connected ? 'text-green-600' : 'text-red-600'">
            {{ t.metrics ? (t.metrics.redis.connected ? '✅' : '❌') : '--' }}
          </span></td>
          <td><span [class]="t.metrics?.mongodb?.connected ? 'text-green-600' : 'text-red-600'">
            {{ t.metrics ? (t.metrics.mongodb.connected ? '✅' : '❌') : '--' }}
          </span></td>
          <td>{{ t.metrics?.conversations_last_30d ?? '--' }}</td>
          <td>{{ t.metrics?.open_tickets ?? '--' }}</td>
          <td>{{ t.metrics?.active_users ?? '--' }}</td>
          <td>{{ t.has_openai_key ? '✅ Própria' : '⚙️ Global' }}</td>
          <td>
            <div class="flex gap-1">
              <p-button icon="pi pi-eye" size="small" severity="secondary"
                (onClick)="router.navigate(['/tenants', t.id])" pTooltip="Ver detalhes" />
              <p-button icon="pi pi-external-link" size="small" severity="info"
                [disabled]="t.status !== 'active'" (onClick)="impersonate(t)" pTooltip="Acessar ambiente" />
              <p-button [icon]="t.status === 'blocked' ? 'pi pi-lock-open' : 'pi pi-lock'" size="small"
                [severity]="t.status === 'blocked' ? 'success' : 'warn'"
                (onClick)="toggleBlock(t)" [pTooltip]="t.status === 'blocked' ? 'Desbloquear' : 'Bloquear'" />
              <p-button icon="pi pi-key" size="small" severity="secondary"
                [disabled]="t.status !== 'active'" (onClick)="resetPassword(t)" pTooltip="Redefinir senha" />
            </div>
          </td>
        </tr>
      </ng-template>
    </p-table>
  `,
})
export class TenantListComponent implements OnInit {
  protected readonly router = inject(Router);
  private readonly tenantService = inject(TenantService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);

  protected readonly tenants = signal<TenantSummary[]>([]);
  protected readonly loading = signal(false);

  ngOnInit(): void {
    this.loadTenants();
  }

  private loadTenants(): void {
    this.loading.set(true);
    this.tenantService.getTenants().subscribe({
      next: (data) => { this.tenants.set(data); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  protected statusSeverity(status: TenantStatus): TagSeverity {
    const map: Record<TenantStatus, TagSeverity> = {
      active: 'success', blocked: 'danger', provisioning: 'info', error: 'warn',
    };
    return map[status] ?? 'secondary';
  }

  protected impersonate(tenant: TenantSummary): void {
    this.tenantService.impersonateTenant(tenant.id).subscribe({
      next: (res) => window.open(res.redirect_url, '_blank'),
      error: () => this.messageService.add({ severity: 'error', summary: 'Erro ao impersonar tenant.' }),
    });
  }

  protected toggleBlock(tenant: TenantSummary): void {
    const isBlocked = tenant.status === 'blocked';
    this.confirmationService.confirm({
      message: isBlocked
        ? `Desbloquear o tenant ${tenant.slug}?`
        : `Bloquear o tenant ${tenant.slug}? As sessões ativas serão invalidadas.`,
      accept: () => {
        const action$ = isBlocked
          ? this.tenantService.unblockTenant(tenant.id)
          : this.tenantService.blockTenant(tenant.id);
        action$.subscribe({ next: () => this.loadTenants() });
      },
    });
  }

  protected resetPassword(tenant: TenantSummary): void {
    this.confirmationService.confirm({
      message: `Redefinir a senha do Super Admin de ${tenant.slug}?`,
      accept: () => {
        this.tenantService.resetSuperAdminPassword(tenant.id).subscribe({
          next: () => this.messageService.add({ severity: 'success', summary: 'Senha redefinida e enviada por e-mail.' }),
        });
      },
    });
  }
}
