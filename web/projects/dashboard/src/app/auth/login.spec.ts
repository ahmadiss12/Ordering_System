import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from 'auth';
import { Observable, throwError } from 'rxjs';
import { Login } from './login';

/**
 * The login screen, from the user's side of it.
 *
 * {@link AuthService} is stubbed on purpose: what tokens are and how they are renewed is already
 * covered by the interceptor tests in the auth library. What is left, and is only testable here,
 * is what the person in front of the form experiences — do they end up where they were going,
 * and does a wrong password tell them so without telling an attacker which half was wrong.
 */
describe('Login', () => {
  let fixture: ComponentFixture<Login>;
  let router: Router;
  let login: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    login = vi.fn();

    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideRouter([
          { path: 'login', component: Login },
          { path: 'menu', children: [] },
          { path: '', children: [] },
        ]),
        { provide: AuthService, useValue: { login } },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(Login);
    await fixture.whenStable();
  });

  it('sends the typed credentials and lands the user on the dashboard', async () => {
    login.mockReturnValue(succeed());

    await submitWith('owner@frieslab.test', 'Passw0rd!');

    expect(login).toHaveBeenCalledWith('owner@frieslab.test', 'Passw0rd!');
    expect(router.url).toBe('/');
  });

  it('returns the user to the page they were trying to reach', async () => {
    // The guard redirects here with ?returnUrl=… when an unauthenticated user asks for a page.
    // Ignoring it would drop them on a dashboard they did not ask for.
    await router.navigateByUrl('/login?returnUrl=%2Fmenu');
    login.mockReturnValue(succeed());

    await submitWith('owner@frieslab.test', 'Passw0rd!');

    expect(router.url).toBe('/menu');
  });

  it('reports a rejected sign-in without saying which half was wrong', async () => {
    login.mockReturnValue(throwError(() => new Error('401')));

    await submitWith('owner@frieslab.test', 'wrong-password');

    const message = errorText();
    expect(message).toBeTruthy();

    // Naming the email or the password would turn this form into an account-existence oracle.
    expect(message?.toLowerCase()).not.toContain('no such');
    expect(message?.toLowerCase()).not.toContain('incorrect password');
    expect(router.url).toBe('/');

    // The button has to come back, or a mistyped password locks the user out of retrying.
    expect(submitButton().disabled).toBe(false);
  });

  it('does not call the server for an incomplete form', async () => {
    await submitWith('not-an-email', '');

    expect(login).not.toHaveBeenCalled();
  });

  // ------------------------------------------------------------------ helpers

  /** A login that resolves, standing in for the real token exchange. */
  function succeed(): Observable<void> {
    return new Observable<void>((subscriber) => {
      subscriber.next(undefined);
      subscriber.complete();
    });
  }

  async function submitWith(email: string, password: string): Promise<void> {
    setInput('input[type=email]', email);
    setInput('input[type=password]', password);

    form().dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  function setInput(selector: string, value: string): void {
    const input = fixture.nativeElement.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  const form = () => fixture.nativeElement.querySelector('form') as HTMLFormElement;
  const submitButton = () =>
    fixture.nativeElement.querySelector('button[type=submit]') as HTMLButtonElement;
  const errorText = () =>
    (
      fixture.nativeElement.querySelector('[role=alert]') as HTMLElement | null
    )?.textContent?.trim();
});
