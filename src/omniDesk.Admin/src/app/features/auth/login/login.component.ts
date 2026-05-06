import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService, LoginTotpRequiredResponse } from '../../../core/services/auth.service';
import { TurnstileComponent } from '../../../shared/components/turnstile/turnstile.component';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TurnstileComponent],
  templateUrl: './login.component.html',
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  readonly turnstileToken = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly loading = signal(false);
  readonly totpRequired = signal<string | null>(null);

  get canSubmit(): boolean {
    return this.form.valid && this.turnstileToken() !== null && !this.loading();
  }

  onTurnstileVerified(token: string): void {
    this.turnstileToken.set(token);
  }

  onSubmit(): void {
    if (!this.canSubmit) return;

    this.loading.set(true);
    this.error.set(null);

    const { email, password } = this.form.getRawValue();

    this.authService.login({
      email,
      password,
      rememberMe: false,
      turnstileToken: this.turnstileToken()!,
    }).subscribe({
      next: response => {
        this.loading.set(false);
        if ('requiresTotp' in response && response.requiresTotp) {
          this.totpRequired.set((response as LoginTotpRequiredResponse).totpSessionToken);
        } else {
          this.router.navigate(['/dashboard']);
        }
      },
      error: err => {
        this.loading.set(false);
        const status = err?.status;
        if (status === 403) {
          this.error.set('Verificação anti-bot falhou. Por favor, tente novamente.');
        } else if (status === 429) {
          this.error.set('Muitas tentativas. Aguarde 15 minutos antes de tentar novamente.');
        } else {
          this.error.set('E-mail ou senha incorretos.');
        }
      },
    });
  }
}
