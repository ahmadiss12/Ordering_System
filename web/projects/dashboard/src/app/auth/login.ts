import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { AuthService } from 'auth';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-login',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
  ],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    const { email, password } = this.form.getRawValue();

    try {
      await firstValueFrom(this.auth.login(email, password));

      // Return the user where they were headed. Landing them on a dashboard they did not ask
      // for is a small thing that reads as the app not paying attention.
      const returnUrl = this.router.parseUrl(this.router.url).queryParams['returnUrl'] ?? '/';
      await this.router.navigateByUrl(returnUrl);
    } catch {
      // The server answers identically for a wrong password and an unknown account, so this
      // message must not imply which it was.
      this.error.set('That email and password do not match an account.');
    } finally {
      this.submitting.set(false);
    }
  }
}
