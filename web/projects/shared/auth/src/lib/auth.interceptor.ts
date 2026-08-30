import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { SessionStore } from './session-store';
import { TokenRefresher } from './token-refresher';

/**
 * Endpoints that must never carry a bearer token or be retried after a 401.
 *
 * Refresh is the important one: intercepting it would mean a failed refresh triggers another
 * refresh, forever. Login and registration are here because a 401 from them is the answer, not a
 * stale token — retrying would turn "wrong password" into an infinite loop.
 */
const UNAUTHENTICATED_PATHS = [
  '/api/auth/login',
  '/api/auth/register',
  '/api/auth/refresh',
  '/api/auth/forgot-password',
  '/api/auth/reset-password',
];

/**
 * Attaches the access token, and recovers from expiry without the user noticing.
 *
 * On a 401 it asks {@link TokenRefresher} for a token — which shares a single exchange across
 * every request that asks at the same moment — then replays the original request with it. The
 * caller sees one slightly slower response instead of an error.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(SessionStore);
  const refresher = inject(TokenRefresher);

  if (UNAUTHENTICATED_PATHS.some((path) => request.url.includes(path))) {
    return next(request);
  }

  const token = session.accessToken();

  return next(token ? withBearer(request, token) : request).pipe(
    catchError((error: unknown) => {
      const isExpiredToken = error instanceof HttpErrorResponse && error.status === 401;

      // Without a refresh token there is nothing to recover with, so the 401 is the real answer.
      if (!isExpiredToken || !session.refreshToken()) {
        return throwError(() => error);
      }

      return refresher.refresh().pipe(
        switchMap((fresh) => next(withBearer(request, fresh))),
        catchError((refreshError: unknown) => {
          // The refresher has already signed the session out. Surfacing the original 401 rather
          // than the refresh failure keeps the error the caller sees about their own request.
          void refreshError;
          return throwError(() => error);
        }),
      );
    }),
  );
};

function withBearer<T>(request: HttpRequest<T>, token: string): HttpRequest<T> {
  return request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}
