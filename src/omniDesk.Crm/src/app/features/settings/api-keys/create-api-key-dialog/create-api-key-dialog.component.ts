import { ChangeDetectionStrategy, Component, EventEmitter, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { ApiKeysService, CreatedApiKeyResponse } from '../api-keys.service';

@Component({
  selector: 'app-create-api-key-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, ButtonModule, DialogModule, InputTextModule],
  template: `
    <p-dialog
      [(visible)]="visible"
      [modal]="true"
      [draggable]="false"
      [resizable]="false"
      [header]="createdKey() ? 'Chave criada' : 'Nova API Key'"
      [style]="{ width: '480px' }"
      (onHide)="onClose()">

      @if (!createdKey()) {
        <div class="dialog-body">
          <label for="keyName" class="field-label">Nome descritivo</label>
          <input
            pInputText
            id="keyName"
            [(ngModel)]="name"
            placeholder="Ex: Metabase Auditoria"
            class="w-full"
            [disabled]="loading()" />
          <small class="hint">Identifica onde esta chave é usada.</small>
        </div>
        <div class="dialog-footer">
          <p-button label="Cancelar" severity="secondary" [text]="true" (click)="onClose()" [disabled]="loading()" />
          <p-button label="Criar" icon="pi pi-plus" [loading]="loading()" [disabled]="!name.trim()" (click)="create()" />
        </div>
      } @else {
        <div class="dialog-body">
          <div class="warn-banner">
            <i class="pi pi-exclamation-triangle"></i>
            <span>Copie a chave agora. Ela <strong>não será exibida novamente</strong>.</span>
          </div>
          <label class="field-label">Chave gerada</label>
          <div class="key-display">
            <code class="key-value">{{ createdKey()!.key }}</code>
            <p-button
              [icon]="copied() ? 'pi pi-check' : 'pi pi-copy'"
              [label]="copied() ? 'Copiado!' : 'Copiar'"
              severity="secondary"
              size="small"
              (click)="copyKey()" />
          </div>
        </div>
        <div class="dialog-footer">
          <p-button label="Fechar" (click)="onDone()" />
        </div>
      }
    </p-dialog>
  `,
  styles: [`
    .dialog-body { display: flex; flex-direction: column; gap: 0.5rem; padding: 0.5rem 0; }
    .dialog-footer { display: flex; justify-content: flex-end; gap: 0.5rem; padding-top: 1rem; border-top: 1px solid var(--surface-200); margin-top: 0.5rem; }
    .field-label { font-weight: 600; font-size: 0.875rem; }
    .hint { color: var(--color-text-muted); }
    .warn-banner { display: flex; align-items: center; gap: 0.5rem; background: var(--yellow-50, #fffbeb);
                   border: 1px solid var(--yellow-300, #fcd34d); border-radius: 6px; padding: 0.75rem;
                   color: var(--yellow-700, #92400e); font-size: 0.875rem; }
    .key-display { display: flex; align-items: center; gap: 0.5rem; background: var(--surface-100);
                   border: 1px solid var(--surface-300); border-radius: 6px; padding: 0.75rem; }
    .key-value { font-family: monospace; font-size: 0.75rem; flex: 1; word-break: break-all; }
  `],
})
export class CreateApiKeyDialogComponent {
  @Output() created = new EventEmitter<void>();

  private readonly service = inject(ApiKeysService);

  visible = false;
  name = '';
  loading = signal(false);
  createdKey = signal<CreatedApiKeyResponse | null>(null);
  copied = signal(false);

  open(): void {
    this.name = '';
    this.createdKey.set(null);
    this.copied.set(false);
    this.visible = true;
  }

  create(): void {
    if (!this.name.trim()) return;
    this.loading.set(true);
    this.service.createApiKey(this.name.trim()).subscribe({
      next: key => {
        this.createdKey.set(key);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  copyKey(): void {
    const key = this.createdKey()?.key;
    if (!key) return;
    navigator.clipboard.writeText(key).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    });
  }

  onClose(): void {
    this.visible = false;
  }

  onDone(): void {
    this.visible = false;
    this.created.emit();
  }
}
