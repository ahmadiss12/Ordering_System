import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from 'api-client';
import { AuthService } from 'auth';
import { firstValueFrom } from 'rxjs';
import {
  MIN_PASSWORD_LENGTH,
  PASSWORD_HINT,
  confirmationMatches,
  passwordValidators,
} from '../passwords/passwords';

/**
 * Choosing a password from a link in an email.
 *
 * <h4>Why this lives in a shared library</h4>
 *
 * Two different emails lead here and they go to different applications. A customer who has
 * forgotten their password lands on the storefront; somebody invited to run a restaurant lands on
 * the dashboard, because that is where the screens they were invited for are. The page itself is
 * the same act either way, and two copies of it would be two places for the password rules to
 * drift apart.
 *
 * <h4>The link had nowhere to go until now</h4>
 *
 * Every reset email since Phase 1 and every staff invitation since Phase 4 pointed at
 * <c>/reset-password</c>, and neither application had that route. The dashboard sent it to its
 * login page and dropped the token on the way. Nothing caught it because every test followed
 * those links through the API rather than through a browser.
 */
@Component({
  selector: 'lib-reset-password',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
  ],
  templateUrl: './reset-password.html',
  styleUrl: './reset-password.scss',
})
export class ResetPassword {
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);
  private readonly builder = inject(FormBuilder);

  protected readonly minLength = MIN_PASSWORD_LENGTH;
  protected readonly passwordHint = PASSWORD_HINT;

  protected readonly saving = signal(false);
  protected readonly done = signal(false);
  protected readonly error = signal<string | null>(null);

  /** The token out of the link. Absent means somebody typed the address, or the mail mangled it. */
  protected readonly token = signal(this.route.snapshot.queryParamMap.get('token') ?? '');

  /**
   * Whether this came from an invitation rather than a forgotten password.
   *
   * The act is identical; the words are not. "Reset your password" to somebody who has never had
   * one reads as though they have lost something they never owned.
   */
  protected readonly invited = signal(this.route.snapshot.queryParamMap.get('invited') === '1');

  protected readonly heading = computed(() =>
    this.invited() ? 'Choose a password' : 'Set a new password',
  );

  protected readonly form = this.builder.nonNullable.group(
    {
      password: ['', [...passwordValidators]],
      confirm: ['', Validators.required],
    },
    { validators: confirmationMatches('password', 'confirm') },
  );

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    try {
      await firstValueFrom(this.auth.resetPassword(this.token(), this.form.getRawValue().password));
      this.done.set(true);
    } catch (error) {
      this.error.set(
        describeError(error, 'That link did not work. It may have been used already or expired.'),
      );
    } finally {
      this.saving.set(false);
    }
  }
}
