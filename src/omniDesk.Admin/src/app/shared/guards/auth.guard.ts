import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map, of, switchMap } from 'rxjs';
import { TokenService } from '../../core/services/token.service';
import { AuthService } from '../../core/services/auth.service';

export const authGuard: CanActivateFn = () => {
  const tokenService = inject(TokenService);
  const authService = inject(AuthService);
  const router = inject(Router);

  if (tokenService.isTokenValid()) {
    return of(true);
  }

  return authService.restoreSession().pipe(
    switchMap(restored => {
      if (restored) return of(true);
      return of(router.createUrlTree(['/login']));
    }),
    map(result => result)
  );
};
