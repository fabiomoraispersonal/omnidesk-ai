import { CommonModule, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { NotificationDto } from './notifications.service';

/**
 * Spec 010 US1 (T047) — single notification row.
 * Renders icon (event_type), title, body truncated at 80 chars, relative time.
 * Click emits the item so the parent can mark-as-read + navigate.
 */
@Component({
  selector: 'app-notification-item',
  standalone: true,
  imports: [CommonModule, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="notification-item"
      [class.unread]="!notification.is_read"
      (click)="onClick()">
      <span class="icon" [attr.aria-hidden]="true">{{ iconFor(notification.event_type) }}</span>
      <div class="content">
        <div class="title">{{ notification.title }}</div>
        <div class="body">{{ truncate(notification.body) }}</div>
        <div class="time">{{ notification.created_at | date:'short' }}</div>
      </div>
    </button>
  `,
  styles: [`
    .notification-item {
      display: flex; gap: 12px; padding: 12px 16px; width: 100%;
      background: transparent; border: 0; border-bottom: 1px solid var(--surface-200);
      text-align: left; cursor: pointer; font: inherit;
    }
    .notification-item.unread { background: var(--surface-50); }
    .notification-item:hover { background: var(--surface-100); }
    .icon { font-size: 20px; line-height: 1; }
    .content { display: flex; flex-direction: column; gap: 2px; flex: 1; min-width: 0; }
    .title { font-weight: 600; color: var(--text-color); }
    .body  { font-size: 13px; color: var(--text-color-secondary); overflow: hidden; text-overflow: ellipsis; }
    .time  { font-size: 11px; color: var(--text-color-secondary); }
  `],
})
export class NotificationItemComponent {
  @Input({ required: true }) notification!: NotificationDto;
  @Output() readonly itemClick = new EventEmitter<NotificationDto>();

  onClick(): void { this.itemClick.emit(this.notification); }

  truncate(s: string): string {
    if (!s) return '';
    return s.length <= 80 ? s : s.slice(0, 80) + '…';
  }

  iconFor(eventType: string): string {
    switch (eventType) {
      case 'ticket.assigned':         return '🎫';
      case 'ticket.new_message':      return '💬';
      case 'ticket.transferred_to_me':return '↪️';
      case 'ticket.sla_warning':      return '⏳';
      case 'ticket.sla_breached':     return '🔴';
      case 'ticket.client_replied':   return '↩️';
      case 'ticket.queued':           return '📥';
      case 'ticket.reminder_failed':          return '⚠️';
      case 'appointment.cancelled_by_client': return '📅';
      default:                                return '🔔';
    }
  }
}
