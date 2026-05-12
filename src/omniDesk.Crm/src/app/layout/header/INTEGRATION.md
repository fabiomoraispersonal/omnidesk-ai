# Header Integration — Spec 010 US1 (T050) Deferred

When `header.component.ts` is added to this folder, integrate the notifications bell:

```ts
import { NotificationBellComponent } from '../../features/notifications/notification-bell.component';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [/* existing */, NotificationBellComponent],
  template: `
    <header class="app-header">
      <!-- existing left side: logo, breadcrumb -->
      <div class="actions">
        <app-notification-bell />
        <!-- existing user menu, theme toggle, etc. -->
      </div>
    </header>
  `,
})
export class HeaderComponent { /* ... */ }
```

The `NotificationBellComponent` already starts the WS stream on init and refreshes the unread
count, so no extra wiring is needed in the parent.

If the user clicks a notification and lands on a 404 route, that's expected (spec edge case);
the notification is still marked as read so it doesn't reappear in the badge.
