// Spec 009 US2 — tiny badge that shows a warning icon when has_reminder_alert is true.
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-reminder-alert-badge',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (show()) {
      <span class="reminder-badge" title="Lembrete ativo" aria-label="Lembrete ativo">
        &#9888;
      </span>
    }
  `,
  styles: [`
    .reminder-badge {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      color: #f57f17;
      font-size: 14px;
      line-height: 1;
    }
  `],
})
export class ReminderAlertBadgeComponent {
  readonly show = input<boolean>(false);
}
