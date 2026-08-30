import { Injectable, inject } from '@angular/core';
import { AuthClient } from 'api-client';
import { Observable, catchError, finalize, map, shareReplay, tap, throwError } from 'rxjs';
import { SessionStore } from './session-store';

/**
 * Exchanges the refresh token for a new pair, and guarantees that only one exchange is ever in
 * flight.
 *
 * That guarantee is the whole reason this class exists. The server rotates refresh tokens and
 * treats a re-presented one as theft: using a spent token revokes every session descended from
 * that login. So if three requests expire at the same moment and each starts its own refresh,
 * two of them replay a token the first has already spent, and the user is signed out for doing
 * nothing wrong. Sharing one in-flight exchange turns that from a race into a queue.
 *
 * The backend being strict is exactly what makes this necessary — see the auth notes in
 * ARCHITECTURE.md ADR-10.
 */
@Injectable({ providedIn: 'root' })
export class TokenRefresher {
  private readonly authClient = inject(AuthClient);
  private readonly session = inject(SessionStore);

  /** The exchange currently in flight, or null when none is. */
  private inFlight: Observable<string> | null = null;

  /**
   * Returns an access token, refreshing if necessary. Concurrent callers all receive the same
   * exchange rather than starting one each.
   */
  refresh(): Observable<string> {
    if (this.inFlight) {
      return this.inFlight;
    }

    const refreshToken = this.session.refreshToken();
    if (!refreshToken) {
      return throwError(() => new Error('No refresh token available.'));
    }

    this.inFlight = this.authClient.refresh({ refreshToken }).pipe(
      tap((tokens) => this.session.signIn(tokens)),
      map((tokens) => tokens.accessToken ?? ''),
      catchError((error: unknown) => {
        // The refresh token is spent, revoked or expired. There is nothing left to retry with,
        // so the session ends here rather than every subsequent request failing in confusion.
        this.session.signOut();
        return throwError(() => error);
      }),
      finalize(() => {
        this.inFlight = null;
      }),
      // refCount stays false so that a subscriber arriving after the exchange completes still
      // receives the token, instead of triggering a second one.
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    return this.inFlight;
  }
}
