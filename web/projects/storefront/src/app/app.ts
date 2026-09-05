import { Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from 'auth';

/**
 * The storefront's frame: a name that goes home, and who you are signed in as.
 *
 * <h4>Two controls, not one that changes label</h4>
 *
 * A visitor who has never signed in gets a button that says <i>Sign in</i>, because an avatar
 * with a question mark in it is a puzzle rather than an invitation. Somebody signed in gets their
 * initial, which says <em>which</em> account is signed in — the question worth answering on a
 * shared phone.
 *
 * <h4>Why the avatar is a link and not a menu</h4>
 *
 * A <c>mat-menu</c> here was written first and then removed. It pulls the CDK overlay into the
 * initial bundle, which measured 17kB gzipped on every first load — paid by every visitor,
 * including the majority who are only browsing and never sign in — to save one tap for the ones
 * who do. The account page shows the same address and carries the sign-out button, and a full
 * page is easier to hit on a phone than a dropdown.
 *
 * <p>There is still no basket here. That is the next step, and a basket icon that does nothing
 * would be worse than no icon.</p>
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, MatToolbarModule, MatIconModule, MatButtonModule],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly auth = inject(AuthService);

  protected readonly session = this.auth.session;

  /**
   * The signed-in visitor's initial, for the avatar.
   *
   * From the email in the token rather than from their name: the name needs a request, and the
   * toolbar is drawn before any screen has made one. A neutral glyph rather than an empty circle
   * if the token somehow carries no address.
   */
  protected readonly initial = computed(() => this.session.email()?.[0]?.toUpperCase() ?? '?');
}
