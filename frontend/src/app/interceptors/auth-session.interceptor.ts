import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * Central session-expiry handling: a `401` from any protected API signs the user out once and
 * reloads into the login page, which drops every in-memory domain state and aborts background
 * queries. `401` answers from `/api/auth/*` belong to the forms there (wrong credentials and
 * friends) and never trigger this path; the interceptor never retries mutating requests.
 */
export const authSessionInterceptor: HttpInterceptorFn = (req, next) => {
  const authService: AuthService = inject(AuthService);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && !req.url.startsWith('/api/auth/')) {
        if (authService.handleSessionExpired()) {
          globalThis.location.assign('/login');
        }
      }

      return throwError(() => error);
    }),
  );
};
