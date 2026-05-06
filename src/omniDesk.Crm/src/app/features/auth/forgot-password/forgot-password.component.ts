import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { TurnstileComponent } from '../../../shared/components/turnstile/turnstile.component';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink, TurnstileComponent],
  templateUrl: './forgot-password.component.html',
})
export class ForgotPasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  readonly turnstileToken = signal<string | null>(null);
  readonly loading = signal(false);
  readonly submitted = signal(false);

  get canSubmit(): boolean {
    return this.form.valid && this.turnstileToken() !== null && !this.loading();
  }

  onTurnstileVerified(token: string): void {
    this.turnstileToken.set(token);
  }

  onSubmit(): void {
    if (!this.canSubmit) return;

    this.loading.set(true);

    this.http.post('/api/auth/forgot-password', {
      email: this.form.getRawValue().email,
      turnstileToken: this.turnstileToken(),
    }).subscribe({
      next: () => {
        this.loading.set(false);
        this.submitted.set(true);
      },
      error: () => {
        this.loading.set(false);
        this.submitted.set(true);
      },
    });
  }
}
