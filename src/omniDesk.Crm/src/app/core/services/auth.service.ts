import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, map, of, tap } from 'rxjs';
import { TokenService } from './token.service';

export interface AuthUser {
  id: string;
  name: string;
  role: string;
  tenantSlug?: string;
  isImpersonation?: boolean;
}

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
  turnstileToken: string;
}

export interface LoginResponse {
  accessToken: string;
  user: AuthUser;
}

export interface LoginTotpRequiredResponse {
  requiresTotp: boolean;
  totpSessionToken: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly tokenService = inject(TokenService);

  readonly currentUser = signal<AuthUser | null>(null);

  login(req: LoginRequest): Observable<LoginResponse | LoginTotpRequiredResponse> {
    return this.http.post<LoginResponse | LoginTotpRequiredResponse>('/api/auth/login', {
      email: req.email,
      password: req.password,
      rememberMe: req.rememberMe,
      turnstileToken: req.turnstileToken,
    }).pipe(
      tap(response => {
        if (!('requiresTotp' in response && response.requiresTotp)) {
          const loginResp = response as LoginResponse;
          this.tokenService.setToken(loginResp.accessToken);
          this.currentUser.set(loginResp.user);
        }
      })
    );
  }

  restoreSession(): Observable<boolean> {
    const searchParams = new URLSearchParams(window.location.search);
    const impersonationToken = searchParams.get('token');

    if (impersonationToken) {
      this.tokenService.setToken(impersonationToken);
      const payload = this.tokenService.decodePayload(impersonationToken);
      if (payload) {
        this.currentUser.set({
          id: payload['sub'],
          name: '',
          role: payload['role'],
          tenantSlug: payload['tenant_slug'],
          isImpersonation: payload['impersonation'] === 'true',
        });
        history.replaceState(null, '', window.location.pathname);
        return of(true);
      }
    }

    return this.http.post<LoginResponse>('/api/auth/refresh', null).pipe(
      tap(response => {
        this.tokenService.setToken(response.accessToken);
        const payload = this.tokenService.decodePayload(response.accessToken);
        if (payload) {
          this.currentUser.set({
            id: payload['sub'],
            name: '',
            role: payload['role'],
            tenantSlug: payload['tenant_slug'],
          });
        }
      }),
      map(() => true),
      catchError(() => of(false))
    );
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', null).pipe(
      tap(() => {
        this.tokenService.clearToken();
        this.currentUser.set(null);
      }),
      catchError(() => {
        this.tokenService.clearToken();
        this.currentUser.set(null);
        return of(undefined);
      })
    );
  }
}
