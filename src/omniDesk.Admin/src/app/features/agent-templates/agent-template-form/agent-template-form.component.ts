import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, ActivatedRoute } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { SelectModule } from 'primeng/select';
import { ToggleButtonModule } from 'primeng/togglebutton';
import { CardModule } from 'primeng/card';
import { AgentTemplateService } from '../services/agent-template.service';
import { AgentTemplate, AgentType } from '../../tenants/models/tenant.models';

@Component({
  selector: 'app-agent-template-form',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    ButtonModule, InputTextModule, TextareaModule, SelectModule, ToggleButtonModule, CardModule,
  ],
  template: `
    <p-card [header]="isEdit() ? 'Editar Template' : 'Novo Template'">
      <form [formGroup]="form" (ngSubmit)="submit()">
        <div class="flex flex-col gap-4">
          <div class="flex flex-col gap-1">
            <label>Nome *</label>
            <input pInputText formControlName="name" />
          </div>
          <div class="flex flex-col gap-1">
            <label>Tipo *</label>
            <p-select formControlName="type" [options]="typeOptions" optionLabel="label" optionValue="value"
              [disabled]="isEdit()" />
          </div>
          <div class="flex flex-col gap-1">
            <label>Descrição *</label>
            <textarea pTextarea formControlName="description" rows="3"></textarea>
          </div>
          <div class="flex flex-col gap-1">
            <label>Prompt (opcional)</label>
            <textarea pTextarea formControlName="prompt" rows="6" placeholder="System prompt do agente..."></textarea>
          </div>
          @if (isEdit()) {
            <div class="flex items-center gap-2">
              <label>Ativo</label>
              <p-toggleButton formControlName="isActive" onLabel="Sim" offLabel="Não" />
            </div>
          }
        </div>

        <div class="flex gap-2 mt-6">
          <p-button [label]="isEdit() ? 'Salvar' : 'Criar'" type="submit"
            [loading]="loading()" [disabled]="form.invalid" />
          <p-button label="Cancelar" severity="secondary" (onClick)="router.navigate(['/agent-templates'])" />
        </div>
      </form>
    </p-card>
  `,
})
export class AgentTemplateFormComponent implements OnInit {
  protected readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);
  private readonly templateService = inject(AgentTemplateService);

  protected readonly loading = signal(false);
  protected readonly isEdit = signal(false);
  private templateId: string | null = null;

  protected readonly typeOptions = [
    { label: 'Orchestrator', value: 'orchestrator' },
    { label: 'Sub-Agent', value: 'sub_agent' },
  ];

  protected form = this.fb.group({
    name: ['', Validators.required],
    type: ['sub_agent' as AgentType, Validators.required],
    description: ['', Validators.required],
    prompt: [''],
    isActive: [true],
  });

  ngOnInit(): void {
    this.templateId = this.route.snapshot.paramMap.get('id');
    if (this.templateId) {
      this.isEdit.set(true);
      this.templateService.getTemplates().subscribe({
        next: (templates) => {
          const t = templates.find(x => x.id === this.templateId);
          if (t) {
            this.form.patchValue({
              name: t.name,
              type: t.type,
              description: t.description,
              prompt: t.prompt ?? '',
              isActive: t.is_active,
            });
          }
        },
      });
    }
  }

  protected submit(): void {
    if (this.form.invalid) return;
    this.loading.set(true);
    const v = this.form.value;

    if (this.isEdit() && this.templateId) {
      this.templateService.updateTemplate(this.templateId, {
        name: v.name || undefined,
        description: v.description || undefined,
        prompt: v.prompt || undefined,
        is_active: v.isActive ?? undefined,
      }).subscribe({
        next: () => this.router.navigate(['/agent-templates']),
        error: () => this.loading.set(false),
      });
    } else {
      this.templateService.createTemplate({
        name: v.name!,
        type: v.type!,
        description: v.description!,
        prompt: v.prompt || undefined,
      }).subscribe({
        next: () => this.router.navigate(['/agent-templates']),
        error: () => this.loading.set(false),
      });
    }
  }
}
