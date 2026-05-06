import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-impersonation-banner',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './impersonation-banner.component.html',
  styles: [`
    .impersonation-banner {
      background-color: var(--color-warning-500, #f59e0b);
      color: var(--color-neutral-900, #111827);
      padding: var(--spacing-2, 8px) var(--spacing-4, 16px);
      text-align: center;
      font-weight: 600;
      font-size: var(--font-size-sm, 0.875rem);
    }
  `],
})
export class ImpersonationBannerComponent {
  protected readonly authService = inject(AuthService);
}
