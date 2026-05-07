import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { ToastModule } from 'primeng/toast';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { MessageService, ConfirmationService } from 'primeng/api';
import { TenantService } from '../services/tenant.service';
import { TenantDetail, TenantStatus } from '../models/tenant.models';

type TagSeverity = 'success' | 'warn' | 'danger' | 'info' | 'secondary' | 'contrast';

@Component({
  selector: 'app-tenant-detail',
  standalone: true,
  imports: [CommonModule, CardModule, TagModule, ButtonModule, ToastModule, ConfirmDialogModule],
  providers: [MessageService, ConfirmationService],
  template: `
    <p-toast />
    <p-confirmDialog />

    @if (tenant()) {
      <div class="flex items-center justify-between mb-4">
        <div>
          <h2 class="text-xl font-semibold">{{ tenant()!.nome_fantasia || tenant()!.razao_social }}</h2>
          <span class="text-gray-500 text-sm">{{ tenant()!.slug }}</span>
        </div>
        <p-tag [value]="tenant()!.status" [severity]="statusSeverity(tenant()!.status)" />
      </div>

      <div class="grid grid-cols-2 gap-4">
        <p-card header="Dados Cadastrais">
          <dl class="grid grid-cols-2 gap-2 text-sm">
            <dt class="font-medium">CNPJ</dt><dd>{{ tenant()!.cnpj }}</dd>
            <dt class="font-medium">Razão Social</dt><dd>{{ tenant()!.razao_social }}</dd>
            <dt class="font-medium">Fuso Horário</dt><dd>{{ tenant()!.timezone }}</dd>
            <dt class="font-medium">OpenAI</dt>
            <dd>{{ tenant()!.has_openai_key ? '✅ Configurada' : '⚙️ Usando global' }}</dd>
            <dt class="font-medium">Criado em</dt><dd>{{ tenant()!.created_at | date:'dd/MM/yyyy HH:mm' }}</dd>
          </dl>
        </p-card>

        <p-card header="Contatos">
          @for (c of tenant()!.contacts; track c.id) {
            <div class="mb-3">
              <div class="font-medium capitalize">{{ c.type }}</div>
              <div>{{ c.name }}</div>
              <div class="text-sm text-gray-500">{{ c.email }} · {{ c.phone }}</div>
            </div>
          }
        </p-card>
      </div>

      @if (tenant()!.provisioning_error_log) {
        <p-card header="Erro de Provisionamento" class="mt-4">
          <pre class="text-sm text-red-600 overflow-auto max-h-60">{{ tenant()!.provisioning_error_log }}</pre>
        </p-card>
      }

      <div class="flex gap-2 mt-4">
        <p-button label="Acessar Ambiente" icon="pi pi-external-link"
          [disabled]="tenant()!.status !== 'active'" (onClick)="impersonate()" />
        <p-button [label]="tenant()!.status === 'blocked' ? 'Desbloquear' : 'Bloquear'"
          [severity]="tenant()!.status === 'blocked' ? 'success' : 'warn'"
          (onClick)="toggleBlock()" />
        <p-button label="Redefinir Senha" icon="pi pi-key" severity="secondary"
          [disabled]="tenant()!.status !== 'active'" (onClick)="resetPassword()" />
        <p-button label="Ver Saúde" icon="pi pi-chart-bar" severity="secondary"
          (onClick)="router.navigate(['/tenants', tenant()!.id, 'health'])" />
        @if (tenant()!.status === 'error') {
          <p-button label="Retentar Provisionamento" severity="warn" (onClick)="retry()" />
        }
        <p-button label="Voltar" severity="secondary" (onClick)="router.navigate(['/tenants'])" />
      </div>
    }
  `,
})
export class TenantDetailComponent implements OnInit {
  protected readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly tenantService = inject(TenantService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);

  protected readonly tenant = signal<TenantDetail | null>(null);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.tenantService.getTenantDetail(id).subscribe({
      next: (t) => this.tenant.set(t),
    });
  }

  protected statusSeverity(status: TenantStatus): TagSeverity {
    const map: Record<TenantStatus, TagSeverity> = {
      active: 'success', blocked: 'danger', provisioning: 'info', error: 'warn',
    };
    return map[status] ?? 'secondary';
  }

  protected impersonate(): void {
    this.tenantService.impersonateTenant(this.tenant()!.id).subscribe({
      next: (res) => window.open(res.redirect_url, '_blank'),
    });
  }

  protected toggleBlock(): void {
    const t = this.tenant()!;
    const isBlocked = t.status === 'blocked';
    this.confirmationService.confirm({
      message: isBlocked ? `Desbloquear ${t.slug}?` : `Bloquear ${t.slug}?`,
      accept: () => {
        const action$ = isBlocked
          ? this.tenantService.unblockTenant(t.id)
          : this.tenantService.blockTenant(t.id);
        action$.subscribe({ next: () => this.tenantService.getTenantDetail(t.id).subscribe(updated => this.tenant.set(updated)) });
      },
    });
  }

  protected resetPassword(): void {
    this.confirmationService.confirm({
      message: `Redefinir a senha do Super Admin?`,
      accept: () => {
        this.tenantService.resetSuperAdminPassword(this.tenant()!.id).subscribe({
          next: () => this.messageService.add({ severity: 'success', summary: 'Senha redefinida e enviada por e-mail.' }),
        });
      },
    });
  }

  protected retry(): void {
    this.tenantService.retryProvisioning(this.tenant()!.id).subscribe({
      next: () => this.messageService.add({ severity: 'info', summary: 'Provisionamento reiniciado.' }),
    });
  }
}
