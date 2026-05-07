import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ROLES, RoleSignal, isAtLeast } from './role.signal';
import { AuthService, AuthUser } from '../services/auth.service';

describe('RoleSignal (CRM)', () => {
  const userSignal = signal<AuthUser | null>(null);

  beforeEach(() => {
    userSignal.set(null);
    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: { currentUser: userSignal } }],
    });
  });

  it('exposes derived signals for hierarchy', () => {
    userSignal.set({ id: 'u', name: '', role: 'supervisor' });
    const r = TestBed.inject(RoleSignal);
    expect(r.isAtLeastSupervisor()).toBeTrue();
    expect(r.isAtLeastTenantAdmin()).toBeFalse();
  });

  it('detects impersonation', () => {
    userSignal.set({ id: 'u', name: '', role: 'saas_admin', isImpersonation: true });
    const r = TestBed.inject(RoleSignal);
    expect(r.isImpersonating()).toBeTrue();
  });
});

describe('isAtLeast()', () => {
  it('tenant_admin >= supervisor >= attendant', () => {
    expect(isAtLeast(ROLES.TenantAdmin, ROLES.Supervisor)).toBeTrue();
    expect(isAtLeast(ROLES.Supervisor, ROLES.Attendant)).toBeTrue();
    expect(isAtLeast(ROLES.Attendant, ROLES.Supervisor)).toBeFalse();
  });

  it('saas_admin is outside the CRM hierarchy', () => {
    expect(isAtLeast(ROLES.SaasAdmin, ROLES.TenantAdmin)).toBeFalse();
    expect(isAtLeast(ROLES.TenantAdmin, ROLES.SaasAdmin)).toBeFalse();
  });

  it('null actual returns false', () => {
    expect(isAtLeast(null, ROLES.Attendant)).toBeFalse();
  });
});
