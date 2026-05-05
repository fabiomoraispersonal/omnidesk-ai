import { inject, Injectable } from '@angular/core';
import { format, toZonedTime, fromZonedTime } from 'date-fns-tz';
import { ptBR } from 'date-fns/locale';
import { TenantContextService } from './tenant-context.service';

@Injectable({ providedIn: 'root' })
export class DateDisplayService {
  private readonly tenantCtx = inject(TenantContextService);

  toDisplay(utcIso: string | null | undefined, fmt = 'dd/MM/yyyy HH:mm'): string {
    if (!utcIso) return '';
    try {
      const tz = this.tenantCtx.timezone();
      const zoned = toZonedTime(new Date(utcIso), tz);
      return format(zoned, fmt, { locale: ptBR, timeZone: tz });
    } catch {
      return '';
    }
  }

  toDisplayDate(utcIso: string | null | undefined): string {
    return this.toDisplay(utcIso, 'dd/MM/yyyy');
  }

  toDisplayTime(utcIso: string | null | undefined): string {
    return this.toDisplay(utcIso, 'HH:mm');
  }

  toUtc(local: Date): string {
    const tz = this.tenantCtx.timezone();
    return fromZonedTime(local, tz).toISOString();
  }
}
