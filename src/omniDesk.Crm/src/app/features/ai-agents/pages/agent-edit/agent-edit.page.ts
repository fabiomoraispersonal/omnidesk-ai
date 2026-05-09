import { ChangeDetectionStrategy, Component, OnInit, ViewChild, ElementRef, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { SelectModule } from 'primeng/select';
import { ToastModule } from 'primeng/toast';
import { InputSwitchModule } from 'primeng/inputswitch';
import { MessageService } from 'primeng/api';
import { AiAgentsService } from '../../data-access/ai-agents.service';
import { AiAgentDetail, CreateAiAgentRequest } from '../../data-access/ai-agents.types';
import { PromptVariablesHelperComponent } from '../../shared/prompt-variables-helper.component';
import { PlaygroundPaneComponent } from '../../shared/playground-pane.component';

interface DepartmentOption { id: string; name: string; }

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
    InputSwitchModule,
    PromptVariablesHelperComponent,
    PlaygroundPaneComponent,
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

  @ViewChild('promptInput') promptInput?: ElementRef<HTMLTextAreaElement>;

  readonly agent = signal<AiAgentDetail | null>(null);
  readonly mode = signal<'edit' | 'create'>('edit');
  readonly saving = signal(false);
  readonly departments = signal<DepartmentOption[]>([]);

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    short_description: [''],
    department_id: [''],
    prompt: ['', [Validators.required, Validators.minLength(10)]],
    model: ['gpt-4o', Validators.required],
    is_active: [true],
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id === 'new') {
      this.mode.set('create');
      // For new sub-agent: department_id and short_description are required.
      this.form.controls.department_id.addValidators(Validators.required);
      this.form.controls.short_description.addValidators([Validators.required, Validators.maxLength(300)]);
      // Defer department fetch to a follow-up integration with the existing departments service.
      // Stub left as empty — UI shows a textbox until integration is wired.
      return;
    }
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
          department_id: a.department_id ?? '',
          prompt: a.prompt,
          model: a.model,
          is_active: a.is_active,
        });
        if (a.type === 'orchestrator') {
          this.form.controls.short_description.disable();
          this.form.controls.department_id.disable();
          this.form.controls.is_active.disable();
        }
      },
      error: () => this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Não foi possível carregar o agente.' }),
    });
  }

  save(): void {
    if (this.form.invalid) return;
    this.saving.set(true);
    const v = this.form.getRawValue();

    if (this.mode() === 'create') {
      const body: CreateAiAgentRequest = {
        name: v.name,
        short_description: v.short_description,
        prompt: v.prompt,
        model: v.model,
        department_id: v.department_id,
      };
      this.service.create(body).subscribe({
        next: (r) => {
          this.saving.set(false);
          this.toast.add({ severity: 'success', summary: 'Criado', detail: 'Sub-agente criado.' });
          this.router.navigate(['/configuracoes/agentes-de-ia', r.id]);
        },
        error: () => {
          this.saving.set(false);
          this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao criar sub-agente.' });
        },
      });
      return;
    }

    const a = this.agent();
    if (!a) { this.saving.set(false); return; }

    this.service.update(a.id, v).subscribe({
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

  isCreate(): boolean {
    return this.mode() === 'create';
  }

  modelOptions(): { label: string; value: string }[] {
    return (this.agent()?.available_models_for_tenant ?? ['gpt-4o', 'gpt-4o-mini']).map((m) => ({ label: m, value: m }));
  }

  insertVariable(variable: string): void {
    const ta = this.promptInput?.nativeElement;
    if (!ta) {
      this.form.patchValue({ prompt: (this.form.controls.prompt.value ?? '') + variable });
      return;
    }
    const start = ta.selectionStart ?? ta.value.length;
    const end = ta.selectionEnd ?? ta.value.length;
    const next = ta.value.slice(0, start) + variable + ta.value.slice(end);
    this.form.patchValue({ prompt: next });
    queueMicrotask(() => {
      ta.focus();
      const cursor = start + variable.length;
      ta.setSelectionRange(cursor, cursor);
    });
  }
}
