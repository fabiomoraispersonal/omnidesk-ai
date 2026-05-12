import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnInit,
  ViewChild,
  computed,
  inject,
} from '@angular/core';
import { BadgeModule } from 'primeng/badge';
import { ButtonModule } from 'primeng/button';
import { OverlayPanelModule, OverlayPanel } from 'primeng/overlaypanel';
import { NotificationListComponent } from './notification-list.component';
import { NotificationsService } from './notifications.service';
import { NotificationStreamService } from '../../core/services/notification-stream.service';
import { WebPushService } from '../../core/services/web-push.service';

/**
 * Spec 010 US1 (T049) — bell icon + badge for the header.
 * Badge hidden when count = 0; "99+" when count >= 99.
 * Click toggles a PrimeNG OverlayPanel containing the list.
 */
@Component({
  selector: 'app-notification-bell',
  standalone: true,
  imports: [
    CommonModule, BadgeModule, ButtonModule,
    OverlayPanelModule, NotificationListComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="bell-btn"
      [attr.aria-label]="'Notificações (' + count() + ' não lidas)'"
      (click)="toggle($event)">
      <i class="pi pi-bell"></i>
      @if (count() > 0) {
        <span class="badge">{{ badgeLabel() }}</span>
      }
    </button>
    <p-overlayPanel #op [dismissable]="true" [style]="{ width: '360px' }">
      <app-notification-list />
    </p-overlayPanel>
  `,
  styles: [`
    .bell-btn { position: relative; background: transparent; border: 0; cursor: pointer;
                padding: 8px; color: var(--text-color); }
    .bell-btn .pi-bell { font-size: 20px; }
    .badge { position: absolute; top: 0; right: 0; background: var(--red-500);
             color: #fff; border-radius: 10px; padding: 0 6px; font-size: 11px;
             font-weight: 600; line-height: 16px; min-width: 16px; text-align: center; }
  `],
})
export class NotificationBellComponent implements OnInit {
  private readonly svc = inject(NotificationsService);
  private readonly stream = inject(NotificationStreamService);
  private readonly push = inject(WebPushService);

  @ViewChild('op') overlay?: OverlayPanel;

  protected readonly count = computed(() => this.svc.unreadCount());
  protected readonly badgeLabel = computed(() => {
    const c = this.count();
    return c >= 99 ? '99+' : String(c);
  });

  async ngOnInit(): Promise<void> {
    this.stream.start();
    await this.svc.refreshUnreadCount().catch(() => { /* tolerate */ });
    // Spec 010 US2 T062 — opportunistically register browser push. Idempotent;
    // does nothing if VAPID isn't configured or permission was denied.
    this.push.register().catch(() => { /* swallow — push is best-effort */ });
  }

  toggle(event: MouseEvent): void {
    this.overlay?.toggle(event);
  }
}
