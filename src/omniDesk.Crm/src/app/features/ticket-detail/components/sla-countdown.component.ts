// Spec 009 US3 — T119
// Countdown display for ticket detail side panel: "Restam: 1h 23min" or "Vencido".
import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  computed,
  input,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { SlaInfo } from '../../tickets-kanban/services/tickets.service';
import { formatDistanceToNowStrict, isPast, differenceInSeconds } from 'date-fns';
import { ptBR } from 'date-fns/locale';

@Component({
  selector: 'app-sla-countdown',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (sla()) {
      <div class="sla-countdown" [class]="containerClass()">
        <span class="sla-label">{{ label() }}</span>
        <span class="sla-time" [class.breached]="isBreached()">{{ timeText() }}</span>
        @if (slaType()) {
          <span class="sla-type-badge">{{ slaTypeLabel() }}</span>
        }
      </div>
    }
  `,
  styles: [`
    .sla-countdown {
      display: flex;
      flex-direction: column;
      gap: 2px;
      padding: 8px 10px;
      border-radius: 8px;
      font-size: 13px;
    }
    .sla-ok      { background: #e8f5e9; border: 1px solid #c8e6c9; }
    .sla-warning { background: #fff8e1; border: 1px solid #ffecb3; }
    .sla-breached { background: #ffebee; border: 1px solid #ffcdd2; }

    .sla-label { font-size: 11px; color: #7a7a7a; text-transform: uppercase; letter-spacing: 0.5px; }

    .sla-time {
      font-size: 16px;
      font-weight: 700;
      color: #2f2f2f;
    }
    .sla-time.breached { color: #b71c1c; }

    .sla-type-badge {
      font-size: 10px;
      color: #7a7a7a;
    }
  `],
})
export class SlaCountdownComponent implements OnInit, OnDestroy {
  readonly sla = input<SlaInfo | null>(null);

  private readonly tick = signal(Date.now());
  private intervalId: ReturnType<typeof setInterval> | null = null;

  ngOnInit(): void {
    this.intervalId = setInterval(() => this.tick.set(Date.now()), 1000);
  }

  ngOnDestroy(): void {
    if (this.intervalId != null) clearInterval(this.intervalId);
  }

  private readonly effectiveDeadline = computed((): Date | null => {
    const s = this.sla();
    if (!s) return null;
    const raw = s.resolution_deadline_effective ?? s.first_response_deadline;
    return raw ? new Date(raw) : null;
  });

  readonly isBreached = computed((): boolean => {
    void this.tick();
    const d = this.effectiveDeadline();
    return d != null && isPast(d);
  });

  readonly containerClass = computed((): string => {
    const s = this.sla();
    if (!s) return '';
    return `sla-countdown sla-${s.status}`;
  });

  readonly label = computed((): string => {
    void this.tick();
    return this.isBreached() ? 'SLA vencido há' : 'Restam';
  });

  readonly timeText = computed((): string => {
    void this.tick();
    const d = this.effectiveDeadline();
    if (!d) return '—';

    const now = new Date();
    const secs = Math.abs(differenceInSeconds(d, now));
    const totalMin = Math.floor(secs / 60);
    const h = Math.floor(totalMin / 60);
    const m = totalMin % 60;
    const s = secs % 60;

    if (h > 0) return `${h}h ${m}min`;
    if (totalMin > 0) return `${m}min ${s}s`;
    return `${s}s`;
  });

  readonly slaType = computed((): string | null => {
    const s = this.sla();
    if (!s) return null;
    // Show which SLA type is driving the countdown
    if (s.first_response_deadline && !s.first_response_at) return 'first_response';
    return 'resolution';
  });

  readonly slaTypeLabel = computed((): string => {
    return this.slaType() === 'first_response' ? 'Primeira resposta' : 'Resolução';
  });
}
