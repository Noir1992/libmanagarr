import { HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { from, Observable, switchMap } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * Attaches the antiforgery request token to every mutating API call. The token is minted for
 * the current identity by `GET /api/auth/csrf` and cached inside the AuthService, which drops
 * it on every identity change so the next mutating call automatically renews it.
 */
export const csrfTokenInterceptor: HttpInterceptorFn = (req: HttpRequest<unknown>, next: HttpHandlerFn): Observable<HttpEvent<unknown>> => {
  if (req.method === 'GET' || !req.url.startsWith('/api/')) {
    return next(req);
  }

  const authService: AuthService = inject(AuthService);

  return from(authService.csrfTokenAsync()).pipe(
    switchMap(
      (token: string): Observable<HttpEvent<unknown>> =>
        token.length === 0 ? next(req) : next(req.clone({ setHeaders: { 'X-XSRF-TOKEN': token } })),
    ),
  );
};
