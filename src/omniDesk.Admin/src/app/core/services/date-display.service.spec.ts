import { TestBed } from '@angular/core/testing';
import { DateDisplayService } from './date-display.service';
import { TenantContextService } from './tenant-context.service';

describe('DateDisplayService', () => {
  let service: DateDisplayService;
  let tenantCtx: TenantContextService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(DateDisplayService);
    tenantCtx = TestBed.inject(TenantContextService);
  });

  describe('toDisplay', () => {
    it('converts UTC to São Paulo time (UTC-3)', () => {
      tenantCtx.setTimezone('America/Sao_Paulo');
      expect(service.toDisplay('2026-05-05T20:00:00Z')).toBe('05/05/2026 17:00');
    });

    it('converts UTC to Manaus time (UTC-4)', () => {
      tenantCtx.setTimezone('America/Manaus');
      expect(service.toDisplay('2026-05-05T20:00:00Z')).toBe('05/05/2026 16:00');
    });

    it('formats date-only when fmt is dd/MM/yyyy', () => {
      tenantCtx.setTimezone('America/Sao_Paulo');
      expect(service.toDisplay('2026-01-15T12:00:00Z', 'dd/MM/yyyy')).toBe('15/01/2026');
    });

    it('returns empty string for null input', () => {
      expect(service.toDisplay(null)).toBe('');
    });

    it('returns empty string for undefined input', () => {
      expect(service.toDisplay(undefined)).toBe('');
    });

    it('returns empty string for empty string input', () => {
      expect(service.toDisplay('')).toBe('');
    });
  });

  describe('toDisplayDate', () => {
    it('returns date-only in dd/MM/yyyy format', () => {
      tenantCtx.setTimezone('America/Sao_Paulo');
      expect(service.toDisplayDate('2026-05-05T20:00:00Z')).toBe('05/05/2026');
    });
  });

  describe('toDisplayTime', () => {
    it('returns time-only in HH:mm format', () => {
      tenantCtx.setTimezone('America/Sao_Paulo');
      expect(service.toDisplayTime('2026-05-05T20:00:00Z')).toBe('17:00');
    });
  });

  describe('toUtc', () => {
    it('converts São Paulo local date to UTC', () => {
      tenantCtx.setTimezone('America/Sao_Paulo');
      const local = new Date('2026-05-05T17:00:00');
      const utc = service.toUtc(local);
      expect(utc).toContain('2026-05-05T20:00:00');
    });
  });
});
