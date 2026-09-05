import { Component } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterOutlet } from '@angular/router';

/**
 * The storefront's frame.
 *
 * <p>
 * Deliberately almost nothing yet: a name that goes home and the page under it. There is no sign
 * in, no basket and no account menu here, because none of them exists — and a toolbar full of
 * controls that do nothing is worse than a plain one.
 * </p>
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, MatToolbarModule, MatIconModule],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {}
