import { HttpInterceptorFn, HttpRequest, HttpHandlerFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { TokenService } from '../../core/services/token.service';
import { AuthService } from '../../core/services/auth.service';

export const authInterceptor: HttpInterceptorFn = (req: HttpRequest<unknown>, next: HttpHandlerFn) => {
  const tokenService = inject(TokenService);
  const authService = inject(AuthService);

  const addBearer = (request: HttpRequest<unknown>) => {
    const token = tokenService.token();
    if (!token) return request;
    return request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  };

  return next(addBearer(req)).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !req.url.includes('/api/auth/')
      ) {
        return authService.restoreSession().pipe(
          switchMap(restored => {
            if (!restored) return throwError(() => error);
            return next(addBearer(req));
          }),
          catchError(() => throwError(() => error))
        );
      }
      return throwError(() => error);
    })
  );
};
