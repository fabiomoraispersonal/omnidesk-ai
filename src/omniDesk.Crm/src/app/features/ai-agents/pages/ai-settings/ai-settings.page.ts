import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { InputNumberModule } from 'primeng/inputnumber';
import { MultiSelectModule } from 'primeng/multiselect';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { AiSettingsService, AiSettingsView } from '../../data-access/ai-settings.service';

@Component({
  selector: 'app-ai-settings-page',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, CardModule, ButtonModule,
    InputTextModule, InputNumberModule, MultiSelectModule, ToastModule,
  ],
  providers: [MessageService],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="ai-settings-page" *ngIf="settings() as s">
      <p-toast></p-toast>
      <header class="page-header">
        <h1>Configurações Avançadas — Agentes de IA</h1>
        <p class="muted">Janela de contexto, modelos disponíveis e credenciais OpenAI.</p>
      </header>

      <p-card header="Contexto enviado à IA">
        <form [formGroup]="form" (ngSubmit)="saveSettings()">
          <div class="field">
            <label for="ctx">Mensagens de histórico (5–100)</label>
            <p-inputNumber id="ctx" formControlName="contextWindowMessages" [min]="5" [max]="100" [showButtons]="true"></p-inputNumber>
            <small class="muted">Mais mensagens = mais contexto, mais tokens consumidos.</small>
          </div>
          <div class="field">
            <label for="models">Modelos OpenAI habilitados (vazio = usar global)</label>
            <p-multiSelect id="models" [options]="modelOptions(s)" formControlName="availableModels"
                           optionLabel="label" optionValue="value" display="chip" [filter]="false"></p-multiSelect>
          </div>
          <div class="actions">
            <p-button type="submit" label="Salvar" [loading]="saving()"></p-button>
          </div>
        </form>
      </p-card>

      <p-card header="Credenciais OpenAI">
        <p *ngIf="s.openai_credentials.key_set" class="muted">
          Chave atual: <code>{{ s.openai_credentials.key_preview }}</code>
          <span *ngIf="s.openai_credentials.organization"> · Org: <code>{{ s.openai_credentials.organization }}</code></span>
        </p>
        <p *ngIf="!s.openai_credentials.key_set" class="muted">
          Sem chave própria — sistema usa a chave global.
        </p>
        <form [formGroup]="credentialsForm" (ngSubmit)="saveKey()">
          <div class="field">
            <label for="apiKey">Nova API key</label>
            <input pInputText id="apiKey" type="password" formControlName="apiKey" autocomplete="new-password" />
            <small class="muted">Formato: sk-...</small>
          </div>
          <div class="field">
            <label for="org">Organization (opcional)</label>
            <input pInputText id="org" formControlName="organization" />
          </div>
          <div class="field">
            <label for="proj">Project (opcional)</label>
            <input pInputText id="proj" formControlName="project" />
          </div>
          <div class="actions">
            <p-button *ngIf="s.openai_credentials.key_set"
                      type="button" label="Remover chave atual" severity="danger" [text]="true"
                      (onClick)="deleteKey()"></p-button>
            <p-button type="submit" label="Cadastrar chave" [loading]="savingKey()"></p-button>
          </div>
        </form>
      </p-card>
    </section>
  `,
  styles: [`
    .ai-settings-page { display: flex; flex-direction: column; gap: 1rem; }
    .field { display: flex; flex-direction: column; gap: .25rem; margin-bottom: .75rem; }
    .actions { display: flex; gap: .5rem; justify-content: flex-end; }
    .muted { color: var(--text-color-secondary, #888); }
  `],
})
export class AiSettingsPage implements OnInit {
  private readonly service = inject(AiSettingsService);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(MessageService);

  readonly settings = signal<AiSettingsView | null>(null);
  readonly saving = signal(false);
  readonly savingKey = signal(false);

  readonly form = this.fb.nonNullable.group({
    contextWindowMessages: [20, [Validators.required, Validators.min(5), Validators.max(100)]],
    availableModels: [<string[]>[]],
  });

  readonly credentialsForm = this.fb.nonNullable.group({
    apiKey: [''],
    organization: [''],
    project: [''],
  });

  ngOnInit(): void { this.refresh(); }

  refresh(): void {
    this.service.get().subscribe({
      next: (s) => {
        this.settings.set(s);
        this.form.patchValue({
          contextWindowMessages: s.context_window_messages,
          availableModels: s.available_models,
        });
      },
      error: () => this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao carregar configurações.' }),
    });
  }

  saveSettings(): void {
    if (this.form.invalid) return;
    this.saving.set(true);
    const v = this.form.getRawValue();
    this.service.update({
      contextWindowMessages: v.contextWindowMessages,
      availableModels: v.availableModels,
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.add({ severity: 'success', summary: 'Salvo', detail: 'Configurações atualizadas.' });
        this.refresh();
      },
      error: () => {
        this.saving.set(false);
        this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao salvar.' });
      },
    });
  }

  saveKey(): void {
    const v = this.credentialsForm.getRawValue();
    if (!v.apiKey) return;
    this.savingKey.set(true);
    this.service.setKey({
      apiKey: v.apiKey,
      organization: v.organization || undefined,
      project: v.project || undefined,
    }).subscribe({
      next: () => {
        this.savingKey.set(false);
        this.credentialsForm.reset();
        this.toast.add({ severity: 'success', summary: 'OK', detail: 'Chave cadastrada e validada.' });
        this.refresh();
      },
      error: () => {
        this.savingKey.set(false);
        this.toast.add({ severity: 'error', summary: 'Erro', detail: 'OpenAI rejeitou a chave.' });
      },
    });
  }

  deleteKey(): void {
    this.service.deleteKey().subscribe({
      next: () => {
        this.toast.add({ severity: 'success', summary: 'OK', detail: 'Chave removida; sistema usa a chave global.' });
        this.refresh();
      },
      error: () => this.toast.add({ severity: 'error', summary: 'Erro', detail: 'Falha ao remover.' }),
    });
  }

  modelOptions(s: AiSettingsView): { label: string; value: string }[] {
    return (s.global_allowlist ?? []).map((m) => ({ label: m, value: m }));
  }
}
