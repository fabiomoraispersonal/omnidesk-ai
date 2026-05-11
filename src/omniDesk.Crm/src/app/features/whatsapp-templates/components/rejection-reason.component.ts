import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TooltipModule } from 'primeng/tooltip';

/**
 * Spec 008 US5 — exibe o motivo de rejeição da Meta. Inline + tooltip com
 * o texto completo (Meta às vezes retorna mensagens longas).
 */
@Component({
  selector: 'app-rejection-reason',
  standalone: true,
  imports: [CommonModule, TooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (reason()) {
      <div class="rejection" [pTooltip]="reason()!" tooltipPosition="top">
        <i class="pi pi-times-circle"></i>
        <span class="text">{{ truncated() }}</span>
      </div>
    }
  `,
  styles: [`
    .rejection {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.5rem 0.75rem;
      background: #FDECEC;
      border: 1px solid #F0A8A8;
      border-radius: 6px;
      color: #842020;
      font-size: 0.875rem;
      cursor: help;
    }
    .pi-times-circle { color: #B85C5C; }
    .text { flex: 1 1 auto; }
  `],
})
export class RejectionReasonComponent {
  readonly reason = input<string | null>(null);

  protected truncated(): string {
    const r = this.reason();
    if (!r) return '';
    return r.length > 80 ? r.slice(0, 80) + '…' : r;
  }
}
