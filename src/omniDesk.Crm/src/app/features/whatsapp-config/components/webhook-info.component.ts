import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageService } from 'primeng/api';

/**
 * Spec 008 US2 — exibe Webhook URL e Verify Token (read-only) para o tenant
 * copiar e configurar no Meta Business Manager. Ambos imutáveis após o
 * provisioning. contracts/whatsapp-config-api.md §4.
 */
@Component({
  selector: 'app-webhook-info',
  standalone: true,
  imports: [CommonModule, ButtonModule, InputTextModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="webhook-info">
      <h3>Webhook (copie para a Meta Business Manager)</h3>

      <div class="field">
        <label for="webhook-url">Webhook URL</label>
        <div class="copy-row">
          <input
            id="webhook-url"
            pInputText
            [value]="webhookUrl()"
            readonly
            class="w-full"
          />
          <button
            pButton
            type="button"
            icon="pi pi-copy"
            severity="secondary"
            (click)="copy(webhookUrl(), 'Webhook URL')"
            aria-label="Copiar Webhook URL"
          ></button>
        </div>
      </div>

      <div class="field">
        <label for="verify-token">Verify Token</label>
        <div class="copy-row">
          <input
            id="verify-token"
            pInputText
            [value]="verifyToken()"
            readonly
            class="w-full"
          />
          <button
            pButton
            type="button"
            icon="pi pi-copy"
            severity="secondary"
            (click)="copy(verifyToken(), 'Verify Token')"
            aria-label="Copiar Verify Token"
          ></button>
        </div>
      </div>
    </section>
  `,
  styles: [`
    .webhook-info {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }
    .copy-row {
      display: flex;
      gap: 0.5rem;
      align-items: stretch;
    }
    .copy-row input {
      flex: 1 1 auto;
      font-family: var(--font-family-mono, monospace);
      font-size: 0.875rem;
    }
  `],
})
export class WebhookInfoComponent {
  private readonly toast = inject(MessageService);

  readonly webhookUrl = input.required<string>();
  readonly verifyToken = input.required<string>();

  async copy(value: string, label: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(value);
      this.toast.add({
        severity: 'success',
        summary: 'Copiado',
        detail: `${label} copiado para a área de transferência.`,
        life: 2000,
      });
    } catch {
      this.toast.add({
        severity: 'error',
        summary: 'Falha ao copiar',
        detail: 'Copie manualmente o valor do campo.',
        life: 3000,
      });
    }
  }
}
