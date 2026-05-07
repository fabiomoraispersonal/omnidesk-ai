import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { ToggleButtonModule } from 'primeng/togglebutton';
import { ToastModule } from 'primeng/toast';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { MessageService, ConfirmationService } from 'primeng/api';
import { FormsModule } from '@angular/forms';
import { AgentTemplateService } from '../services/agent-template.service';
import { AgentTemplate } from '../../tenants/models/tenant.models';

@Component({
  selector: 'app-agent-template-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, TableModule, TagModule, ButtonModule,
    ToggleButtonModule, ToastModule, ConfirmDialogModule,
  ],
  providers: [MessageService, ConfirmationService],
  template: `
    <p-toast />
    <p-confirmDialog />

    <div class="flex items-center justify-between mb-4">
      <h2 class="text-xl font-semibold">Templates de Agentes</h2>
      <div class="flex gap-2">
        <p-toggleButton [(ngModel)]="activeOnly" onLabel="Apenas ativos" offLabel="Todos"
          (onChange)="loadTemplates()" />
        <p-button label="Novo Template" icon="pi pi-plus"
          (onClick)="router.navigate(['/agent-templates/new'])" />
      </div>
    </div>

    <p-table [value]="templates()" [loading]="loading()" dataKey="id">
      <ng-template pTemplate="header">
        <tr>
          <th>Nome</th>
          <th>Tipo</th>
          <th>Status</th>
          <th>Usos</th>
          <th>Ações</th>
        </tr>
      </ng-template>
      <ng-template pTemplate="body" let-t>
        <tr>
          <td>
            <div class="font-medium">{{ t.name }}</div>
            <div class="text-sm text-gray-500 truncate max-w-xs">{{ t.description }}</div>
          </td>
          <td>
            <p-tag [value]="t.type === 'orchestrator' ? 'Orchestrator' : 'Sub-Agent'"
              [severity]="t.type === 'orchestrator' ? 'info' : 'secondary'" />
          </td>
          <td>
            <p-tag [value]="t.is_active ? 'Ativo' : 'Inativo'"
              [severity]="t.is_active ? 'success' : 'warn'" />
          </td>
          <td>{{ t.used_in_provisioning_count }}</td>
          <td>
            <div class="flex gap-1">
              <p-button icon="pi pi-pencil" size="small" severity="secondary"
                (onClick)="router.navigate(['/agent-templates', t.id, 'edit'])" />
              <p-button icon="pi pi-trash" size="small" severity="danger"
                (onClick)="deleteTemplate(t)" />
            </div>
          </td>
        </tr>
      </ng-template>
    </p-table>
  `,
})
export class AgentTemplateListComponent implements OnInit {
  protected readonly router = inject(Router);
  private readonly templateService = inject(AgentTemplateService);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);

  protected readonly templates = signal<AgentTemplate[]>([]);
  protected readonly loading = signal(false);
  protected activeOnly = false;

  ngOnInit(): void { this.loadTemplates(); }

  protected loadTemplates(): void {
    this.loading.set(true);
    this.templateService.getTemplates(this.activeOnly || undefined).subscribe({
      next: (data) => { this.templates.set(data); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  protected deleteTemplate(template: AgentTemplate): void {
    this.confirmationService.confirm({
      message: `Desativar o template "${template.name}"?`,
      accept: () => {
        this.templateService.deleteTemplate(template.id).subscribe({
          next: () => { this.messageService.add({ severity: 'success', summary: 'Template desativado.' }); this.loadTemplates(); },
          error: () => this.messageService.add({ severity: 'error', summary: 'Erro ao desativar template.' }),
        });
      },
    });
  }
}
