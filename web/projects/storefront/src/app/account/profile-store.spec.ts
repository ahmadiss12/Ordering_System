import { TestBed } from '@angular/core/testing';
import { MeClient, ProfileResponse } from 'api-client';
import { Observable, of, throwError } from 'rxjs';
import { ProfileStore } from './profile-store';

/**
 * The signed-in person's own details.
 *
 * <p>
 * Almost everything here is about not lying. A save that failed must not leave a tick beside the
 * button, and a load that failed must not leave an empty form that reads as an account with no
 * name in it.
 * </p>
 */
describe('ProfileStore', () => {
  let client: FakeMeClient;
  let store: ProfileStore;

  beforeEach(() => {
    client = new FakeMeClient();
    TestBed.configureTestingModule({
      providers: [ProfileStore, { provide: MeClient, useValue: client }],
    });
    store = TestBed.inject(ProfileStore);
  });

  it('holds what the server said, not what the caller asked for', async () => {
    // The server trims. A screen that kept its own copy would show the untrimmed one back.
    client.profile = profile({ fullName: 'Rita Haddad', phone: '+961 3 111 222' });

    await store.load();

    expect(store.profile()?.fullName).toBe('Rita Haddad');
    expect(store.loaded()).toBe(true);
    expect(store.loading()).toBe(false);
  });

  it('does not claim to have loaded when it could not', async () => {
    client.failNext();

    await store.load();

    // loaded() gates the whole screen. True here would draw an empty form over a failed load.
    expect(store.loaded()).toBe(false);
    expect(store.error()).toBeTruthy();
    expect(store.loading()).toBe(false);
  });

  it('replaces the held profile with the saved one', async () => {
    client.profile = profile({ fullName: 'Rita', phone: '03 111 222' });
    await store.load();

    client.profile = profile({ fullName: 'Rita Haddad', phone: '03 999 888' });
    const ok = await store.save('Rita Haddad', '03 999 888');

    expect(ok).toBe(true);
    expect(client.updated).toEqual({ fullName: 'Rita Haddad', phone: '03 999 888' });
    expect(store.profile()?.phone).toBe('03 999 888');
    expect(store.saved()).toBe(true);
  });

  it('does not show a tick for a save that failed', async () => {
    client.profile = profile({});
    await store.load();
    client.failNext();

    const ok = await store.save('Rita Haddad', '03 999 888');

    expect(ok).toBe(false);
    expect(store.saved()).toBe(false);
    expect(store.error()).toBeTruthy();

    // The old details stay on screen. Blanking them would look like the save deleted them.
    expect(store.profile()).not.toBeNull();
  });

  it('sends both halves of a password change', async () => {
    const ok = await store.changePassword('Passw0rd!', 'correct1horse');

    expect(ok).toBe(true);
    expect(client.changed).toEqual({
      currentPassword: 'Passw0rd!',
      newPassword: 'correct1horse',
    });
  });

  it('keeps a rejected password change out of the details error', async () => {
    client.profile = profile({});
    await store.load();
    client.failNext();

    const ok = await store.changePassword('wrong', 'correct1horse');

    expect(ok).toBe(false);
    expect(store.passwordError()).toBeTruthy();

    // Two forms on one screen. A wrong current password must not put a message under Save.
    expect(store.error()).toBeNull();
    expect(store.passwordChanged()).toBe(false);
  });

  // ------------------------------------------------------------------ helpers

  function profile(overrides: Partial<ProfileResponse>): ProfileResponse {
    return {
      id: '11111111-1111-1111-1111-111111111111',
      email: 'rita@example.test',
      fullName: 'Rita',
      phone: '03 111 222',
      mustSetPassword: false,
      roles: ['Customer'],
      ...overrides,
    } as ProfileResponse;
  }

  /**
   * Answers reads from what it was given and records writes separately.
   *
   * Deliberately not one field for both: a fake that answered a read from what the last write
   * passed it would agree with the store no matter what the store did with the response.
   */
  class FakeMeClient {
    profile: ProfileResponse = {
      id: '11111111-1111-1111-1111-111111111111',
      email: 'rita@example.test',
      fullName: 'Rita',
      phone: '03 111 222',
      mustSetPassword: false,
      roles: ['Customer'],
    } as ProfileResponse;

    updated: { fullName: string; phone: string } | null = null;
    changed: { currentPassword: string; newPassword: string } | null = null;

    private failing = false;

    failNext(): void {
      this.failing = true;
    }

    get(): Observable<ProfileResponse> {
      return this.answer(() => this.profile);
    }

    update(body: { fullName: string; phone: string }): Observable<ProfileResponse> {
      this.updated = body;
      return this.answer(() => this.profile);
    }

    changePassword(body: { currentPassword: string; newPassword: string }): Observable<void> {
      this.changed = body;
      return this.answer(() => undefined as void);
    }

    private answer<T>(value: () => T): Observable<T> {
      if (this.failing) {
        this.failing = false;
        return throwError(() => new Error('the server said no'));
      }
      return of(value());
    }
  }
});
