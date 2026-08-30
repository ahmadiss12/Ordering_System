import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthService } from './auth.service';
import { SessionStore } from './session-store';

/**
 * Requires a signed-in user.
 *
 * The awkward case is a page reload: the access token lived in memory and is gone, while the
 * refresh token survived in storage. Redirecting on that would throw the user out every time
 * they pressed F5, so the guard restores the session first and only then decides.
 *
 * The rejected path carries the attempted URL, so signing in returns the user where they were
 * going rather than to a dashboard they did not ask for.
 */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (await auth.restoreSession()) {
    return true;
  }

  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/**
 * Requires one of the given roles, on top of being signed in.
 *
 * This is a convenience for the user, not a security boundary: it decides which screens are
 * worth showing. Every endpoint behind them is enforced independently by an authorization policy
 * and a tenant check, so a user who reaches a screen they should not see finds it empty and gets
 * a 403 from anything they press.
 */
export function roleGuard(...roles: readonly string[]): CanActivateFn {
  return async (_route, state): Promise<boolean | UrlTree> => {
    const auth = inject(AuthService);
    const session = inject(SessionStore);
    const router = inject(Router);

    const signedIn = await auth.restoreSession();
    if (!signedIn) {
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }

    if (session.hasAnyRole(...roles)) {
      return true;
    }

    // Denial gets its own page rather than a bounce to the home route. A silent redirect
    // looks like the click did nothing, and the user retries it forever.
    // Every application using this guard must therefore route '/forbidden'.
    return router.createUrlTree(['/forbidden']);
  };
}

/**
 * Keeps a signed-in user off the login page. Without it, the back button lands on a login form
 * that immediately bounces, which reads as a broken app.
 */
export const anonymousOnlyGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return (await auth.restoreSession()) ? router.createUrlTree(['/']) : true;
};
