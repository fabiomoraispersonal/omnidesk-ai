import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { ToastModule } from 'primeng/toast';
import { ConfirmationService, MessageService } from 'primeng/api';
import { AiAgentsService } from '../../data-access/ai-agents.service';
import { AiAgentSummary } from '../../data-access/ai-agents.types';

@Component({
  selector: 'app-agents-list-page',
  standalone: true,
  imports: [
    CommonModule, RouterLink, CardModule, TagModule, ButtonModule,
    ProgressSpinnerModule, ConfirmDialogModule, ToastModule,
  ],
  providers: [ConfirmationService, MessageService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agents-list.page.html',
})
export class AgentsListPage implements OnInit {
  private readonly service = inject(AiAgentsService);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmationService);
  private readonly toast = inject(MessageService);

  readonly agents = signal<AiAgentSummary[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.refresh();
  }

  refresh(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.list(true).subscribe({
      next: (data) => {
        this.agents.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Falha ao carregar agentes.');
        this.loading.set(false);
      },
    });
  }

  edit(agent: AiAgentSummary): void {
    this.router.navigate(['/configuracoes/agentes-de-ia', agent.id]);
  }

  newSubAgent(): void {
    this.router.navigate(['/configuracoes/agentes-de-ia', 'new']);
  }

  toggleActive(agent: AiAgentSummary, event: Event): void {
    event.stopPropagation();
    this.service.toggle(agent.id, !agent.is_active).subscribe({
      next: () => this.refresh(),
      error: () => this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Não foi possível alternar status.' }),
    });
  }

  delete(agent: AiAgentSummary, event: Event): void {
    event.stopPropagation();
    this.confirm.confirm({
      message: `Excluir o sub-agente "${agent.name}"? Esta ação não pode ser desfeita.`,
      header: 'Confirmar exclusão',
      icon: 'pi pi-exclamation-triangle',
      accept: () => {
        this.service.delete(agent.id).subscribe({
          next: (r) => {
            const msg = r.soft_deleted
              ? 'Sub-agente desativado (havia histórico vinculado).'
              : 'Sub-agente excluído.';
            this.toast.add({ severity: 'success', summary: 'OK', detail: msg });
            this.refresh();
          },
          error: () => this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao excluir.' }),
        });
      },
    });
  }

  isOrchestrator(a: AiAgentSummary): boolean {
    return a.type === 'orchestrator';
  }
}
