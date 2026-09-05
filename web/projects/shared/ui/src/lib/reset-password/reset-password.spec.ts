import { provideZonelessChangeDetection } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from 'auth';
import { Observable, throwError } from 'rxjs';
import { ResetPassword } from './reset-password';

/**
 * The page at the end of an emailed link.
 *
 * <p>
 * Worth its own suite for a reason the code cannot show: for four phases this route did not
 * exist. Every reset email and every staff invitation pointed at it, the dashboard's catch-all
 * sent them to the login page, and the token went with the redirect. Nothing failed, because
 * every test until now followed those links through the API rather than through a browser.
 * These tests follow them the way a person does.
 * </p>
 */
describe('ResetPassword', () => {
  let fixture: ComponentFixture<ResetPassword>;
  let router: Router;
  let resetPassword: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    resetPassword = vi.fn().mockReturnValue(succeed());
  });

  it('sends the token out of the link, not one it invented', async () => {
    await open('?token=abc123');

    await submit('a-good-password1', 'a-good-password1');

    expect(resetPassword).toHaveBeenCalledWith('abc123', 'a-good-password1');
    expect(text()).toContain('Your password is set');
  });

  it('says the link is incomplete rather than showing a form that cannot work', async () => {
    // A mail client that wraps a long URL cuts the token off the end. Showing the form anyway
    // means somebody chooses a password, presses the button, and is told the link expired.
    await open('');

    expect(text()).toContain('incomplete');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(resetPassword).not.toHaveBeenCalled();
  });

  it('does not tell an invited owner to reset a password they have never had', async () => {
    await open('?token=abc123&invited=1');

    const shown = text();
    expect(shown).toContain('Choose a password');
    expect(shown.toLowerCase()).not.toContain('new password for your account');
  });

  it('asks a forgotten-password visitor for a new one, in those words', async () => {
    await open('?token=abc123');

    expect(text()).toContain('Set a new password');
  });

  it('refuses to submit when the two boxes disagree', async () => {
    await open('?token=abc123');

    await submit('a-good-password1', 'a-good-pasword1');

    // There is no way back from a typo in a masked box except another email, which is exactly
    // why the second box exists.
    expect(resetPassword).not.toHaveBeenCalled();
    expect(text()).toContain('do not match');
  });

  it('refuses a password the server would refuse anyway', async () => {
    await open('?token=abc123');

    await submit('short1', 'short1');

    expect(resetPassword).not.toHaveBeenCalled();
    expect(text()).toContain('At least 10 characters');
  });

  it('holds a long password that has no digit, because the server would refuse it', async () => {
    await open('?token=abc123');

    // The rule the three password forms all used to get wrong: length was checked, the letter
    // and the digit were not, so this reached the server and came back as a validation message.
    await submit('allletters', 'allletters');

    expect(resetPassword).not.toHaveBeenCalled();
    expect(text()).toContain('Add a number');
  });

  it('reports a spent link without claiming the password changed', async () => {
    resetPassword.mockReturnValue(throwError(() => new Error('400')));
    await open('?token=already-used');

    await submit('a-good-password1', 'a-good-password1');

    expect(text()).not.toContain('Your password is set');
    expect(alertText()).toBeTruthy();

    // The button has to come back, or a network blip locks somebody out of their own link.
    expect(submitButton().disabled).toBe(false);
  });

  // ------------------------------------------------------------------ helpers

  /** Navigates to the page with the given query string, so the component reads a real route. */
  async function open(query: string): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [ResetPassword],
      providers: [
        provideZonelessChangeDetection(),
        provideNoopAnimations(),
        provideRouter([
          { path: 'reset-password', component: ResetPassword },
          { path: 'login', children: [] },
        ]),
        { provide: AuthService, useValue: { resetPassword } },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    await router.navigateByUrl(`/reset-password${query}`);

    fixture = TestBed.createComponent(ResetPassword);
    await fixture.whenStable();
  }

  function succeed(): Observable<void> {
    return new Observable<void>((subscriber) => {
      subscriber.next(undefined);
      subscriber.complete();
    });
  }

  async function submit(password: string, confirm: string): Promise<void> {
    const boxes = fixture.nativeElement.querySelectorAll(
      'input[type=password]',
    ) as NodeListOf<HTMLInputElement>;

    setValue(boxes[0], password);
    setValue(boxes[1], confirm);

    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );
    await fixture.whenStable();
  }

  function setValue(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
  }

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
  const alertText = () =>
    (
      fixture.nativeElement.querySelector('[role=alert]') as HTMLElement | null
    )?.textContent?.trim();
  const submitButton = () =>
    fixture.nativeElement.querySelector('button[type=submit]') as HTMLButtonElement;
});
