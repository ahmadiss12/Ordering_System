import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from 'auth';
import { Observable, of, throwError } from 'rxjs';
import { Catalog } from '../catalog/catalog';
import { SignIn } from './sign-in';

/**
 * Signing in, from the customer's side.
 *
 * <p>
 * Almost nobody arrives at this screen on purpose. They were looking at a restaurant, or reaching
 * for their account, and a guard sent them through here on the way — so where it puts them
 * afterwards is most of what it does.
 * </p>
 */
describe('SignIn', () => {
  let fixture: ComponentFixture<SignIn>;
  let router: Router;
  let login: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    login = vi.fn().mockReturnValue(of(undefined));
  });

  it('takes a signed-in customer back where they were headed', async () => {
    await open('/login?returnUrl=%2Frestaurants%2Fsaj-corner');

    await submit('rita@example.test', 'Passw0rd!');

    expect(login).toHaveBeenCalledWith('rita@example.test', 'Passw0rd!');
    expect(router.url).toBe('/restaurants/saj-corner');
  });

  it('lands on the list when nothing sent them here', async () => {
    await open('/login');

    await submit('rita@example.test', 'Passw0rd!');

    expect(router.url).toBe('/');
  });

  it('ignores a return address that is not a path in this application', async () => {
    // Angular's router would not leave the origin with this anyway. Checking it here is what
    // keeps that true if the navigation is ever done with location.href instead.
    await open('/login?returnUrl=https:%2F%2Fexample.invalid%2Fpay');

    await submit('rita@example.test', 'Passw0rd!');

    expect(router.url).toBe('/');
  });

  it('trims the address, because a phone keyboard adds a space after it', async () => {
    await open('/login');

    await submit('  rita@example.test ', 'Passw0rd!');

    expect(login).toHaveBeenCalledWith('rita@example.test', 'Passw0rd!');
  });

  it('says why it is showing after a password change', async () => {
    // Without this the screen appears seconds after a save that worked, which reads as one that
    // did not.
    await open('/login?passwordChanged=1');

    expect(text()).toContain('Your password is changed');
  });

  it('reports a rejected sign-in without saying which half was wrong', async () => {
    login.mockReturnValue(throwError(() => new Error('401')));
    await open('/login');

    await submit('rita@example.test', 'wrong-password');

    const message = alertText();
    expect(message).toBeTruthy();
    expect(message?.toLowerCase()).not.toContain('no such');
    expect(message?.toLowerCase()).not.toContain('incorrect password');

    // The button has to come back, or a mistyped password locks somebody out of retrying.
    expect(submitButton().disabled).toBe(false);
  });

  it('does not call the server for an incomplete form', async () => {
    await open('/login');

    await submit('not-an-email', '');

    expect(login).not.toHaveBeenCalled();
  });

  // ------------------------------------------------------------------ helpers

  async function open(url: string): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [SignIn],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideRouter([
          { path: '', component: Catalog },
          { path: 'login', component: SignIn },
          { path: 'restaurants/:slug', children: [] },
          { path: 'register', children: [] },
          { path: 'forgot-password', children: [] },
        ]),
        { provide: AuthService, useValue: { login } },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    await router.navigateByUrl(url);

    fixture = TestBed.createComponent(SignIn);
    await fixture.whenStable();
  }

  async function submit(email: string, password: string): Promise<void> {
    setInput('input[type=email]', email);
    setInput('input[type=password]', password);

    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );
    await fixture.whenStable();
  }

  function setInput(selector: string, value: string): void {
    const input = fixture.nativeElement.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
  const alertText = () =>
    (
      fixture.nativeElement.querySelector('[role=alert]') as HTMLElement | null
    )?.textContent?.trim();
  const submitButton = () =>
    fixture.nativeElement.querySelector('button[type=submit]') as HTMLButtonElement;
});
