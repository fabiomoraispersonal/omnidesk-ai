import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { Role, RoleSignal, isAtLeast } from './role.signal';

export function roleGuard(minimum: Role): CanActivateFn {
  return () => {
    const signal = inject(RoleSignal);
    const router = inject(Router);
    if (isAtLeast(signal.role(), minimum)) return true;
    return router.createUrlTree(['/login']);
  };
}
