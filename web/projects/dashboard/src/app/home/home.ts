import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RestaurantSummary, RestaurantsClient } from 'api-client';
import { AuthService } from 'auth';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

/**
 * Still the placeholder screen from step 4, now behind a guard and showing who is signed in.
 * Replaced by the real shell in step 7.
 */
@Component({
  selector: 'app-home',
  imports: [
    MatToolbarModule,
    MatCardModule,
    MatChipsModule,
    MatIconModule,
    MatProgressBarModule,
    MatButtonModule,
  ],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  private readonly restaurantsClient = inject(RestaurantsClient);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly session = this.auth.session;
  protected readonly restaurants = signal<RestaurantSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected async signOut(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }

  private async load(): Promise<void> {
    try {
      const page = await firstValueFrom(this.restaurantsClient.list());
      this.restaurants.set(page.items ?? []);
    } catch {
      this.error.set('Could not reach the API. Is it running on port 5080?');
    } finally {
      this.loading.set(false);
    }
  }
}
