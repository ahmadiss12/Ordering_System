import { Injectable, inject } from '@angular/core';
import { AuthClient } from 'api-client';
import { Observable, catchError, firstValueFrom, map, of, tap } from 'rxjs';
import { SessionStore } from './session-store';
import { TokenRefresher } from './token-refresher';

/**
 * What a screen calls to sign someone in or out. Everything below it — token storage, rotation,
 * the retry on expiry — is handled by {@link SessionStore}, {@link TokenRefresher} and the
 * interceptor, so a component never touches a token.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly authClient = inject(AuthClient);
  private readonly refresher = inject(TokenRefresher);
  readonly session = inject(SessionStore);

  login(email: string, password: string): Observable<void> {
    return this.authClient.login({ email, password }).pipe(
      tap((tokens) => this.session.signIn(tokens)),
      map(() => undefined),
    );
  }

  /**
   * Ends the session locally first, then tells the server.
   *
   * That order is deliberate: if the network call fails the user is still signed out of this
   * browser, which is what they asked for. The server-side revocation is the part that can be
   * retried; the local one is the part that must not fail.
   */
  async logout(): Promise<void> {
    const refreshToken = this.session.refreshToken();
    this.session.signOut();

    if (!refreshToken) {
      return;
    }

    await firstValueFrom(
      this.authClient.logout({ refreshToken }).pipe(catchError(() => of(undefined))),
    );
  }

  /**
   * Creates an account and signs straight into it.
   *
   * No "now please log in" step: somebody who has just typed their password twice has proved they
   * know it, and asking for it again is a screen that exists only because the code was easier
   * that way.
   */
  register(email: string, password: string, fullName: string, phone: string): Observable<void> {
    return this.authClient.register({ email, password, fullName, phone }).pipe(
      tap((tokens) => this.session.signIn(tokens)),
      map(() => undefined),
    );
  }

  forgotPassword(email: string): Observable<void> {
    return this.authClient.forgotPassword({ email }).pipe(map(() => undefined));
  }

  resetPassword(token: string, newPassword: string): Observable<void> {
    return this.authClient.resetPassword({ token, newPassword }).pipe(map(() => undefined));
  }

  /**
   * Rebuilds the session after a page reload, when the refresh token survived in storage but the
   * in-memory access token did not. Resolves to whether the user ended up signed in.
   */
  async restoreSession(): Promise<boolean> {
    if (this.session.isAuthenticated()) {
      return true;
    }

    if (!this.session.canRestore()) {
      return false;
    }

    try {
      await firstValueFrom(this.refresher.refresh());
      return this.session.isAuthenticated();
    } catch {
      return false;
    }
  }
}
