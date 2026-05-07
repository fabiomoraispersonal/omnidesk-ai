import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { catchError, map, of } from 'rxjs';

interface MePermissionsResponse {
  permissions: string[];
}

export function permissionGuard(policyName: string): CanActivateFn {
  return () => {
    const http = inject(HttpClient);
    const router = inject(Router);
    return http.get<MePermissionsResponse>('/api/me/permissions').pipe(
      map(resp => resp?.permissions?.includes(policyName) ?? false),
      map(allowed => allowed ? true : router.createUrlTree(['/login'])),
      catchError(() => of(router.createUrlTree(['/login']))),
    );
  };
}
