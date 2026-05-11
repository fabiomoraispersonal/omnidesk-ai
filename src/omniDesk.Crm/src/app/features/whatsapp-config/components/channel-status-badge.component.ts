import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TagModule } from 'primeng/tag';
import { WhatsAppChannelStatus } from '../services/whatsapp-config.types';

/**
 * Spec 008 US2 — badge visual do estado do canal WhatsApp.
 *  - 🔴 not_configured       — credenciais ausentes
 *  - 🟡 configured_inactive  — credenciais salvas, is_enabled=false
 *  - 🟢 active               — is_enabled=true
 */
@Component({
  selector: 'app-channel-status-badge',
  standalone: true,
  imports: [CommonModule, TagModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-tag [severity]="severity()" [value]="label()"></p-tag>
  `,
})
export class ChannelStatusBadgeComponent {
  readonly status = input.required<WhatsAppChannelStatus | null>();

  readonly severity = computed<'danger' | 'warning' | 'success' | 'secondary'>(() => {
    const s = this.status();
    if (s === 'active') return 'success';
    if (s === 'configured_inactive') return 'warning';
    if (s === 'not_configured') return 'danger';
    return 'secondary';
  });

  readonly label = computed(() => {
    const s = this.status();
    if (s === 'active') return '🟢 Ativo';
    if (s === 'configured_inactive') return '🟡 Configurado / Inativo';
    if (s === 'not_configured') return '🔴 Não configurado';
    return '— Carregando';
  });
}
