import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { describeError } from 'api-client';
import { AuthService } from 'auth';
import { firstValueFrom } from 'rxjs';

/**
 * Signing in.
 *
 * <h4>One message for both halves</h4>
 *
 * The server does not say which of the email and the password was wrong, and neither does this.
 * Splitting them would turn the form into a way of finding out which addresses have accounts.
 *
 * <h4>Where it goes afterwards</h4>
 *
 * Back where the visitor was headed, not to the home page. Almost nobody arrives here on purpose
 * — they were looking at a restaurant, or reaching for their account, and the guard sent them
 * through this screen on the way. Landing them at the top of the list would make them find it
 * again.
 */
@Component({
  selector: 'app-sign-in',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
  ],
  templateUrl: './sign-in.html',
  styleUrl: './account.scss',
})
export class SignIn {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly builder = inject(FormBuilder);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /**
   * Set when the account screen sent somebody here after a password change.
   *
   * Without it this page appears seconds after a save that worked, which reads as one that did
   * not — the session really did end, and saying so is the difference between a rule and a bug.
   */
  protected readonly afterPasswordChange = signal(
    this.route.snapshot.queryParamMap.get('passwordChanged') === '1',
  );

  protected readonly form = this.builder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  /**
   * Where to go once signed in, from the <c>returnUrl</c> the guard attached.
   *
   * Only an in-application path is honoured. Angular's router would not leave the origin anyway —
   * it parses the string as a route rather than handing it to the browser — so this is not
   * closing an open redirect that exists today. It is making sure the one thing that could open
   * one later, a change to <c>location.href</c>, finds the value already checked.
   */
  private returnUrl(): string {
    const requested = this.route.snapshot.queryParamMap.get('returnUrl') ?? '';
    return requested.startsWith('/') && !requested.startsWith('//') ? requested : '/';
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    const { email, password } = this.form.getRawValue();

    try {
      await firstValueFrom(this.auth.login(email.trim(), password));
      await this.router.navigateByUrl(this.returnUrl());
    } catch (error) {
      this.error.set(describeError(error, 'That email and password do not match an account.'));
    } finally {
      this.busy.set(false);
    }
  }
}
