import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { SelectModule } from 'primeng/select';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { AiAgentsService } from '../../data-access/ai-agents.service';
import { AiAgentDetail } from '../../data-access/ai-agents.types';

@Component({
  selector: 'app-agent-edit-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    CardModule,
    ButtonModule,
    InputTextModule,
    TextareaModule,
    SelectModule,
    ToastModule,
  ],
  providers: [MessageService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agent-edit.page.html',
})
export class AgentEditPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(AiAgentsService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);

  readonly agent = signal<AiAgentDetail | null>(null);
  readonly saving = signal(false);

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    short_description: [''],
    prompt: ['', [Validators.required, Validators.minLength(10)]],
    model: ['gpt-4o', Validators.required],
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.router.navigate(['/configuracoes/agentes-de-ia']);
      return;
    }
    this.service.get(id).subscribe({
      next: (a) => {
        this.agent.set(a);
        this.form.patchValue({
          name: a.name,
          short_description: a.short_description,
          prompt: a.prompt,
          model: a.model,
        });
      },
      error: () => this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Não foi possível carregar o agente.' }),
    });
  }

  save(): void {
    const a = this.agent();
    if (!a || this.form.invalid) return;
    this.saving.set(true);
    const payload = this.form.getRawValue();
    this.service.update(a.id, payload).subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.add({ severity: 'success', summary: 'Salvo', detail: 'Agente atualizado.' });
      },
      error: () => {
        this.saving.set(false);
        this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao salvar.' });
      },
    });
  }

  cancel(): void {
    this.router.navigate(['/configuracoes/agentes-de-ia']);
  }

  isOrchestrator(): boolean {
    return this.agent()?.type === 'orchestrator';
  }

  modelOptions(): { label: string; value: string }[] {
    return (this.agent()?.available_models_for_tenant ?? ['gpt-4o']).map((m) => ({ label: m, value: m }));
  }
}
