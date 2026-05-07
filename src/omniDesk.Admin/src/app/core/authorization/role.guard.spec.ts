import { TestBed } from '@angular/core/testing';
import { computed, signal } from '@angular/core';
import { Router, UrlTree } from '@angular/router';
import { saasAdminGuard } from './role.guard';
import { ROLES, Role, RoleSignal } from './role.signal';

describe('saasAdminGuard', () => {
  const roleValue = signal<Role | null>(null);
  const fakeRoleSignal = {
    role: computed(() => roleValue()),
    isSaasAdmin: computed(() => roleValue() === ROLES.SaasAdmin),
  };
  const router = {
    createUrlTree: jasmine.createSpy('createUrlTree').and.returnValue({} as UrlTree),
  };

  beforeEach(() => {
    roleValue.set(null);
    router.createUrlTree.calls.reset();
    TestBed.configureTestingModule({
      providers: [
        { provide: RoleSignal, useValue: fakeRoleSignal },
        { provide: Router, useValue: router },
      ],
    });
  });

  function run() {
    return TestBed.runInInjectionContext(() => saasAdminGuard({} as any, {} as any));
  }

  it('allows saas_admin', () => {
    roleValue.set(ROLES.SaasAdmin);
    expect(run()).toBeTrue();
  });

  it('redirects when role is not saas_admin', () => {
    roleValue.set(ROLES.TenantAdmin);
    expect(run()).toBeTruthy();
    expect(router.createUrlTree).toHaveBeenCalledWith(['/login']);
  });

  it('redirects when no role', () => {
    roleValue.set(null);
    run();
    expect(router.createUrlTree).toHaveBeenCalled();
  });
});
