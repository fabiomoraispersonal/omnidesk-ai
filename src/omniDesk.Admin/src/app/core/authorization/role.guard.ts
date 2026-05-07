import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { RoleSignal, ROLES } from './role.signal';

export const saasAdminGuard: CanActivateFn = () => {
  const role = inject(RoleSignal);
  const router = inject(Router);
  if (role.role() === ROLES.SaasAdmin) return true;
  return router.createUrlTree(['/login']);
};
