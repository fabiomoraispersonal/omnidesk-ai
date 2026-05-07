import { CommonModule } from '@angular/common';
import {
  Component,
  DestroyRef,
  Injector,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { interval } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { TokenService } from '../../../core/services/token.service';

@Component({
  selector: 'omni-impersonation-banner',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './impersonation-banner.component.html',
  styles: [`
    :host { display: block; }
    .impersonation-banner {
      background-color: var(--color-warning, var(--color-warning-500, #c09a4d));
      color: #1f1f1f;
      padding: 10px 16px;
      text-align: center;
      font-weight: 600;
      font-size: 0.875rem;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 16px;
      border-bottom: 1px solid rgba(0, 0, 0, 0.15);
    }
    .impersonation-banner strong { font-weight: 700; }
    .impersonation-banner button {
      background: rgba(0, 0, 0, 0.85);
      color: #fff;
      border: none;
      border-radius: 4px;
      padding: 4px 10px;
      cursor: pointer;
      font-size: 0.8rem;
      font-weight: 600;
    }
    .impersonation-banner button:hover { background: #000; }
  `],
})
export class ImpersonationBannerComponent {
  protected readonly authService = inject(AuthService);
  private readonly tokenService = inject(TokenService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly remainingSeconds = signal<number>(0);
  protected readonly remaining = computed(() => {
    const total = Math.max(0, this.remainingSeconds());
    const m = Math.floor(total / 60).toString().padStart(2, '0');
    const s = (total % 60).toString().padStart(2, '0');
    return `${m}:${s}`;
  });
  protected readonly visible = computed(() => !!this.authService.currentUser()?.isImpersonation);

  constructor() {
    interval(1_000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.recompute());

    effect(() => {
      // Re-read token when impersonation state changes.
      this.authService.currentUser();
      untracked(() => this.recompute());
    });
  }

  private recompute(): void {
    const token = this.tokenService.getToken();
    if (!token) {
      this.remainingSeconds.set(0);
      return;
    }
    const payload = this.tokenService.decodePayload(token);
    if (!payload?.['exp']) {
      this.remainingSeconds.set(0);
      return;
    }
    const expSec = Number(payload['exp']);
    const nowSec = Math.floor(Date.now() / 1000);
    this.remainingSeconds.set(Math.max(0, expSec - nowSec));
  }

  endNow(): void {
    this.authService.logout().subscribe({
      complete: () => this.router.navigate(['/login']),
      error: () => this.router.navigate(['/login']),
    });
  }
}
