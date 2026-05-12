import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  OnInit,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { NotificationItemComponent } from './notification-item.component';
import { NotificationDto, NotificationsService } from './notifications.service';

/**
 * Spec 010 US1 (T048) — paginated panel of notifications.
 * Infinite scroll: 20 per page, fetches next page on scroll-bottom.
 * Click on item: mark-as-read + navigate. Button: mark all as read.
 */
@Component({
  selector: 'app-notification-list',
  standalone: true,
  imports: [CommonModule, ButtonModule, NotificationItemComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="notif-panel">
      <header class="notif-header">
        <h3>Notificações</h3>
        <p-button
          label="Marcar todas como lidas"
          severity="secondary"
          [text]="true"
          size="small"
          (click)="markAll()" />
      </header>
      <div #scrollContainer class="notif-list" (scroll)="onScroll()">
        @for (n of svc.items(); track n.id) {
          <app-notification-item [notification]="n" (itemClick)="onClick(n)" />
        } @empty {
          <p class="empty">Nenhuma notificação.</p>
        }
        @if (loading()) { <p class="loading">Carregando…</p> }
      </div>
    </div>
  `,
  styles: [`
    .notif-panel { display: flex; flex-direction: column; width: 360px; max-height: 480px; }
    .notif-header { display: flex; align-items: center; justify-content: space-between;
                    padding: 12px 16px; border-bottom: 1px solid var(--surface-200); }
    .notif-header h3 { margin: 0; font-size: 14px; }
    .notif-list { overflow-y: auto; }
    .empty, .loading { padding: 24px 16px; color: var(--text-color-secondary); text-align: center; }
  `],
})
export class NotificationListComponent implements OnInit {
  readonly svc = inject(NotificationsService);
  private readonly router = inject(Router);

  @ViewChild('scrollContainer') scrollContainer?: ElementRef<HTMLDivElement>;
  protected readonly loading = signal(false);
  private page = 1;
  private hasMore = true;

  async ngOnInit(): Promise<void> {
    this.page = 1;
    await this.loadPage(1);
  }

  private async loadPage(page: number): Promise<void> {
    if (this.loading()) return;
    this.loading.set(true);
    try {
      const { items, total } = await this.svc.fetchPage(page, 20, false);
      this.hasMore = (page * 20) < total;
    } catch {
      this.hasMore = false;
    } finally {
      this.loading.set(false);
    }
  }

  async onScroll(): Promise<void> {
    if (!this.hasMore || this.loading()) return;
    const el = this.scrollContainer?.nativeElement;
    if (!el) return;
    const nearBottom = el.scrollTop + el.clientHeight >= el.scrollHeight - 64;
    if (nearBottom) {
      this.page += 1;
      await this.loadPage(this.page);
    }
  }

  async onClick(n: NotificationDto): Promise<void> {
    try {
      if (!n.is_read) await this.svc.markAsRead(n.id);
    } finally {
      const target = n.entity_type === 'ticket'
        ? `/tickets/${n.entity_id}`
        : `/conversations/${n.entity_id}`;
      this.router.navigateByUrl(target).catch(() => { /* leave UI to handle 404 */ });
    }
  }

  async markAll(): Promise<void> {
    await this.svc.markAllAsRead();
  }
}
