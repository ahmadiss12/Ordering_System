import { Component, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router } from '@angular/router';
import { AuthService } from 'auth';
import { MIN_PASSWORD_LENGTH, PASSWORD_HINT, confirmationMatches, passwordValidators } from 'ui';
import { ProfileStore } from './profile-store';

/**
 * Your own details, and the two things you can change about them.
 *
 * <h4>Why the email is shown but not editable</h4>
 *
 * It is the account. Moving one to a new address is a different operation with its own proof — a
 * link sent to the new address, so a borrowed session cannot quietly take an account somewhere
 * its owner cannot reach. Rendering a box that looks editable and then refusing the save would be
 * worse than showing it plainly as what it is.
 *
 * <h4>Why changing a password signs you out</h4>
 *
 * The server revokes every refresh token when a password changes, this browser's included — that
 * is the whole point of the operation for somebody whose phone went missing. So the screen ends
 * the session itself rather than leaving somebody on a page that will start failing in fifteen
 * minutes for a reason they cannot see.
 */
@Component({
  selector: 'app-account',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
  ],
  providers: [ProfileStore],
  templateUrl: './account.html',
  styleUrl: './account.scss',
})
export class Account {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly builder = inject(FormBuilder);

  protected readonly store = inject(ProfileStore);
  protected readonly minLength = MIN_PASSWORD_LENGTH;
  protected readonly passwordHint = PASSWORD_HINT;

  protected readonly details = this.builder.nonNullable.group({
    fullName: ['', [Validators.required, Validators.maxLength(200)]],
    phone: ['', [Validators.required, Validators.maxLength(32)]],
  });

  protected readonly password = this.builder.nonNullable.group(
    {
      current: ['', Validators.required],
      next: ['', [...passwordValidators]],
      confirm: ['', Validators.required],
    },
    { validators: confirmationMatches('next', 'confirm') },
  );

  constructor() {
    // "Saved" is about the last save, not about the form. Leaving it up while somebody types the
    // next correction would tell them their unsaved edit is already stored.
    this.details.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.store.saved.set(false));

    void this.fill();
  }

  /**
   * Fills the form from the server rather than from the token.
   *
   * The token was minted at sign-in, so a name corrected since is not in it — the form would open
   * showing the old one, and saving would put it back.
   */
  private async fill(): Promise<void> {
    await this.store.load();

    const profile = this.store.profile();
    if (profile) {
      this.details.setValue({ fullName: profile.fullName, phone: profile.phone });
      this.details.markAsPristine();
    }
  }

  protected async saveDetails(): Promise<void> {
    if (this.details.invalid) {
      this.details.markAllAsTouched();
      return;
    }

    const { fullName, phone } = this.details.getRawValue();

    if (await this.store.save(fullName.trim(), phone.trim())) {
      // Pristine again, so the Save button goes back to being unavailable rather than inviting a
      // second identical write.
      this.details.markAsPristine();
    }
  }

  /**
   * Signing out lives here rather than in the toolbar, so the toolbar does not need a menu.
   * Home rather than the login page afterwards: signing out of a shop leaves you in the shop.
   */
  protected async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/');
  }

  protected async changePassword(): Promise<void> {
    if (this.password.invalid) {
      this.password.markAllAsTouched();
      return;
    }

    const { current, next } = this.password.getRawValue();
    if (!(await this.store.changePassword(current, next))) {
      return;
    }

    // Signed out locally, then the login page is told why it is showing. Without that note
    // somebody is dropped at a sign-in form seconds after a successful save, which reads as one
    // that failed.
    await this.auth.logout();
    await this.router.navigate(['/login'], { queryParams: { passwordChanged: 1 } });
  }
}
