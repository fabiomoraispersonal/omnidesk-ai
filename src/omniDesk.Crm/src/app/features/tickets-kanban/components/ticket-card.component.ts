// Spec 009 US2 — draggable ticket card for the Kanban board.
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  output,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkDrag } from '@angular/cdk/drag-drop';
import { formatDistanceToNow } from 'date-fns';
import { ptBR } from 'date-fns/locale';
import { TicketSummary } from '../services/tickets.service';
import { SlaBadgeComponent } from './sla-badge.component';
import { ReminderAlertBadgeComponent } from './reminder-alert-badge.component';

const CHANNEL_ICONS: Record<string, string> = {
  live_chat: '💬',
  whatsapp: '📱',
  manual: '✏️',
};

const MAX_TAGS_SHOWN = 3;
const SUBJECT_MAX_LENGTH = 60;

@Component({
  selector: 'app-ticket-card',
  standalone: true,
  imports: [CommonModule, CdkDrag, SlaBadgeComponent, ReminderAlertBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article
      class="ticket-card"
      cdkDrag
      [cdkDragData]="ticket()"
      (click)="clicked.emit(ticket().id)"
      role="button"
      [attr.aria-label]="'Ticket ' + ticket().protocol"
      tabindex="0"
      (keydown.enter)="clicked.emit(ticket().id)"
    >
      <!-- Drag placeholder -->
      <div class="drag-placeholder" *cdkDragPlaceholder></div>

      <!-- Row 1: channel + protocol + badges -->
      <div class="card-header">
        <span class="channel-icon" [title]="ticket().channel">
          {{ channelIcon() }}
        </span>
        <span class="protocol">{{ ticket().protocol }}</span>
        <div class="badges">
          <app-reminder-alert-badge [show]="ticket().has_reminder_alert" />
          <app-sla-badge [sla]="ticket().sla" />
        </div>
      </div>

      <!-- Row 2: contact name -->
      <div class="contact-name">
        {{ ticket().contact?.name ?? 'Visitante anônimo' }}
      </div>

      <!-- Row 3: subject -->
      <div class="subject" [title]="ticket().subject">
        {{ subjectTruncated() }}
      </div>

      <!-- Row 4: tags -->
      @if (ticket().tags.length > 0) {
        <div class="tags">
          @for (tag of visibleTags(); track tag) {
            <span class="tag">{{ tag }}</span>
          }
          @if (hiddenTagCount() > 0) {
            <span class="tag tag-overflow">+{{ hiddenTagCount() }}</span>
          }
        </div>
      }

      <!-- Row 5: attendant + time -->
      <div class="card-footer">
        <span class="attendant">
          👤 {{ ticket().attendant?.name ?? 'Sem atendente' }}
        </span>
        <span class="time-since">{{ timeSince() }}</span>
      </div>
    </article>
  `,
  styles: [`
    .ticket-card {
      background: #fff;
      border: 1px solid #e0e0e0;
      border-radius: 8px;
      padding: 10px 12px;
      cursor: grab;
      transition: box-shadow 0.15s ease, transform 0.15s ease;
      user-select: none;
    }
    .ticket-card:hover {
      box-shadow: 0 2px 8px rgba(0,0,0,0.12);
    }
    .ticket-card:active { cursor: grabbing; }

    .card-header {
      display: flex;
      align-items: center;
      gap: 6px;
      margin-bottom: 4px;
    }
    .channel-icon { font-size: 14px; flex-shrink: 0; }
    .protocol {
      font-size: 11px;
      font-weight: 700;
      color: #6F7D5C;
      letter-spacing: 0.3px;
    }
    .badges {
      margin-left: auto;
      display: flex;
      align-items: center;
      gap: 4px;
    }

    .contact-name {
      font-size: 13px;
      font-weight: 600;
      color: #2F2F2F;
      margin-bottom: 2px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .subject {
      font-size: 12px;
      color: #7A7A7A;
      margin-bottom: 6px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .tags {
      display: flex;
      flex-wrap: wrap;
      gap: 4px;
      margin-bottom: 6px;
    }
    .tag {
      font-size: 10px;
      padding: 1px 6px;
      border-radius: 999px;
      background: #EDE7DF;
      color: #4A563E;
      font-weight: 500;
    }
    .tag-overflow { background: #e0e0e0; color: #666; }

    .card-footer {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 11px;
      color: #7A7A7A;
    }
    .attendant { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .time-since { flex-shrink: 0; margin-left: 8px; }

    .drag-placeholder {
      background: #F4F1EC;
      border: 2px dashed #6F7D5C;
      border-radius: 8px;
      height: 80px;
    }
  `],
})
export class TicketCardComponent {
  readonly ticket = input.required<TicketSummary>();
  readonly clicked = output<string>();

  readonly channelIcon = computed(() => CHANNEL_ICONS[this.ticket().channel] ?? '🎫');

  readonly subjectTruncated = computed(() => {
    const s = this.ticket().subject;
    return s.length > SUBJECT_MAX_LENGTH ? s.slice(0, SUBJECT_MAX_LENGTH) + '…' : s;
  });

  readonly visibleTags = computed(() => this.ticket().tags.slice(0, MAX_TAGS_SHOWN));

  readonly hiddenTagCount = computed(() =>
    Math.max(0, this.ticket().tags.length - MAX_TAGS_SHOWN),
  );

  readonly timeSince = computed(() => {
    try {
      return formatDistanceToNow(new Date(this.ticket().created_at), {
        addSuffix: true,
        locale: ptBR,
      });
    } catch {
      return '';
    }
  });
}
