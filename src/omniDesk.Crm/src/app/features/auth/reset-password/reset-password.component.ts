import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators, AbstractControl } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';

function passwordsMatch(control: AbstractControl): Record<string, boolean> | null {
  const password = control.get('newPassword')?.value;
  const confirm = control.get('confirmPassword')?.value;
  if (password && confirm && password !== confirm) {
    return { passwordsMismatch: true };
  }
  return null;
}

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './reset-password.component.html',
})
export class ResetPasswordComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);

  private token = '';

  readonly form = this.fb.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', Validators.required],
  }, { validators: passwordsMatch });

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly success = signal(false);

  ngOnInit(): void {
    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';
    if (!this.token) {
      this.error.set('Link de redefinição inválido ou expirado.');
    }
  }

  onSubmit(): void {
    if (this.form.invalid || !this.token) return;

    this.loading.set(true);
    this.error.set(null);

    this.http.post('/api/auth/reset-password', {
      token: this.token,
      newPassword: this.form.getRawValue().newPassword,
    }).subscribe({
      next: () => {
        this.loading.set(false);
        this.success.set(true);
        setTimeout(() => this.router.navigate(['/login']), 2000);
      },
      error: err => {
        this.loading.set(false);
        const code = err?.error?.code;
        if (code === 'invalid_token') {
          this.error.set('Link de redefinição inválido ou expirado.');
        } else if (code === 'password_too_short') {
          this.error.set('A senha deve ter pelo menos 8 caracteres.');
        } else {
          this.error.set('Ocorreu um erro. Por favor, tente novamente.');
        }
      },
    });
  }
}
