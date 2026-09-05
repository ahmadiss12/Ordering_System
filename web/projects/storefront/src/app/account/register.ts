import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { describeError } from 'api-client';
import { AuthService } from 'auth';
import { firstValueFrom } from 'rxjs';
import { MIN_PASSWORD_LENGTH, PASSWORD_HINT, passwordValidators } from 'ui';

/**
 * Creating an account.
 *
 * <h4>Four fields, and each one is needed</h4>
 *
 * The phone number is not optional and is not there for marketing: it is what a courier rings
 * when they cannot find the building. Asking for it at sign-up rather than at checkout keeps the
 * one screen somebody is impatient on shorter.
 *
 * <h4>Signed in straight afterwards</h4>
 *
 * No "now please log in". Somebody who has just typed their password has proved they know it.
 */
@Component({
  selector: 'app-register',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  templateUrl: './register.html',
  styleUrl: './account.scss',
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly builder = inject(FormBuilder);

  protected readonly minLength = MIN_PASSWORD_LENGTH;
  protected readonly passwordHint = PASSWORD_HINT;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = this.builder.nonNullable.group({
    fullName: ['', [Validators.required, Validators.maxLength(200)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    phone: ['', [Validators.required, Validators.maxLength(32)]],
    password: ['', [...passwordValidators]],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    const { fullName, email, phone, password } = this.form.getRawValue();

    try {
      await firstValueFrom(
        this.auth.register(email.trim(), password, fullName.trim(), phone.trim()),
      );
      await this.router.navigateByUrl('/');
    } catch (error) {
      // The conflict is the one worth reading: an address that already has an account is not a
      // failure, it is somebody who has been here before and should sign in instead.
      this.error.set(describeError(error, 'Could not create your account.'));
    } finally {
      this.busy.set(false);
    }
  }
}
