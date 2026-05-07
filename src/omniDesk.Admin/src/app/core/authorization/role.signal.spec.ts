import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ROLES, RoleSignal } from './role.signal';
import { AuthService, AuthUser } from '../services/auth.service';

describe('RoleSignal', () => {
  const userSignal = signal<AuthUser | null>(null);

  beforeEach(() => {
    userSignal.set(null);
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { currentUser: userSignal } },
      ],
    });
  });

  it('returns null when no user', () => {
    const role = TestBed.inject(RoleSignal);
    expect(role.role()).toBeNull();
    expect(role.isSaasAdmin()).toBeFalse();
  });

  it('normalizes PascalCase role from claims', () => {
    userSignal.set({ id: 'u', name: 'n', role: 'SaasAdmin' });
    const role = TestBed.inject(RoleSignal);
    expect(role.role()).toBe(ROLES.SaasAdmin);
    expect(role.isSaasAdmin()).toBeTrue();
  });

  it('accepts lowercase role from claims', () => {
    userSignal.set({ id: 'u', name: 'n', role: 'tenant_admin' });
    const role = TestBed.inject(RoleSignal);
    expect(role.role()).toBe(ROLES.TenantAdmin);
    expect(role.isSaasAdmin()).toBeFalse();
  });
});
