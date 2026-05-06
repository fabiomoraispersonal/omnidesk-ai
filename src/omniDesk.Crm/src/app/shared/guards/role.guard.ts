import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';

export const roleGuard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  const allowedRoles = route.data['roles'] as string[] | undefined;
  const user = authService.currentUser();

  if (!user || !allowedRoles || !allowedRoles.includes(user.role)) {
    return of(router.createUrlTree(['/acesso-negado']));
  }

  return of(true);
};
