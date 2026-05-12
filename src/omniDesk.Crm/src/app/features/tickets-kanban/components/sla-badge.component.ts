// Spec 009 US2 — colored SLA status badge that ticks every second.
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
import { SlaInfo } from '../services/tickets.service';

@Component({
  selector: 'app-sla-badge',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="sla-badge"
      [class]="badgeClass()"
      [title]="badgeTitle()"
      aria-label="SLA status"
    >
      <span class="dot" aria-hidden="true"></span>
      @if (labelText()) {
        <span class="label">{{ labelText() }}</span>
      }
    </span>
  `,
  styles: [`
    .sla-badge {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      font-size: 11px;
      font-weight: 600;
      padding: 2px 6px;
      border-radius: 999px;
    }
    .dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      flex-shrink: 0;
    }
    .sla-ok   { background: #e8f5e9; color: #2e7d32; }
    .sla-ok .dot { background: #4caf50; }
    .sla-warning { background: #fff8e1; color: #f57f17; }
    .sla-warning .dot { background: #ffc107; }
    .sla-breached { background: #ffebee; color: #b71c1c; }
    .sla-breached .dot { background: #f44336; }
  `],
})
export class SlaBadgeComponent implements OnInit, OnDestroy {
  readonly sla = input<SlaInfo | null>(null);

  /** Tick signal — updated every second to force recalculation of countdown. */
  private readonly tick = signal(Date.now());
  private intervalId: ReturnType<typeof setInterval> | null = null;

  ngOnInit(): void {
    this.intervalId = setInterval(() => this.tick.set(Date.now()), 1000);
  }

  ngOnDestroy(): void {
    if (this.intervalId != null) clearInterval(this.intervalId);
  }

  readonly badgeClass = computed(() => {
    void this.tick(); // subscribe to tick so computed re-runs every second
    const s = this.sla();
    if (!s) return '';
    return `sla-badge sla-${s.status}`;
  });

  readonly badgeTitle = computed(() => {
    void this.tick();
    const s = this.sla();
    if (!s) return '';
    const deadline = s.resolution_deadline_effective ?? s.first_response_deadline;
    if (!deadline) return `SLA: ${s.status}`;
    const ms = new Date(deadline).getTime() - Date.now();
    if (ms <= 0) return 'SLA vencido';
    const totalMin = Math.floor(ms / 60_000);
    const h = Math.floor(totalMin / 60);
    const m = totalMin % 60;
    return h > 0 ? `Restam ${h}h ${m}min` : `Restam ${m}min`;
  });

  readonly labelText = computed(() => {
    void this.tick();
    const s = this.sla();
    if (!s) return '';
    const deadline = s.resolution_deadline_effective ?? s.first_response_deadline;
    if (!deadline) return '';
    const ms = new Date(deadline).getTime() - Date.now();
    if (ms <= 0) return 'Vencido';
    const totalMin = Math.floor(ms / 60_000);
    const h = Math.floor(totalMin / 60);
    const m = totalMin % 60;
    return h > 0 ? `${h}h ${m}m` : `${m}m`;
  });
}
