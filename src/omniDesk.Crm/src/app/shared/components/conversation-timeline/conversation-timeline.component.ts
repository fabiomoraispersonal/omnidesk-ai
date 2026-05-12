// Spec 009 US2 — Shared chronological conversation timeline.
// Reusable by Live Chat Inbox and Ticket Detail.
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { ConversationMessage } from '../../../features/tickets-kanban/services/tickets.service';

export { ConversationMessage };

@Component({
  selector: 'app-conversation-timeline',
  standalone: true,
  imports: [CommonModule, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="timeline" role="log" aria-live="polite" aria-label="Histórico da conversa">
      @for (msg of messages(); track msg.id) {
        <!-- System messages — centered -->
        @if (msg.sender_type === 'system') {
          <div class="msg-system">
            <span class="system-content">{{ msg.content }}</span>
            <time
              [dateTime]="msg.sent_at"
              class="msg-time"
            >{{ msg.sent_at | date:'shortTime' }}</time>
          </div>
        } @else {
          <!-- Visitor = left; attendant/ai_agent = right -->
          <div
            class="msg-bubble-wrapper"
            [class.right]="msg.sender_type !== 'visitor'"
            [class.left]="msg.sender_type === 'visitor'"
          >
            <div class="msg-meta">
              <span class="sender-name">{{ senderLabel(msg) }}</span>
            </div>
            <div
              class="msg-bubble"
              [class.bubble-visitor]="msg.sender_type === 'visitor'"
              [class.bubble-attendant]="msg.sender_type === 'attendant'"
              [class.bubble-ai]="msg.sender_type === 'ai_agent'"
            >
              @if (msg.content) {
                <p class="bubble-text">{{ msg.content }}</p>
              }
              @if (msg.attachment_url) {
                <a
                  [href]="msg.attachment_url"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="attachment-link"
                >
                  📎 Ver anexo
                </a>
              }
            </div>
            <time
              [dateTime]="msg.sent_at"
              class="msg-time"
            >{{ msg.sent_at | date:'shortTime' }}</time>
          </div>
        }
      } @empty {
        <p class="empty">Nenhuma mensagem ainda.</p>
      }
    </div>
  `,
  styles: [`
    .timeline {
      display: flex;
      flex-direction: column;
      gap: 12px;
      padding: 12px 16px;
      overflow-y: auto;
      flex: 1;
    }

    /* System message */
    .msg-system {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 2px;
    }
    .system-content {
      font-size: 11px;
      color: #7A7A7A;
      background: #EDE7DF;
      border-radius: 999px;
      padding: 2px 12px;
    }

    /* Bubble wrapper */
    .msg-bubble-wrapper {
      display: flex;
      flex-direction: column;
      max-width: 70%;
    }
    .msg-bubble-wrapper.left  { align-self: flex-start; align-items: flex-start; }
    .msg-bubble-wrapper.right { align-self: flex-end;   align-items: flex-end; }

    .msg-meta { margin-bottom: 2px; }
    .sender-name { font-size: 11px; color: #7A7A7A; font-weight: 600; }

    .msg-bubble {
      border-radius: 12px;
      padding: 8px 12px;
      font-size: 13px;
      line-height: 1.5;
      word-break: break-word;
    }
    .bubble-visitor   { background: #fff; border: 1px solid #e0e0e0; color: #2F2F2F; border-bottom-left-radius: 2px; }
    .bubble-attendant { background: #6F7D5C; color: #fff; border-bottom-right-radius: 2px; }
    .bubble-ai        { background: #EDE7DF; color: #2F2F2F; border-bottom-right-radius: 2px; }

    .bubble-text { margin: 0; white-space: pre-wrap; }

    .attachment-link {
      display: block;
      margin-top: 4px;
      font-size: 12px;
      color: inherit;
      opacity: 0.85;
    }

    .msg-time {
      font-size: 10px;
      color: #7A7A7A;
      margin-top: 2px;
    }

    .empty {
      text-align: center;
      color: #7A7A7A;
      font-size: 13px;
      padding: 24px;
    }
  `],
})
export class ConversationTimelineComponent {
  readonly messages = input<ConversationMessage[]>([]);

  senderLabel(msg: ConversationMessage): string {
    if (msg.sender_name) return msg.sender_name;
    switch (msg.sender_type) {
      case 'visitor':   return 'Visitante';
      case 'attendant': return 'Atendente';
      case 'ai_agent':  return 'Agente de IA';
      case 'system':    return 'Sistema';
      default:          return '';
    }
  }
}
