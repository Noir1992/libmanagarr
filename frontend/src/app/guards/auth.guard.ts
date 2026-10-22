import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/** Absolute or protocol-relative URLs are rejected; only local application routes may be targets. */
const LOCAL_RETURN_URL_PATTERN: RegExp = /^\/(?!\/|\\)[^\s]*$/;

export const authGuard: CanActivateFn = (_route, state) => {
  const authService: AuthService = inject(AuthService);
  const router: Router = inject(Router);

  if (authService.state() === 'authenticated') {
    return true;
  }

  if (authService.state() === 'setupRequired') {
    return router.createUrlTree(['/setup']);
  }

  const returnUrl: string | null = LOCAL_RETURN_URL_PATTERN.test(state.url) ? state.url : null;

  return router.createUrlTree(['/login'], { queryParams: returnUrl === null ? {} : { returnUrl } });
};

export const loginGuard: CanActivateFn = () => {
  const authService: AuthService = inject(AuthService);
  const router: Router = inject(Router);

  if (authService.state() === 'authenticated') {
    return router.createUrlTree(['/library']);
  }

  if (authService.state() === 'setupRequired') {
    return router.createUrlTree(['/setup']);
  }

  return true;
};

export const setupGuard: CanActivateFn = () => {
  const authService: AuthService = inject(AuthService);
  const router: Router = inject(Router);

  if (authService.state() === 'authenticated') {
    return router.createUrlTree(['/library']);
  }

  if (authService.state() !== 'setupRequired') {
    return router.createUrlTree(['/login']);
  }

  return true;
};
