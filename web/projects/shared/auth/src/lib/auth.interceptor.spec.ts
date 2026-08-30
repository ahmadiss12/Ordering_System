import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideApiClient } from 'api-client';
import { authInterceptor } from './auth.interceptor';
import { SessionStore } from './session-store';

/**
 * The interceptor, and specifically the behaviour that makes it worth having.
 *
 * The server rotates refresh tokens and treats a re-presented one as theft: using a spent token
 * revokes every session from that login. So the dangerous case is not a single expired request —
 * it is three of them expiring together. Without a shared exchange, two would replay a token the
 * first had already spent and the user would be signed out for doing nothing wrong.
 */
describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let session: SessionStore;

  beforeEach(() => {
    localStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideApiClient(),
      ],
    });

    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    session = TestBed.inject(SessionStore);
  });

  afterEach(() => {
    backend.verify();
    localStorage.clear();
  });

  it('attaches the access token to an authenticated request', () => {
    signIn('access-1', 'refresh-1');

    http.get('/api/restaurants').subscribe();

    const request = backend.expectOne('/api/restaurants');
    expect(request.request.headers.get('Authorization')).toBe('Bearer access-1');
    request.flush({});
  });

  it('sends no Authorization header when nobody is signed in', () => {
    http.get('/api/restaurants').subscribe();

    const request = backend.expectOne('/api/restaurants');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  it('never attaches a token to the refresh endpoint', () => {
    signIn('access-1', 'refresh-1');

    http.post('/api/auth/refresh', {}).subscribe();

    const request = backend.expectOne('/api/auth/refresh');

    // Intercepting refresh would mean a failed refresh triggers another refresh, forever.
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  it('refreshes once and retries when the token has expired', async () => {
    signIn('expired', 'refresh-1');

    const result = firstResult(http.get<{ ok: boolean }>('/api/restaurants'));

    backend.expectOne('/api/restaurants').flush(null, { status: 401, statusText: 'Unauthorized' });
    await tick();

    respondToRefresh('access-2', 'refresh-2');
    await tick();

    // Replayed with the new token, not the expired one.
    const retried = backend.expectOne('/api/restaurants');
    expect(retried.request.headers.get('Authorization')).toBe('Bearer access-2');
    retried.flush({ ok: true });

    expect(await result).toEqual({ ok: true });
    expect(session.accessToken()).toBe('access-2');
  });

  it('shares one refresh across requests that expire together', async () => {
    signIn('expired', 'refresh-1');

    const results = [
      firstResult(http.get('/api/restaurants')),
      firstResult(http.get('/api/menu-items/1')),
      firstResult(http.get('/api/restaurant/categories')),
    ];

    // All three arrive at the server before any of them has recovered.
    for (const url of ['/api/restaurants', '/api/menu-items/1', '/api/restaurant/categories']) {
      backend.expectOne(url).flush(null, { status: 401, statusText: 'Unauthorized' });
    }
    await tick();

    // The point of the whole class: one exchange, not three. A second here would replay a spent
    // token and the server would end every session for this login.
    const refreshes = backend.match((r) => r.url.includes('/api/auth/refresh'));
    expect(refreshes.length).toBe(1);

    respondToRefreshRequest(refreshes[0], 'access-2', 'refresh-2');
    await tick();

    for (const url of ['/api/restaurants', '/api/menu-items/1', '/api/restaurant/categories']) {
      const retried = backend.expectOne(url);
      expect(retried.request.headers.get('Authorization')).toBe('Bearer access-2');
      retried.flush({});
    }

    await Promise.all(results);
  });

  it('signs the user out when the refresh token is itself rejected', async () => {
    signIn('expired', 'stolen-or-expired');

    const result = firstResult(http.get('/api/restaurants')).catch(() => 'failed');

    backend.expectOne('/api/restaurants').flush(null, { status: 401, statusText: 'Unauthorized' });
    await tick();

    backend
      .expectOne((r) => r.url.includes('/api/auth/refresh'))
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    await tick();

    expect(await result).toBe('failed');

    // Nothing is left to retry with, so continuing to hold a dead session would only produce
    // confusing failures on every later request.
    expect(session.isAuthenticated()).toBe(false);
    expect(session.refreshToken()).toBeNull();
  });

  it('does not attempt a refresh when there is no refresh token', async () => {
    // An access token with no refresh token: the 401 is the real answer.
    session.signIn({ accessToken: 'access-1', refreshToken: undefined });

    const result = firstResult(http.get('/api/restaurants')).catch(() => 'failed');

    backend.expectOne('/api/restaurants').flush(null, { status: 401, statusText: 'Unauthorized' });
    await tick();

    expect(await result).toBe('failed');
    backend.expectNone((r) => r.url.includes('/api/auth/refresh'));
  });

  it('leaves errors that are not 401 alone', async () => {
    signIn('access-1', 'refresh-1');

    const result = firstResult(http.get('/api/restaurants')).catch(() => 'failed');

    backend.expectOne('/api/restaurants').flush(null, { status: 500, statusText: 'Server Error' });
    await tick();

    expect(await result).toBe('failed');
    backend.expectNone((r) => r.url.includes('/api/auth/refresh'));
  });

  // ------------------------------------------------------------------ helpers

  function signIn(accessToken: string, refreshToken: string): void {
    session.signIn({ accessToken, refreshToken });
  }

  /** The generated client asks for a Blob response so it can read an error body. */
  function respondToRefresh(accessToken: string, refreshToken: string): void {
    respondToRefreshRequest(
      backend.expectOne((r) => r.url.includes('/api/auth/refresh')),
      accessToken,
      refreshToken,
    );
  }

  function respondToRefreshRequest(
    request: { flush(body: unknown): void },
    accessToken: string,
    refreshToken: string,
  ): void {
    const body = {
      accessToken,
      refreshToken,
      accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
      refreshTokenExpiresAt: new Date(Date.now() + 2_592_000_000).toISOString(),
    };
    request.flush(new Blob([JSON.stringify(body)], { type: 'application/json' }));
  }

  function firstResult<T>(source: { subscribe(observer: unknown): unknown }): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      source.subscribe({ next: resolve, error: reject });
    });
  }

  /**
   * The generated client decodes its Blob response with a FileReader, which settles across
   * several macrotask turns under jsdom. Waiting a fixed number of turns is what keeps these
   * deterministic rather than flaky on a slower machine.
   */
  async function tick(turns = 12): Promise<void> {
    for (let i = 0; i < turns; i++) {
      await new Promise((resolve) => setTimeout(resolve, 2));
    }
  }
});
