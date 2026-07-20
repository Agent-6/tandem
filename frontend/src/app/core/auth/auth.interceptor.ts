import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.getToken();

  if (token) {
    req = req.clone({
      setHeaders: { Authorization: `Bearer ${token}` },
    });
  }

  console.log(`[AuthInterceptor] ${req.method} ${req.url}`);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      console.error(`[AuthInterceptor] Error ${error.status} on ${req.method} ${req.url}`, error);
      if (error.status === 401 && !req.url.includes('/auth/')) {
        // Refresh tokens not implemented, just logout
        auth.logout();
        return throwError(() => error);
      }
      return throwError(() => error);
    }),
  );
};
