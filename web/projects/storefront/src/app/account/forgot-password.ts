import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import { describeError } from 'api-client';
import { AuthService } from 'auth';
import { firstValueFrom } from 'rxjs';

/**
 * Asking for a link.
 *
 * <p>
 * The answer is the same whether or not the address has an account, because the server answers
 * that way on purpose: a form that said "no such account" would be a way of finding out who is
 * registered here. So the confirmation is worded as what was done — a link was sent if there is
 * an account — rather than as a promise that one is coming.
 * </p>
 */
@Component({
  selector: 'app-forgot-password',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
  ],
  templateUrl: './forgot-password.html',
  styleUrl: './account.scss',
})
export class ForgotPassword {
  private readonly auth = inject(AuthService);
  private readonly builder = inject(FormBuilder);

  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = this.builder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await firstValueFrom(this.auth.forgotPassword(this.form.getRawValue().email.trim()));
      this.sent.set(true);
    } catch (error) {
      this.error.set(describeError(error, 'Could not send the link. Try again in a moment.'));
    } finally {
      this.busy.set(false);
    }
  }
}
