import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators, AbstractControl } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { AuthService, LoginResponse } from '../../../core/services/auth.service';
import { TokenService } from '../../../core/services/token.service';

function passwordsMatch(control: AbstractControl): Record<string, boolean> | null {
  const password = control.get('password')?.value;
  const confirm = control.get('confirmPassword')?.value;
  if (password && confirm && password !== confirm) {
    return { passwordsMismatch: true };
  }
  return null;
}

@Component({
  selector: 'app-accept-invite',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './accept-invite.component.html',
})
export class AcceptInviteComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly authService = inject(AuthService);
  private readonly tokenService = inject(TokenService);

  private token = '';

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', Validators.required],
  }, { validators: passwordsMatch });

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.token = this.route.snapshot.queryParamMap.get('token') ?? '';
    if (!this.token) {
      this.error.set('Link de convite inválido ou expirado.');
    }
  }

  onSubmit(): void {
    if (this.form.invalid || !this.token) return;

    this.loading.set(true);
    this.error.set(null);

    const { name, password } = this.form.getRawValue();

    this.http.post<LoginResponse>('/api/auth/accept-invite', {
      token: this.token,
      name,
      password,
    }).subscribe({
      next: response => {
        this.loading.set(false);
        this.tokenService.setToken(response.accessToken);
        this.authService.currentUser.set(response.user);
        this.router.navigate(['/dashboard']);
      },
      error: err => {
        this.loading.set(false);
        const code = err?.error?.code;
        if (code === 'invalid_token') {
          this.error.set('Convite inválido ou expirado.');
        } else if (code === 'password_too_short') {
          this.error.set('A senha deve ter pelo menos 8 caracteres.');
        } else {
          this.error.set('Ocorreu um erro. Por favor, tente novamente.');
        }
      },
    });
  }
}
