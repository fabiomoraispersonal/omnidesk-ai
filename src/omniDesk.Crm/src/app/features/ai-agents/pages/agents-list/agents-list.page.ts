import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { AiAgentsService } from '../../data-access/ai-agents.service';
import { AiAgentSummary } from '../../data-access/ai-agents.types';

@Component({
  selector: 'app-agents-list-page',
  standalone: true,
  imports: [CommonModule, RouterLink, CardModule, TagModule, ButtonModule, ProgressSpinnerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agents-list.page.html',
})
export class AgentsListPage implements OnInit {
  private readonly service = inject(AiAgentsService);
  private readonly router = inject(Router);

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

  isOrchestrator(a: AiAgentSummary): boolean {
    return a.type === 'orchestrator';
  }
}
