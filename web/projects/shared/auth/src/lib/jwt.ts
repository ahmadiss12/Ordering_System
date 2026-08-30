/**
 * Reads the claims out of a JWT payload.
 *
 * Deliberately does not verify the signature: the browser has no key, and anything it concluded
 * would be worthless anyway. The server verifies every token on every request. What this is for
 * is deciding which menu items to draw — a tampered token here shows a user buttons the API will
 * refuse to honour, which is a cosmetic problem, not a security one.
 */
export interface JwtClaims {
  sub?: string;
  email?: string;
  role?: string | string[];
  restaurant_id?: string;
  exp?: number;
}

export function decodeJwt(token: string): JwtClaims | null {
  const payload = token.split('.')[1];
  if (!payload) {
    return null;
  }

  try {
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    const json = decodeURIComponent(
      atob(padded)
        .split('')
        .map((c) => '%' + c.charCodeAt(0).toString(16).padStart(2, '0'))
        .join(''),
    );
    return JSON.parse(json) as JwtClaims;
  } catch {
    return null;
  }
}

/** Roles arrive as a single string when there is one, and an array when there are several. */
export function rolesFrom(claims: JwtClaims | null): readonly string[] {
  if (!claims?.role) {
    return [];
  }
  return Array.isArray(claims.role) ? claims.role : [claims.role];
}
