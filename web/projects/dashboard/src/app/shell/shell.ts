import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from 'auth';
import { map } from 'rxjs';
import { NAV_ITEMS, NavItem } from './navigation';

/**
 * The frame every signed-in screen sits inside: toolbar, navigation, content.
 *
 * It owns two decisions. Which sections this user can see — answered from the roles in their
 * token, so a staff member is never shown a Settings entry that would only refuse them. And how
 * the navigation behaves on a small screen, where a permanent sidebar would eat the width the
 * content needs.
 */
@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatToolbarModule,
    MatSidenavModule,
    MatListModule,
    MatIconModule,
    MatButtonModule,
    MatMenuModule,
    MatDividerModule,
    MatTooltipModule,
  ],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly breakpoints = inject(BreakpointObserver);

  protected readonly session = this.auth.session;

  /**
   * True on phones and small tablets. Below this the sidenav becomes an overlay that closes
   * behind you, because a 240px sidebar on a 360px screen leaves nothing to work in.
   */
  protected readonly isCompact = toSignal(
    this.breakpoints
      .observe([Breakpoints.XSmall, Breakpoints.Small])
      .pipe(map((result) => result.matches)),
    { initialValue: false },
  );

  protected readonly drawerOpen = signal(false);

  /** Only the sections this user's roles allow. The server enforces the same list again. */
  protected readonly sections = computed<readonly NavItem[]>(() =>
    NAV_ITEMS.filter((item) => !item.roles || this.session.hasAnyRole(...item.roles)),
  );

  /**
   * The signed-in user's initial, for the avatar button. Falls back to a neutral glyph rather
   * than rendering an empty circle if the token somehow carries no email.
   */
  protected readonly initial = computed(() => this.session.email()?.[0]?.toUpperCase() ?? '?');

  protected toggleDrawer(): void {
    this.drawerOpen.update((open) => !open);
  }

  /** On a small screen the drawer covers the content, so following a link must close it. */
  protected onNavigate(): void {
    if (this.isCompact()) {
      this.drawerOpen.set(false);
    }
  }

  protected async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
