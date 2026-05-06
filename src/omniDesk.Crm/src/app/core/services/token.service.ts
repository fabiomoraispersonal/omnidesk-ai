import { Injectable, signal } from '@angular/core';

interface JwtPayload {
  sub: string;
  exp: number;
  role: string;
  tenant_id?: string;
  tenant_slug?: string;
  email: string;
  impersonation?: string;
  impersonated_by?: string;
}

@Injectable({ providedIn: 'root' })
export class TokenService {
  private readonly _token = signal<string | null>(null);

  readonly token = this._token.asReadonly();

  setToken(token: string): void {
    this._token.set(token);
  }

  clearToken(): void {
    this._token.set(null);
  }

  isTokenValid(): boolean {
    const token = this._token();
    if (!token) return false;
    const payload = this.decodePayload(token);
    if (!payload) return false;
    return payload.exp > Math.floor(Date.now() / 1000);
  }

  decodePayload(token: string): JwtPayload | null {
    try {
      const parts = token.split('.');
      if (parts.length !== 3) return null;
      const raw = atob(parts[1].replace(/-/g, '+').replace(/_/g, '/'));
      return JSON.parse(raw) as JwtPayload;
    } catch {
      return null;
    }
  }
}
