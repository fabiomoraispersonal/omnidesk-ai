import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class TenantContextService {
  readonly timezone = signal<string>('America/Sao_Paulo');

  setTimezone(tz: string): void {
    this.timezone.set(tz);
  }
}
