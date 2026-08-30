import { Injectable, computed, signal } from '@angular/core';
import { JwtClaims, decodeJwt, rolesFrom } from './jwt';

const REFRESH_TOKEN_KEY = 'ordering.refreshToken';

/**
 * The part of an auth response this store actually uses.
 *
 * Narrower than the generated `AuthTokensResponse` on purpose: the expiry timestamps are for the
 * caller to reason about, not for storage, and requiring them here would mean anything handing
 * tokens to the store — including a test — had to invent values it does not care about.
 */
export interface IssuedTokens {
  accessToken?: string | null;
  refreshToken?: string | null;
}

/**
 * Who is signed in, and the two tokens that say so.
 *
 * The access token lives in memory only. Putting it in localStorage would make it readable by
 * any script that gets injected into the page, and it buys nothing: it expires in fifteen
 * minutes, and the refresh token can mint a new one after a reload.
 *
 * The refresh token does go to localStorage, because a session that ends every time someone
 * reloads the page is not a session. An httpOnly cookie would be safer, and is unavailable to
 * us: the same API serves a React Native app, where cookies are awkward and inconsistent, and
 * one token scheme across all three clients is worth more than the cookie's advantage. Rotation
 * with reuse detection on the server is what covers the gap — a stolen refresh token can be used
 * once, and using it ends every session for that login.
 */
@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly accessTokenSignal = signal<string | null>(null);
  private readonly refreshTokenSignal = signal<string | null>(readStoredRefreshToken());

  readonly accessToken = this.accessTokenSignal.asReadonly();
  readonly refreshToken = this.refreshTokenSignal.asReadonly();

  readonly claims = computed<JwtClaims | null>(() => {
    const token = this.accessTokenSignal();
    return token ? decodeJwt(token) : null;
  });

  readonly isAuthenticated = computed(() => this.accessTokenSignal() !== null);
  readonly email = computed(() => this.claims()?.email ?? null);
  readonly roles = computed(() => rolesFrom(this.claims()));
  readonly restaurantId = computed(() => this.claims()?.restaurant_id ?? null);

  /**
   * True when a refresh token exists but no access token does — the state right after a page
   * reload. Guards must wait for the refresh rather than redirecting to the login page, or every
   * reload throws the user out.
   */
  readonly canRestore = computed(
    () => this.accessTokenSignal() === null && this.refreshTokenSignal() !== null,
  );

  hasAnyRole(...roles: readonly string[]): boolean {
    const held = this.roles();
    return roles.some((role) => held.includes(role));
  }

  signIn(tokens: IssuedTokens): void {
    this.accessTokenSignal.set(tokens.accessToken ?? null);
    this.setRefreshToken(tokens.refreshToken ?? null);
  }

  signOut(): void {
    this.accessTokenSignal.set(null);
    this.setRefreshToken(null);
  }

  private setRefreshToken(token: string | null): void {
    this.refreshTokenSignal.set(token);

    // Wrapped because storage throws outright in some privacy modes, and a sign-in failing
    // because a browser declined to remember it would be a strange way to lose a user.
    try {
      if (token) {
        localStorage.setItem(REFRESH_TOKEN_KEY, token);
      } else {
        localStorage.removeItem(REFRESH_TOKEN_KEY);
      }
    } catch {
      // Session simply will not survive a reload. Everything else still works.
    }
  }
}

function readStoredRefreshToken(): string | null {
  try {
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  } catch {
    return null;
  }
}
