import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { MeClient, ProfileResponse } from 'api-client';
import { AuthService } from 'auth';
import { Observable, of, throwError } from 'rxjs';
import { Account } from './account';

/**
 * The account screen.
 *
 * <p>
 * Two of these are about consequences the person in front of the form cannot see. Changing a
 * password revokes every refresh token on the server, this browser's included — so the screen has
 * to end the session itself, or somebody sits on a page that starts failing a quarter of an hour
 * later for no visible reason. And the form has to be filled from the server rather than from the
 * token, or a name corrected on another device is silently put back by the next save.
 * </p>
 */
describe('Account', () => {
  let fixture: ComponentFixture<Account>;
  let router: Router;
  let logout: ReturnType<typeof vi.fn>;
  let client: FakeMeClient;

  beforeEach(async () => {
    logout = vi.fn().mockResolvedValue(undefined);
    client = new FakeMeClient();

    await TestBed.configureTestingModule({
      imports: [Account],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideRouter([
          { path: '', children: [] },
          { path: 'login', children: [] },
        ]),
        { provide: MeClient, useValue: client },
        {
          provide: AuthService,
          useValue: { logout, session: { email: () => 'rita@example.test' } },
        },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(Account);
    await fixture.whenStable();
  });

  it('fills the form from the server, not from the token', async () => {
    // The token was minted at sign-in. A name corrected since is not in it, so a form filled
    // from the token would show the old one and saving would put it back.
    expect(inputValue('input[formControlName=fullName]')).toBe('Rita');
    expect(inputValue('input[formControlName=phone]')).toBe('03 111 222');
    expect(text()).toContain('rita@example.test');
  });

  it('does not offer to save a form nobody has touched', () => {
    expect(saveButton().disabled).toBe(true);
  });

  it('saves the corrected details and stops offering to save them again', async () => {
    setInput('input[formControlName=phone]', '03 999 888');
    await fixture.whenStable();
    expect(saveButton().disabled).toBe(false);

    await submit(0);

    expect(client.updated).toEqual({ fullName: 'Rita', phone: '03 999 888' });
    expect(text()).toContain('Saved');
    expect(saveButton().disabled).toBe(true);
  });

  it('trims what it sends, so a stray space does not become part of a name', async () => {
    setInput('input[formControlName=fullName]', '  Rita Haddad  ');

    await submit(0);

    expect(client.updated?.fullName).toBe('Rita Haddad');
  });

  it('signs out and says why after a password change', async () => {
    await changePassword('Passw0rd!', 'correct1horse', 'correct1horse');

    expect(client.changed).toEqual({
      currentPassword: 'Passw0rd!',
      newPassword: 'correct1horse',
    });

    // Every refresh token is now revoked, this one included. Staying on the page would leave
    // somebody signed in until the access token lapsed and then bounce them mid-order.
    expect(logout).toHaveBeenCalled();
    expect(router.url).toBe('/login?passwordChanged=1');
  });

  it('stays put when the current password was wrong', async () => {
    client.failNext();

    await changePassword('not-my-password', 'correct1horse', 'correct1horse');

    // The dangerous failure: signing somebody out on a rejected change would end their session
    // as a punishment for a typo.
    expect(logout).not.toHaveBeenCalled();
    expect(router.url).toBe('/');
    expect(alertText()).toBeTruthy();
  });

  it('does not send a password the server would refuse', async () => {
    await changePassword('Passw0rd!', 'allletters', 'allletters');

    expect(client.changed).toBeNull();
    expect(text()).toContain('Add a number');
  });

  it('does not send a password typed differently the second time', async () => {
    await changePassword('Passw0rd!', 'correct1horse', 'correct1house');

    expect(client.changed).toBeNull();
    expect(text()).toContain('do not match');
  });

  // ------------------------------------------------------------------ helpers

  async function changePassword(current: string, next: string, confirm: string): Promise<void> {
    setInput('input[formControlName=current]', current);
    setInput('input[formControlName=next]', next);
    setInput('input[formControlName=confirm]', confirm);

    await submit(1);
  }

  async function submit(index: number): Promise<void> {
    const forms = fixture.nativeElement.querySelectorAll('form') as NodeListOf<HTMLFormElement>;
    forms[index].dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  function setInput(selector: string, value: string): void {
    const input = fixture.nativeElement.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
  }

  const inputValue = (selector: string) =>
    (fixture.nativeElement.querySelector(selector) as HTMLInputElement).value;
  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
  const alertText = () =>
    (
      fixture.nativeElement.querySelector('[role=alert]') as HTMLElement | null
    )?.textContent?.trim();
  const saveButton = () =>
    (fixture.nativeElement.querySelectorAll('form')[0] as HTMLFormElement).querySelector(
      'button[type=submit]',
    ) as HTMLButtonElement;

  class FakeMeClient {
    updated: { fullName: string; phone: string } | null = null;
    changed: { currentPassword: string; newPassword: string } | null = null;

    private failing = false;

    failNext(): void {
      this.failing = true;
    }

    get(): Observable<ProfileResponse> {
      return of({
        id: '11111111-1111-1111-1111-111111111111',
        email: 'rita@example.test',
        fullName: 'Rita',
        phone: '03 111 222',
        mustSetPassword: false,
        roles: ['Customer'],
      } as ProfileResponse);
    }

    update(body: { fullName: string; phone: string }): Observable<ProfileResponse> {
      this.updated = body;
      return of({
        id: '11111111-1111-1111-1111-111111111111',
        email: 'rita@example.test',
        mustSetPassword: false,
        roles: ['Customer'],
        ...body,
      } as ProfileResponse);
    }

    changePassword(body: { currentPassword: string; newPassword: string }): Observable<void> {
      if (this.failing) {
        this.failing = false;
        return throwError(() => new Error('that is not your current password'));
      }

      this.changed = body;
      return of(undefined as void);
    }
  }
});
