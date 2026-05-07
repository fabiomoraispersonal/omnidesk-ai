import { TestBed } from '@angular/core/testing';
import { computed, signal } from '@angular/core';
import { Router, UrlTree } from '@angular/router';
import { roleGuard } from './role.guard';
import { ROLES, Role, RoleSignal } from './role.signal';

describe('roleGuard()', () => {
  const roleValue = signal<Role | null>(null);
  const fakeSignal = { role: computed(() => roleValue()) };
  const router = {
    createUrlTree: jasmine.createSpy('createUrlTree').and.returnValue({} as UrlTree),
  };

  beforeEach(() => {
    roleValue.set(null);
    router.createUrlTree.calls.reset();
    TestBed.configureTestingModule({
      providers: [
        { provide: RoleSignal, useValue: fakeSignal },
        { provide: Router, useValue: router },
      ],
    });
  });

  function run(min: Role) {
    return TestBed.runInInjectionContext(() => roleGuard(min)({} as any, {} as any));
  }

  it('allows when actual >= minimum', () => {
    roleValue.set(ROLES.TenantAdmin);
    expect(run(ROLES.Supervisor)).toBeTrue();
  });

  it('redirects when actual < minimum', () => {
    roleValue.set(ROLES.Attendant);
    run(ROLES.Supervisor);
    expect(router.createUrlTree).toHaveBeenCalledWith(['/login']);
  });
});
