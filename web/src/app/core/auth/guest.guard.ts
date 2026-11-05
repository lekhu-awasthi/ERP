import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';

import { AuthService } from './auth.service';

/**
 * The mirror of authGuard, for the sign-in page: someone already signed in who opens /login (or
 * the bare root, which redirects there) goes to their Organizations instead of a sign-in form
 * they have no use for. The session lives in an httpOnly cookie, so after a reload the only way
 * to know is to ask /me, exactly as authGuard does.
 */
export const guestGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);
  const toOrganizations = () => router.createUrlTree(['/organizations']);

  if (authService.currentUser()) {
    return toOrganizations();
  }

  return authService.fetchCurrentUser().pipe(map((user) => (user ? toOrganizations() : true)));
};
