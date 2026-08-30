import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

/**
 * Where {@link roleGuard} sends someone who is signed in but lacks the role for a page.
 *
 * Distinct from the login redirect on purpose: this user is not anonymous and signing in again
 * will not help them, so offering a login form would send them round a loop. The honest answer
 * is that this account cannot see this page and someone with access has to grant it.
 */
@Component({
  selector: 'app-forbidden',
  imports: [RouterLink, MatButtonModule, MatIconModule],
  template: `
    <div class="shell">
      <mat-icon>lock</mat-icon>
      <h1>You do not have access to this page</h1>
      <p>Your account is signed in, but it is not allowed to open this part of the dashboard.</p>
      <p>If you think that is wrong, ask the restaurant owner to change your role.</p>
      <a matButton="filled" routerLink="/">Back to the dashboard</a>
    </div>
  `,
  styles: `
    .shell {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.5rem;
      max-width: 30rem;
      margin: 6rem auto;
      padding: 0 1rem;
      text-align: center;
    }

    mat-icon {
      font-size: 3rem;
      width: 3rem;
      height: 3rem;
      color: var(--mat-sys-outline);
    }

    h1 {
      font: var(--mat-sys-headline-small);
      margin: 0.5rem 0 0;
    }

    p {
      font: var(--mat-sys-body-medium);
      color: var(--mat-sys-on-surface-variant);
      margin: 0;
    }

    a {
      margin-top: 1rem;
    }
  `,
})
export class Forbidden {}
