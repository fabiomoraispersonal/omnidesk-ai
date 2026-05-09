import { Component, DestroyRef, Input, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval } from 'rxjs';
import { TagModule } from 'primeng/tag';

export type SlaStatus = 'ok' | 'warning' | 'overdue' | 'not_configured';

export interface SlaSnapshot {
  firstResponseMinutes: number | null;
  resolutionMinutes: number | null;
  firstResponseElapsedMinutes: number | null;
  resolutionElapsedMinutes: number | null;
  firstResponseStatus: SlaStatus;
  resolutionStatus: SlaStatus;
}

@Component({
  selector: 'omni-sla-badge',
  standalone: true,
  imports: [CommonModule, TagModule],
  templateUrl: './sla-badge.component.html',
})
export class SlaBadgeComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  @Input({ required: true }) snapshot!: SlaSnapshot;

  protected readonly tick = signal(0);
  protected readonly displayed = computed(() => {
    this.tick(); // re-run on tick
    return this.pickRelevant();
  });

  ngOnInit(): void {
    // Re-render every minute so the contador stays approximately accurate without backend round-trip.
    interval(60_000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.tick.update(v => v + 1));
  }

  private pickRelevant(): { status: SlaStatus; label: string; severity: 'success' | 'warning' | 'danger' | 'secondary' } | null {
    const s = this.snapshot;
    if (!s) return null;

    // Overdue overrides; otherwise pick warning, otherwise pick first-response if configured.
    const phases: Array<{ status: SlaStatus; minutes: number | null; target: number | null; label: string }> = [
      {
        status: s.firstResponseStatus,
        minutes: s.firstResponseElapsedMinutes,
        target: s.firstResponseMinutes,
        label: 'Primeira resposta',
      },
      {
        status: s.resolutionStatus,
        minutes: s.resolutionElapsedMinutes,
        target: s.resolutionMinutes,
        label: 'Resolução',
      },
    ];

    const overdue = phases.find(p => p.status === 'overdue');
    if (overdue) return { status: 'overdue', label: `${overdue.label} atrasada`, severity: 'danger' };
    const warning = phases.find(p => p.status === 'warning');
    if (warning) {
      const remaining = (warning.target ?? 0) - (warning.minutes ?? 0);
      return { status: 'warning', label: `${warning.label}: ${remaining} min restantes`, severity: 'warning' };
    }
    const ok = phases.find(p => p.status === 'ok');
    if (ok) {
      const remaining = (ok.target ?? 0) - (ok.minutes ?? 0);
      return { status: 'ok', label: `${ok.label}: ${remaining} min restantes`, severity: 'success' };
    }
    return null;
  }
}
